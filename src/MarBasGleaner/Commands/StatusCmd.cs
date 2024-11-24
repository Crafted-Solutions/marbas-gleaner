using System.CommandLine;
using System.CommandLine.Invocation;
using MarBasGleaner.Tracking;
using MarBasSchema.Grain;
using MarBasSchema.Transport;

namespace MarBasGleaner.Commands
{

    internal sealed class StatusCmd : GenericCmd
    {
        public StatusCmd()
            : base("status", StatusCmdL10n.CmdDesc)
        {
            Setup();
        }

        protected override void Setup()
        {
            base.Setup();
            AddOption(new Option<bool>("--show-all", StatusCmdL10n.ShowAllOptionDesc));
            AddOption(new Option<bool>("--assume-reset", StatusCmdL10n.AssumeResetOptionDesc));
        }

        public new sealed class Worker(ITrackingService trackingService, ILogger<Worker> logger) : GenericCmd.Worker(trackingService, (ILogger)logger)
        {

            public bool ShowAll { get; set; }
            public bool AssumeReset { get; set; }

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

                DisplayMessage(string.Format(StatusCmdL10n.MsgCmdStart, snapshotDir.FullPath, client.APIUrl), MessageSeparatorOption.After);

                bool isCheckpointInSync = snapshotDir.LocalCheckpoint!.IsSame(snapshotDir.SharedCheckpoint);
                if (!isCheckpointInSync)
                {
                    DisplayWarning(StatusCmdL10n.WarnSnapshotModified);
                }

                var rootId = (Guid)(snapshotDir.Snapshot?.AnchorId!);

                var brokerMods = await client.ListGrains(rootId, SnapshotScope.Recursive == (snapshotDir.Snapshot.Scope & SnapshotScope.Recursive),
                    mtimeFrom: snapshotDir.LocalCheckpoint.Latest, includeParent: SnapshotScope.Anchor == (SnapshotScope.Anchor & snapshotDir.Snapshot.Scope), cancellationToken: ctoken);
                var brokerModHash = new Dictionary<Guid, IGrain>(brokerMods.Select(x => new KeyValuePair<Guid, IGrain>(x.Id, x)));

                var conflated = await snapshotDir.LoadConflatedCheckpoint(cancellationToken: ctoken);
                var additionsToCheck = new Dictionary<Guid, IGrain>();

                await foreach (var grain in snapshotDir.ListGrains<GrainTransportable>(cancellationToken: ctoken))
                {
                    if (null != grain)
                    {
                        var status = (GrainTrackingStatus.Uptodate, GrainTrackingStatus.Uptodate);
                        if (brokerModHash.TryGetValue(grain.Id, out IGrain? value))
                        {
                            status.Item2 = GrainTrackingStatus.Modified;
                            if (value.MTime < grain.MTime)
                            {
                                status.Item1 = GrainTrackingStatus.Modified;
                            }
                            brokerModHash.Remove(grain.Id);
                        }

                        var pending = false;
                        if (!conflated.Modifications.Contains(grain.Id))
                        {
                            status.Item1 = snapshotDir.SharedCheckpoint!.Modifications.Contains(grain.Id) ? GrainTrackingStatus.New : GrainTrackingStatus.Obscure;
                        }
                        else if (conflated.Deletions.Contains(grain.Id))
                        {
                            status.Item1 = GrainTrackingStatus.Obscure;
                        }
                        else if (GrainTrackingStatus.Uptodate == status.Item1 && GrainTrackingStatus.Uptodate == status.Item2
                            && grain.MTime > (AssumeReset ? SnapshotCheckpoint.BuiltInGrainsMTime : snapshotDir.LocalCheckpoint.Latest))
                        {
                            status.Item1 = GrainTrackingStatus.Modified;
                            if (AssumeReset || (!isCheckpointInSync && !snapshotDir.LocalCheckpoint.Modifications.Contains(grain.Id)))
                            {
                                additionsToCheck[grain.Id] = grain;
                                pending = true;
                            }
                        }

                        if (0 == result && GrainTrackingStatus.Uptodate < (status.Item1 | status.Item2))
                        {
                            result = (int)CmdResultCode.SnapshotStatusOutofdate;
                        }
                        if (!pending)
                        {
                            PrintGrainInfo(grain, status.Item1, status.Item2);
                        }

                        conflated.Modifications.Remove(grain.Id);
                        conflated.Deletions.Remove(grain.Id);
                    }
                }

                if (0 < additionsToCheck.Count)
                {
                    var checkResults = await client.CheckGrainsExist(additionsToCheck.Keys, ctoken);
                    foreach (var checkResult in checkResults)
                    {
                        PrintGrainInfo(additionsToCheck[checkResult.Key], checkResult.Value ? GrainTrackingStatus.Modified : GrainTrackingStatus.New);
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

                foreach (var id in conflated.Modifications)
                {
                    var grain = await client.GetGrain(id, false, ctoken);
                    if (null == grain)
                    {
                        PrintGrainInfo(DeletedGrain(id), statusBroker: GrainTrackingStatus.Deleted);
                    }
                    else
                    {
                        PrintGrainInfo(grain, GrainTrackingStatus.Missing);
                    }
                }
                foreach (var id in conflated.Deletions)
                {
                    var grain = await client.GetGrain(id, false, ctoken);
                    if (null != grain)
                    {
                        PrintGrainInfo(grain, GrainTrackingStatus.Deleted);
                    }
                }

                foreach (var entry in brokerModHash)
                {
                    PrintGrainInfo(entry.Value, statusBroker: snapshotDir.IsIgnoredGrain(entry.Value) ? GrainTrackingStatus.Ignored : GrainTrackingStatus.New);
                }
                if (0 == result)
                {
                    DisplayInfo(string.Format(StatusCmdL10n.MsgCmdSuccessNoop, snapshotDir.FullPath));
                }
                else
                {
                    DisplayMessage(StatusCmdL10n.MsgCmdSuccessLegend, MessageSeparatorOption.Before);
                    var legend = string.Empty;
                    for (var s = GrainTrackingStatus.Missing; s <= GrainTrackingStatus.Deleted; s++)
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

            private void PrintGrainInfo(IGrain grain, GrainTrackingStatus statusSnapshot = GrainTrackingStatus.Uptodate, GrainTrackingStatus statusBroker = GrainTrackingStatus.Uptodate)
            {
                if (ShowAll || GrainTrackingStatus.Uptodate < (statusSnapshot | statusBroker))
                {
                    var mod = GrainTrackingStatus.Uptodate < (statusSnapshot | statusBroker);
                    try
                    {
                        if (GrainTrackingStatus.Uptodate < statusSnapshot && GrainTrackingStatus.Uptodate < statusBroker)
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                        }
                        else
                        {
                            if (GrainTrackingStatus.Uptodate < statusSnapshot)
                            {
                                Console.ForegroundColor = statusSnapshot switch
                                {
                                    GrainTrackingStatus.Modified => ConsoleColor.Cyan,
                                    GrainTrackingStatus.New => ConsoleColor.Green,
                                    GrainTrackingStatus.Deleted => ConsoleColor.Red,
                                    GrainTrackingStatus.Ignored => ConsoleColor.Gray,
                                    GrainTrackingStatus.Missing or GrainTrackingStatus.Obscure => ConsoleColor.Magenta,
                                    _ => throw new NotImplementedException()
                                };
                            }
                            else if (GrainTrackingStatus.Uptodate < statusBroker)
                            {
                                Console.ForegroundColor = statusBroker switch
                                {
                                    GrainTrackingStatus.Modified => ConsoleColor.DarkCyan,
                                    GrainTrackingStatus.New => ConsoleColor.DarkGreen,
                                    GrainTrackingStatus.Deleted => ConsoleColor.DarkRed,
                                    GrainTrackingStatus.Ignored => ConsoleColor.DarkGray,
                                    GrainTrackingStatus.Missing or GrainTrackingStatus.Obscure => ConsoleColor.DarkMagenta,
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

            private static string GetStatusIndicator(GrainTrackingStatus status)
            {
                var result = Enum.GetName(status)?[..1] ?? "#";
                if (GrainTrackingStatus.Uptodate == status)
                {
                    result = " ";
                }
                else if (GrainTrackingStatus.Missing == status)
                {
                    result = "!";
                }
                return result;
            }
        }
    }
}
