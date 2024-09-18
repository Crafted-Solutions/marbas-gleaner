using System.CommandLine;
using System.CommandLine.Invocation;
using MarBasGleaner.Tracking;
using MarBasSchema.Grain;
using MarBasSchema.Transport;

namespace MarBasGleaner.Commands
{

    internal class StatusCmd : GenericCmd
    {
        public enum GrainStatus
        {
            Uptodate, Missing, Obscure, Ignored, Modified, New, Deleted
        }

        public StatusCmd()
            : base("status", "Shows status of MarBas grains in a tracking snapshot")
        {
            Setup();
        }

        protected override void Setup()
        {
            base.Setup();
            AddOption(new Option<bool>("--show-all", "List all grains, even unmodified ones"));
        }

        public new class Worker(ITrackingService trackingService, ILogger<Worker> logger) : GenericCmd.Worker(trackingService, (ILogger)logger)
        {

            public bool ShowAll { get; set; }

            public override async Task<int> InvokeAsync(InvocationContext context)
            {
                var ctoken = context.GetCancellationToken();
                var snapshotDir = await _trackingService.GetSnapshotDirectoryAsync(Directory, ctoken);

                var result = CheckSnapshot(snapshotDir);
                if (0 != result)
                {
                    return result;
                }
                if (null == snapshotDir.LocalCheckpoint || null == snapshotDir.SharedCheckpoint)
                {
                    return ReportError(CmdResultCode.SnapshotStateError, $"Snapshot checkpoints are missing, delete {SnapshotDirectory.LocalStateFile} and execute 'connect' command");
                }

                using var client = _trackingService.GetBrokerClient(snapshotDir.ConnectionSettings!);

                var brokerStat = await CheckBrokerConnection(client, snapshotDir.SharedSnapshot?.SchemaVersion, ctoken);
                if (CmdResultCode.Success != brokerStat.Code)
                {
                    return (int)brokerStat.Code;
                }

                DisplayMessage($"Comparing snapshot {snapshotDir.FullPath} with {client.APIUrl}", MessageSeparatorOption.After);

                bool isCheckpointInSync = snapshotDir.LocalCheckpoint.IsSame(snapshotDir.SharedCheckpoint);
                if (!isCheckpointInSync)
                {
                    DisplayWarning("Snapshot has been modified externally, results may be inaccurate");
                }

                var rootId = (Guid)(snapshotDir.LocalSnapshot?.AnchorId!);

                var brokerMods = await client.ListGrains(rootId, SnapshotScope.Recursive == (snapshotDir.LocalSnapshot.Scope & SnapshotScope.Recursive),
                    mtimeFrom: snapshotDir.LocalCheckpoint.Latest, includeParent: SnapshotScope.Anchor == (SnapshotScope.Anchor & snapshotDir.LocalSnapshot.Scope), cancellationToken: ctoken);
                var brokerModHash = new Dictionary<Guid, IGrain>(brokerMods.Select(x => new KeyValuePair<Guid, IGrain>(x.Id, x)));

                var intCheckpoint = await snapshotDir.LoadIntegratedCheckpoint(ctoken);

                await foreach (var grain in snapshotDir.ListGrains<GrainTransportable>(ctoken))
                {
                    if (null != grain)
                    {
                        var status = new[] { GrainStatus.Uptodate, GrainStatus.Uptodate };
                        if (brokerModHash.ContainsKey(grain.Id))
                        {
                            status[1] = GrainStatus.Modified;
                            if (brokerModHash[grain.Id].MTime < grain.MTime)
                            {
                                status[0] = GrainStatus.Modified;
                            }
                            brokerModHash.Remove(grain.Id);
                        }

                        if (!intCheckpoint.Additions.Contains(grain.Id))
                        {
                            status[0] = snapshotDir.SharedCheckpoint.Additions.Contains(grain.Id) ? GrainStatus.New : GrainStatus.Obscure;
                        }
                        else if (intCheckpoint.Deletions.Contains(grain.Id))
                        {
                            status[0] = GrainStatus.Obscure;
                        }
                        else if (grain.MTime > snapshotDir.LocalCheckpoint.Latest)
                        {
                            status[0] = GrainStatus.Modified;
                        }

                        if (0 == result && GrainStatus.Uptodate < (status[0] | status[1]))
                        {
                            result = (int)CmdResultCode.SnapshotStatusOutofdate;
                        }
                        PrintGrainInfo(grain, status[0], status[1]);

                        intCheckpoint.Additions.Remove(grain.Id);
                        intCheckpoint.Deletions.Remove(grain.Id);
                    }
                }

                static IGrain DeletedGrain(Guid id)
                {
                    return new GrainPlain()
                    {
                        Id = id,
                        Name = $"Deleted-{id:D}",
                        Path = "~"
                    };
                };

                foreach (var id in intCheckpoint.Additions)
                {
                    var grain = await client.GetGrain(id, ctoken);
                    if (null == grain)
                    {
                        PrintGrainInfo(DeletedGrain(id), statusBroker: GrainStatus.Deleted);
                    }
                    else
                    {
                        PrintGrainInfo(grain, GrainStatus.Missing);
                    }
                }
                foreach (var id in intCheckpoint.Deletions)
                {
                    var grain = await client.GetGrain(id, ctoken);
                    if (null != grain)
                    {
                        PrintGrainInfo(grain, GrainStatus.Deleted);
                    }
                }

                foreach (var entry in brokerModHash)
                {
                    PrintGrainInfo(entry.Value, statusBroker: snapshotDir.IsIgnoredGrain(entry.Value) ? GrainStatus.Ignored : GrainStatus.New);
                }
                if (0 == result)
                {
                    DisplayInfo($"Snapshot {snapshotDir.LocalSnapshot.InstanceId:D} is uptodate");
                }
                else
                {
                    DisplayMessage("Status legend (left side - snapshot, right side - broker):", MessageSeparatorOption.Before);
                    var legend = string.Empty;
                    for (var s = GrainStatus.Missing; s <= GrainStatus.Deleted; s++)
                    {
                        if (0 < legend.Length)
                        {
                            legend += ", ";
                        }
                        legend += $"[{GetStatusIndicator(s)}] - {Enum.GetName(s)}";
                    }
                    DisplayMessage(legend);
                }
                return result;
            }

            private void PrintGrainInfo(IGrain grain, GrainStatus statusSnapshot = GrainStatus.Uptodate, GrainStatus statusBroker = GrainStatus.Uptodate)
            {
                if (ShowAll || GrainStatus.Uptodate < (statusSnapshot | statusBroker))
                {
                    var mod = GrainStatus.Uptodate < (statusSnapshot | statusBroker);
                    try
                    {
                        if (GrainStatus.Uptodate < statusSnapshot && GrainStatus.Uptodate < statusBroker)
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                        }
                        else
                        {
                            if (GrainStatus.Uptodate < statusSnapshot)
                            {
                                Console.ForegroundColor = statusSnapshot switch
                                {
                                    GrainStatus.Modified => ConsoleColor.Cyan,
                                    GrainStatus.New => ConsoleColor.Green,
                                    GrainStatus.Deleted => ConsoleColor.Red,
                                    GrainStatus.Ignored => ConsoleColor.Gray,
                                    GrainStatus.Missing or GrainStatus.Obscure => ConsoleColor.Magenta,
                                    _ => throw new NotImplementedException()
                                };
                            }
                            else if (GrainStatus.Uptodate < statusBroker)
                            {
                                Console.ForegroundColor = statusBroker switch
                                {
                                    GrainStatus.Modified => ConsoleColor.DarkCyan,
                                    GrainStatus.New => ConsoleColor.DarkGreen,
                                    GrainStatus.Deleted => ConsoleColor.DarkRed,
                                    GrainStatus.Ignored => ConsoleColor.DarkGray,
                                    GrainStatus.Missing or GrainStatus.Obscure => ConsoleColor.DarkMagenta,
                                    _ => throw new NotImplementedException()
                                };
                            }

                        }
                        
                        Console.Write($"[{GetStatusIndicator(statusSnapshot)}{GetStatusIndicator(statusBroker)}] {grain.Id} ({grain.Path ?? "\\"}){Environment.NewLine}");
                    }
                    finally
                    {
                        if (mod)
                        {
                            Console.ResetColor();
                        }
                    }
                }
            }

            private static string GetStatusIndicator(GrainStatus status)
            {
                var result = Enum.GetName(status)?[..1] ?? "#";
                if (GrainStatus.Uptodate == status)
                {
                    result = " ";
                }
                else if (GrainStatus.Missing == status)
                {
                    result = "!";
                }
                return result;
            }
        }
    }
}
