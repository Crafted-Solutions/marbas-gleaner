using System.CommandLine;
using System.CommandLine.Invocation;
using MarBasGleaner.Tracking;
using MarBasSchema.Grain;
using MarBasSchema.Transport;

namespace MarBasGleaner.Commands
{
    internal class PullCmd: GenericCmd
    {
        public PullCmd()
            : base("pull", PullCmdL10n.CmdDesc)
        {
            Setup();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments", Justification = "The Setup() method is meant to be called once per lifetime")]
        protected override void Setup()
        {
            base.Setup();
            AddOption(new Option<bool>(new[] { "-o", "--overwrite" }, PullCmdL10n.OverwriteOptionDesc));
            AddOption(new Option<bool>("--force-checkpoint", PullCmdL10n.ForceCheckpointOptionDesc));
        }

        public new class Worker(ITrackingService trackingService, ILogger<Worker> logger) : GenericCmd.Worker(trackingService, (ILogger)logger)
        {
            public bool Overwrite { get; set; }
            public bool ForceCheckpoint { get; set; }

            public override async Task<int> InvokeAsync(InvocationContext context)
            {
                var ctoken = context.GetCancellationToken();
                var snapshotDir = await _trackingService.GetSnapshotDirectoryAsync(Directory, ctoken);

                var result = ValidateSnapshot(snapshotDir);
                if (0 != result)
                {
                    return result;
                }

                using var client = _trackingService.GetBrokerClient(snapshotDir.ConnectionSettings!);

                var brokerStat = await ValidateBrokerConnection(client, snapshotDir.Snapshot?.SchemaVersion, snapshotDir.BrokerInstanceId, ctoken);
                if (CmdResultCode.Success != brokerStat.Code)
                {
                    return (int)brokerStat.Code;
                }

                DisplayMessage(string.Format(PullCmdL10n.MsgCmdStart, client.APIUrl, snapshotDir.FullPath), MessageSeparatorOption.After);

                var isSafeCheckpoint = snapshotDir.LocalCheckpoint!.IsSame(snapshotDir.SharedCheckpoint);
                var targetCheckpoint = ForceCheckpoint || !isSafeCheckpoint
                    ? new SnapshotCheckpoint() 
                    {
                        InstanceId = snapshotDir.LocalCheckpoint.InstanceId,
                        Ordinal = snapshotDir.SharedCheckpoint!.Ordinal + 1,
                        Latest = snapshotDir.LocalCheckpoint.Latest
                    }
                    : snapshotDir.LocalCheckpoint;

                var rootId = (Guid)(snapshotDir.Snapshot?.AnchorId!);
               
                var brokerGrains = await client.ListGrains(rootId, SnapshotScope.Recursive == (SnapshotScope.Recursive & snapshotDir.Snapshot.Scope),
                    mtimeFrom: snapshotDir.LocalCheckpoint.Latest, includeParent: SnapshotScope.Anchor == (SnapshotScope.Anchor & snapshotDir.Snapshot.Scope), cancellationToken: ctoken);

                int changes = 0;
                var conflated = await snapshotDir.LoadConflatedCheckpoint(cancellationToken: ctoken);
                var pathsToCheck = new HashSet<string>();
                var incoming = new Dictionary<Guid, (GrainTrackingStatus local, GrainTrackingStatus broker)>();

                foreach (var brokerGrain in brokerGrains)
                {
                    if (snapshotDir.IsIgnoredGrain(brokerGrain))
                    {
                        continue;
                    }

                    var localGrain = await snapshotDir.LoadGrainById<GrainPlain>(brokerGrain.Id, cancellationToken: ctoken);

                    if (null == localGrain)
                    {
                        if (conflated.Deletions.Contains(brokerGrain.Id))
                        {
                            DisplayMessage(string.Format(PullCmdL10n.StatusGrainDeleted, brokerGrain.Id));
                        }
                        else
                        {
                            incoming[brokerGrain.Id] = (GrainTrackingStatus.Missing, GrainTrackingStatus.New);
                            changes++;
                        }
                    }
                    else
                    {
                        incoming[brokerGrain.Id] = (Overwrite || localGrain.MTime <= brokerGrain.MTime ? GrainTrackingStatus.Uptodate : GrainTrackingStatus.Modified, GrainTrackingStatus.Modified);
                        pathsToCheck.Add($"{brokerGrain.Path ?? string.Empty}/");
                        changes++;
                    }
                }

                var imports = await client.PullGrains(incoming.Keys, ctoken);
                foreach (var grain in imports)
                {
                    var import = true;
                    var (localStatus, brokerStatus) = incoming[grain.Id];
                    if (GrainTrackingStatus.Modified == brokerStatus && GrainTrackingStatus.Modified == localStatus)
                    {
                        import = await ResolveConflict(snapshotDir, grain, targetCheckpoint, ctoken);
                    }
                    if (import)
                    {
                        await ImportGrain(snapshotDir, grain, brokerStatus, targetCheckpoint, ctoken);
                    }
                }

                if (0 < pathsToCheck.Count)
                {
                    var childrenToCheck = new Dictionary<Guid, IGrain>();
                    await foreach (var grain in snapshotDir.ListGrains<GrainTransportable>((id) =>
                        {
                            return !incoming.ContainsKey(id);
                        }, ctoken))
                    {
                        if (null != grain && pathsToCheck.Any((x) => !string.IsNullOrEmpty(grain.Path) && grain.Path.StartsWith(x)))
                        {
                            childrenToCheck[grain.Id] = grain;
                        }
                    }
                    var checks = await client.CheckGrainsExist(childrenToCheck.Keys, ctoken);

                    foreach (var del in checks.Where(x => !x.Value))
                    {
                        PurgeGrain(snapshotDir, childrenToCheck[del.Key], targetCheckpoint);
                        changes++;
                    }
                }

                if (0 == changes)
                {
                    DisplayInfo(string.Format(PullCmdL10n.MsgCmdSuccessNoop, snapshotDir.FullPath));
                }
                else
                {
                    snapshotDir.Snapshot.Updated = DateTime.UtcNow;
                    snapshotDir.Snapshot.Checkpoint = targetCheckpoint.Ordinal;

                    if (isSafeCheckpoint && snapshotDir.LastPushCheckpoint == snapshotDir.SharedCheckpoint!.Ordinal)
                    {
                        snapshotDir.LastPushCheckpoint = targetCheckpoint.Ordinal;
                    }

                    await snapshotDir.StoreCheckpoint(targetCheckpoint, true, ctoken);
                    await snapshotDir.StoreMetadata(false, ctoken);

                    DisplayMessage(string.Format(PullCmdL10n.MsgCmdSuccess, changes, targetCheckpoint.Ordinal, snapshotDir.FullPath), MessageSeparatorOption.Before);
                }

                return result;
            }

            private static async Task<bool> ResolveConflict(SnapshotDirectory snapshotDir, IGrainTransportable brokerGrain, SnapshotCheckpoint checkpoint, CancellationToken cancellationToken = default)
            {
                var result = false;
                var localGrain = await snapshotDir.LoadGrainById<GrainTransportable>(brokerGrain.Id, cancellationToken: cancellationToken);
                if (null == localGrain)
                {
                    throw new ApplicationException($"Grain {brokerGrain.Id:D} not found in snapshot");
                }
                if (cancellationToken.IsCancellationRequested)
                {
                    return result;
                }
                
                DisplayMessage(string.Format(PullCmdL10n.StatusGrainConflict, brokerGrain.Id, brokerGrain.Path ?? " / ", localGrain.MTime, brokerGrain.MTime));
                bool choiceComplete;
                do
                {
                    DisplayMessage(string.Format(PullCmdL10n.ChoiceGrainConflict, Environment.NewLine));

                    int choice;
                    while (!Int32.TryParse(Console.ReadLine(), out choice) || 1 > choice || 4 < choice)
                    {
                        DisplayMessage(PullCmdL10n.MsgChooseOf4);
                    }
                    choiceComplete = 4 != choice;
                    
                    switch (choice)
                    {
                        case 1:
                            localGrain.MTime = brokerGrain.MTime;
                            await ImportGrain(snapshotDir, localGrain, GrainTrackingStatus.Modified, checkpoint, cancellationToken);
                            break;
                        case 2:
                            result = true;
                            break;
                        case 3:
                            var path = await snapshotDir.StoreGrain(brokerGrain, false, true, cancellationToken);
                            DisplayMessage(string.Format(PullCmdL10n.StatusGrainStored, brokerGrain.Id, path));
                            break;
                        case 4:
                            DiffCmd.DisplayDiff(localGrain, brokerGrain);
                            break;
                    }

                } while (!choiceComplete);

                return result;
            }

            private static async Task ImportGrain(SnapshotDirectory snapshotDir, IGrainTransportable grain, GrainTrackingStatus status, SnapshotCheckpoint checkpoint, CancellationToken cancellationToken = default)
            {
                DisplayMessage(string.Format(GrainTrackingStatus.New == status ? PullCmdL10n.StatusGrainPull : PullCmdL10n.StatusGrainUpdate, grain.Id, grain.Path ?? " / "));
                await snapshotDir.StoreGrain(grain, false, cancellationToken: cancellationToken);
                UpdateCheckpoint(checkpoint, grain, status);
            }

            private static void PurgeGrain(SnapshotDirectory snapshotDir, IGrain grain, SnapshotCheckpoint checkpoint)
            {
                DisplayMessage(string.Format(PullCmdL10n.StatusGrainPurge, grain.Id, grain.Path ?? " / "));
                snapshotDir.DeleteGrains(new[] { grain });
                UpdateCheckpoint(checkpoint, grain, GrainTrackingStatus.Deleted);
            }

            private static void UpdateCheckpoint(SnapshotCheckpoint checkpoint, IGrain grain, GrainTrackingStatus status)
            {
                if (GrainTrackingStatus.Deleted == status)
                {
                    checkpoint.Deletions.Add(grain.Id);
                }
                else
                {
                    checkpoint.Modifications.Add(grain.Id);
                    if (checkpoint.Latest < grain.MTime)
                    {
                        checkpoint.Latest = grain.MTime;
                    }
                }
            }
        }
    }
}
