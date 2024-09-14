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
            Uptodate, Missing, Obscure, Modified, New, Deleted
        }

        public StatusCmd()
            : base("status", "Shows status of MarBas grains in a tracking snapshot")
        {
            AddOption(DirectoryOpion);
            AddOption(new Option<bool>("--show-all", "List all grains, even unmodified ones"));
        }

        public class Worker(ITrackingService trackingService, ILogger<Worker> logger) : ICommandHandler
        {
            private readonly ITrackingService _trackingService = trackingService;
            private readonly ILogger<Worker> _logger = logger;

            public string Directory { get; set; } = SnapshotDirectory.DefaultPath;
            public bool ShowAll { get; set; }

            public int Invoke(InvocationContext context)
            {
                return InvokeAsync(context).Result;
            }

            public async Task<int> InvokeAsync(InvocationContext context)
            {
                var ctoken = context.GetCancellationToken();
                var snapshotDir = await _trackingService.GetSnapshotDirectoryAsync(Directory, ctoken);
                if (!snapshotDir.IsDirectory || !snapshotDir.IsSnapshot)
                {
                    return ReportError(CmdResultCode.SnapshotStateError, $"'{snapshotDir.FullPath}' contains no tracking snapshots");
                }
                if (!snapshotDir.IsConnected)
                {
                    return ReportError(CmdResultCode.SnapshotStateError, $"'{snapshotDir.FullPath}' is not connected to broker, execute 'connect' first");
                }

                using var client = _trackingService.GetBrokerClient(snapshotDir.ConnectionSettings!);

                var brokerStat = await CheckBrokerConnection(client, snapshotDir.SharedSnapshot?.SchemaVersion, ctoken);
                if (CmdResultCode.Success != brokerStat.Code)
                {
                    return (int)brokerStat.Code;
                }

                Console.WriteLine("Comparing snapshot {0} with {1}", snapshotDir.FullPath, client.APIUrl);
                Console.WriteLine(SeparatorLine);

                var rootId = (Guid)(snapshotDir.LocalSnapshot?.AnchorId!);
                
                var brokerMods = await client.ListGrains(rootId, SnapshotScope.Recursive == (snapshotDir.LocalSnapshot.Scope & SnapshotScope.Recursive), mtimeFrom: snapshotDir.LocalSnapshot.Latest, cancellationToken: ctoken);
                var brokerModHash = new Dictionary<Guid, IGrain>(brokerMods.Select(x => new KeyValuePair<Guid, IGrain>(x.Id, x)));
                if (SnapshotScope.Anchor == (SnapshotScope.Anchor & snapshotDir.LocalSnapshot.Scope))
                {
                    var anchor = await client.GetGrain(rootId, ctoken);
                    if (null != anchor && snapshotDir.LocalSnapshot.Latest < anchor.MTime)
                    {
                        brokerModHash.Add(anchor.Id, anchor);
                    }
                }

                var deletions = snapshotDir.LocalSnapshot.DeadGrains;
                var remainingAdditions = snapshotDir.LocalSnapshot.AliveGrains;
                // TODO do we need a copy of LocalSnapshot.AliveGrains?
                //remainingAdditions = new HashSet<Guid>(snapshotDir.LocalSnapshot.AliveGrains);
                if (true == snapshotDir.SharedSnapshot?.DeadGrains.Any())
                {
                    remainingAdditions.ExceptWith(snapshotDir.SharedSnapshot.DeadGrains);
                    deletions.UnionWith(snapshotDir.SharedSnapshot.DeadGrains);
                }


                var result = 0;
                await foreach(var grain in snapshotDir.ListGrains<GrainTransportable>(ctoken))
                {
                    if (null != grain)
                    {
                        var status = new[] { GrainStatus.Uptodate, GrainStatus.Uptodate };
                        if (brokerModHash.ContainsKey(grain.Id))
                        {
                            status[1] = GrainStatus.Modified;
                            brokerModHash.Remove(grain.Id);
                        }

                        if (!remainingAdditions.Contains(grain.Id))
                        {
                            status[0] = snapshotDir.SharedSnapshot!.AliveGrains.Contains(grain.Id) ? GrainStatus.New : GrainStatus.Obscure;
                        }
                        else if (deletions.Contains(grain.Id))
                        {
                            status[0] = GrainStatus.Deleted;
                        }
                        else if (grain.MTime > snapshotDir.LocalSnapshot.Latest)
                        {
                            status[0] = GrainStatus.Modified;
                        }

                        if (0 == result && GrainStatus.Uptodate < (status[0] | status[1]))
                        {
                            result = 42;
                        }
                        PrintGrainInfo(grain, status[0], status[1]);

                        remainingAdditions.Remove(grain.Id);
                        deletions.Remove(grain.Id);
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

                foreach (var id in remainingAdditions)
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
                foreach (var id in deletions)
                {
                    var grain = await client.GetGrain(id, ctoken);
                    if (null == grain)
                    {
                        PrintGrainInfo(DeletedGrain(id), GrainStatus.Deleted, GrainStatus.Deleted);
                    }
                    else
                    {
                        PrintGrainInfo(grain, GrainStatus.Deleted);
                    }
                }

                foreach (var entry in brokerModHash)
                {
                    PrintGrainInfo(entry.Value, statusBroker: GrainStatus.New);
                }
                if (0 == result)
                {
                    ReportInfo($"Snapshot {snapshotDir.LocalSnapshot.InstanceId:D} is uptodate");
                }
                else
                {
                    Console.WriteLine(SeparatorLine);
                    Console.WriteLine("Status legend (left side - snapshot, right side - broker):");
                    var legend = string.Empty;
                    for (var s = GrainStatus.Missing; s <= GrainStatus.Deleted; s++)
                    {
                        if (0 < legend.Length)
                        {
                            legend += ", ";
                        }
                        legend += $"[{GetStatusIndicator(s)}] - {Enum.GetName(s)}";
                    }
                    Console.WriteLine(legend);
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
                                    GrainStatus.Missing or GrainStatus.Obscure => ConsoleColor.DarkMagenta,
                                    _ => throw new NotImplementedException()
                                };
                            }

                        }
                        
                        Console.WriteLine($"[{GetStatusIndicator(statusSnapshot)}{GetStatusIndicator(statusBroker)}] {grain.Id} ({grain.Path ?? "\\"})");
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
