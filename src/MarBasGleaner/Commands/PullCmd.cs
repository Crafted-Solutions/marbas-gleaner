using CraftedSolutions.MarBasGleaner.Tracking;
using CraftedSolutions.MarBasGleaner.UI;
using CraftedSolutions.MarBasSchema.Grain;
using CraftedSolutions.MarBasSchema.Transport;
using diVISION.CommandLineX;
using System.CommandLine;

namespace CraftedSolutions.MarBasGleaner.Commands
{
    internal class PullCmd : GenericCmd
    {
        public static readonly Option<bool> OverwriteOption = new("--overwrite", "-o")
        {
            Description = PullCmdL10n.OverwriteOptionDesc
        };
        public static readonly Option<bool> ForceCheckpointOption = new("--force-checkpoint")
        {
            Description = PullCmdL10n.ForceCheckpointOptionDesc
        };

        public PullCmd()
            : base("pull", PullCmdL10n.CmdDesc)
        {
            Setup();
        }

        protected override void Setup()
        {
            base.Setup();
            Add(OverwriteOption);
            Add(ForceCheckpointOption);
        }

        public new sealed class Worker : GenericCmd.Worker
        {
            public bool Overwrite { get; set; }
            public bool ForceCheckpoint { get; set; }

            public Worker(ITrackingService trackingService, ILogger<Worker> logger)
                : this(trackingService, (ILogger)logger)
            {
            }

            internal Worker(ITrackingService trackingService, ILogger logger)
                : base(trackingService, logger)
            {
            }

            public override async Task<int> InvokeAsync(CommandActionContext context, CancellationToken cancellationToken = default)
            {
                var snapshotDir = await _trackingService.GetSnapshotDirectoryAsync(Directory, cancellationToken);

                var result = ValidateSnapshot(snapshotDir);
                if (0 != result)
                {
                    return result;
                }

                var brokerStat = await ValidateBrokerConnection(_trackingService, snapshotDir.ConnectionSettings!, snapshotDir.Snapshot?.SchemaVersion, snapshotDir.BrokerInstanceId, cancellationToken);
                if (CmdResultCode.Success != brokerStat.Code)
                {
                    return (int)brokerStat.Code;
                }

                using var client = await _trackingService.GetBrokerClientAsync(snapshotDir.ConnectionSettings!, cancellationToken);
                await snapshotDir.StoreLocalState(false, cancellationToken);

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

                var rootId = (Guid)snapshotDir.Snapshot?.AnchorId!;

                var brokerGrains = await client.ListGrains(rootId, SnapshotScope.Recursive == (SnapshotScope.Recursive & snapshotDir.Snapshot.Scope),
                    mtimeFrom: snapshotDir.LocalCheckpoint.Latest, includeParent: SnapshotScope.Anchor == (SnapshotScope.Anchor & snapshotDir.Snapshot.Scope), cancellationToken: cancellationToken);

                int changes = 0;
                var conflated = await snapshotDir.LoadConflatedCheckpoint(cancellationToken: cancellationToken);
                var pathsToCheck = new HashSet<string>();
                var incoming = new Dictionary<Guid, (GrainTrackingStatus local, GrainTrackingStatus broker)>();

                foreach (var brokerGrain in brokerGrains)
                {
                    if (snapshotDir.IsIgnoredGrain(brokerGrain))
                    {
                        continue;
                    }

                    var localGrain = await snapshotDir.LoadGrainById<GrainPlain>(brokerGrain.Id, cancellationToken: cancellationToken);

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

                var pages = (int)Math.Ceiling((decimal)incoming.Count / 100);
                for (var page = 0; page < pages; page++)
                {
                    var block = 1 < pages ? incoming.Keys.Skip(page * 100).Take(100) : incoming.Keys;
                    var imports = await client.PullGrains(block, cancellationToken);
                    foreach (var grain in imports)
                    {
                        var import = true;
                        var (localStatus, brokerStatus) = incoming[grain.Id];
                        if (GrainTrackingStatus.Modified == brokerStatus && GrainTrackingStatus.Modified == localStatus)
                        {
                            import = await ResolveConflict(snapshotDir, grain, targetCheckpoint, cancellationToken);
                        }
                        if (import)
                        {
                            await ImportGrain(snapshotDir, grain, brokerStatus, targetCheckpoint, cancellationToken);
                        }
                    }
                }

                if (0 < pathsToCheck.Count)
                {
                    var childrenToCheck = new Dictionary<Guid, IGrain>();
                    await foreach (var grain in snapshotDir.ListGrains<GrainTransportable>((id) =>
                        {
                            return !incoming.ContainsKey(id);
                        }, cancellationToken))
                    {
                        if (null != grain && pathsToCheck.Any((x) => !string.IsNullOrEmpty(grain.Path) && grain.Path.StartsWith(x)))
                        {
                            childrenToCheck[grain.Id] = grain;
                        }
                    }
                    var checks = await client.CheckGrainsExist(childrenToCheck.Keys, cancellationToken);

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
                    if (isSafeCheckpoint)
                    {
                        targetCheckpoint.InstanceId = (Guid)snapshotDir.BrokerInstanceId!;
                    }
                    await snapshotDir.StoreCheckpoint(targetCheckpoint, true, cancellationToken);
                    await snapshotDir.StoreMetadata(false, cancellationToken);

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
                    while (!int.TryParse(Console.ReadLine(), out choice) || 1 > choice || 4 < choice)
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
                    checkpoint.Modifications.Remove(grain.Id);
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
