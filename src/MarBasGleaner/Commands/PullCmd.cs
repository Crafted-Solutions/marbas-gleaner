using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using MarBasGleaner.Json;
using MarBasGleaner.Tracking;
using MarBasSchema.Grain;
using MarBasSchema.Transport;

namespace MarBasGleaner.Commands
{
    internal class PullCmd: GenericCmd
    {
        public PullCmd()
            : base("pull", "Pulls modified and new grains from MarBas broker into snapshot")
        {
            Setup();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments", Justification = "The Setup() method is meant to be called once per lifetime")]
        protected override void Setup()
        {
            base.Setup();
            AddOption(new Option<bool>(new[] { "-o", "--overwrite" }, "Always overwrite grains in snapshot even newer ones"));
            AddOption(new Option<bool>("--force-checkpoint", "Create new checkpoint even when it's safe using latest existing one"));
        }

        public new class Worker(ITrackingService trackingService, ILogger<Worker> logger) : GenericCmd.Worker(trackingService, (ILogger)logger)
        {
            public bool Overwrite { get; set; }
            public bool ForceCheckpoint { get; set; }

            public override async Task<int> InvokeAsync(InvocationContext context)
            {
                var ctoken = context.GetCancellationToken();
                var snapshotDir = await _trackingService.GetSnapshotDirectoryAsync(Directory, ctoken);

                var result = CheckSnapshot(snapshotDir);
                if (0 != result)
                {
                    return result;
                }

                using var client = _trackingService.GetBrokerClient(snapshotDir.ConnectionSettings!);

                var brokerStat = await CheckBrokerConnection(client, snapshotDir.Snapshot?.SchemaVersion, ctoken);
                if (CmdResultCode.Success != brokerStat.Code)
                {
                    return (int)brokerStat.Code;
                }

                DisplayMessage($"Pulling grains from {client.APIUrl} into snapshot {snapshotDir.FullPath}", MessageSeparatorOption.After);

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
                var conflated = await snapshotDir.LoadConflatedCheckpoint(ctoken);
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
                            DisplayMessage($"Skippin deleted grain {brokerGrain.Id:D}");
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

                var imports = await client.ImportGrains(incoming.Keys, ctoken);
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
                    DisplayInfo($"Snapshot {snapshotDir.FullPath} is already uptodate");
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

                    DisplayMessage($"Integrated {changes} changes into checkpont {targetCheckpoint.Ordinal} of snapshot {snapshotDir.FullPath}", MessageSeparatorOption.Before);
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
                
                DisplayMessage($"Both versions of grain {brokerGrain.Id:D} ({brokerGrain.Path ?? " / "}) have been modified (snapshot timestamp: {localGrain.MTime:O}, broker timestamp: {brokerGrain.MTime:O})");
                bool choiceComplete;
                do
                {
                    DisplayMessage("Please decide how to procede");
                    DisplayMessage("   1. Keep snapshot version as current");
                    DisplayMessage("   2. Overwrite snapshot grain with the broker version");
                    DisplayMessage("   3. Save broker version into a temporary file and resolve conflict manually");
                    DisplayMessage("   4. Display differences and decide again");

                    int choice;
                    while (!Int32.TryParse(Console.ReadLine(), out choice) || 1 > choice || 4 < choice)
                    {
                        DisplayMessage("Please choose an option 1-4");
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
                            DisplayMessage($"Grain {brokerGrain.Id:D} stored to {path}");
                            break;
                        case 4:
                            DisplayDiff(localGrain, brokerGrain);
                            break;
                    }

                } while (!choiceComplete);

                return result;
            }

            private static void DisplayDiff(IGrainTransportable localGrain, IGrainTransportable brokerGrain)
            {
                var localText = JsonSerializer.Serialize(localGrain, JsonDefaults.SerializationOptions);
                var brokerText = JsonSerializer.Serialize(brokerGrain, JsonDefaults.SerializationOptions);

                var diffBuilder = new InlineDiffBuilder(new Differ());
                var diff = diffBuilder.BuildDiffModel(localText, brokerText);

                foreach (var line in diff.Lines)
                {
                    Console.ForegroundColor = ConsoleColor.Blue;
                    if (line.Position.HasValue) Console.Write(line.Position.Value);
                    Console.Write('\t');
                    switch (line.Type)
                    {
                        case ChangeType.Inserted:
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.Write("+ ");
                            break;
                        case ChangeType.Deleted:
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.Write("- ");
                            break;
                        default:
                            Console.ForegroundColor = ConsoleColor.Gray;
                            Console.Write("  ");
                            break;
                    }

                    Console.Write(line.Text);
                    Console.Write(Environment.NewLine);
                }
                Console.ResetColor();
            }

            private static async Task ImportGrain(SnapshotDirectory snapshotDir, IGrainTransportable grain, GrainTrackingStatus status, SnapshotCheckpoint checkpoint, CancellationToken cancellationToken = default)
            {
                DisplayMessage($"{(GrainTrackingStatus.New == status ? "Pulling missing " : "Updating ")}grain {grain.Id:D} ({grain.Path ?? " / "})");
                await snapshotDir.StoreGrain(grain, false, cancellationToken: cancellationToken);
                UpdateCheckpoint(checkpoint, grain, status);
            }

            private static void PurgeGrain(SnapshotDirectory snapshotDir, IGrain grain, SnapshotCheckpoint checkpoint)
            {
                DisplayMessage($"Purging deleted grain {grain.Id:D} ({grain.Path ?? " / "})");
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
                    if (GrainTrackingStatus.New == status)
                    {
                        checkpoint.Additions.Add(grain.Id);
                    }
                    if (checkpoint.Latest < grain.MTime)
                    {
                        checkpoint.Latest = grain.MTime;
                    }
                }
            }
        }
    }
}
