using CraftedSolutions.MarBasGleaner.Tracking;
using CraftedSolutions.MarBasGleaner.UI;
using CraftedSolutions.MarBasSchema.Grain;
using CraftedSolutions.MarBasSchema.Transport;
using diVISION.CommandLineX;
using System.CommandLine;

namespace CraftedSolutions.MarBasGleaner.Commands
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
            Add(new Option<bool>("--show-all")
            {
                Description = StatusCmdL10n.ShowAllOptionDesc
            });
            Add(new Option<bool>("--assume-reset")
            {
                Description = StatusCmdL10n.AssumeResetOptionDesc
            });
        }

        public new sealed class Worker(ITrackingService trackingService, ILogger<Worker> logger) : GenericCmd.Worker(trackingService, (ILogger)logger)
        {

            public bool ShowAll { get; set; }
            public bool AssumeReset { get; set; }

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

                DisplayMessage(string.Format(StatusCmdL10n.MsgCmdStart, snapshotDir.FullPath, client.APIUrl), MessageSeparatorOption.After);

                bool isCheckpointInSync = snapshotDir.LocalCheckpoint!.IsSame(snapshotDir.SharedCheckpoint);
                if (!isCheckpointInSync)
                {
                    DisplayWarning(StatusCmdL10n.WarnSnapshotModified);
                }

                var rootId = (Guid)snapshotDir.Snapshot?.AnchorId!;

                var brokerMods = await client.ListGrains(rootId, SnapshotScope.Recursive == (snapshotDir.Snapshot.Scope & SnapshotScope.Recursive),
                    mtimeFrom: snapshotDir.LocalCheckpoint.Latest, includeParent: SnapshotScope.Anchor == (SnapshotScope.Anchor & snapshotDir.Snapshot.Scope), cancellationToken: cancellationToken);
                var brokerModHash = new Dictionary<Guid, IGrain>(brokerMods.Select(x => new KeyValuePair<Guid, IGrain>(x.Id, x)));

                var conflated = await snapshotDir.LoadConflatedCheckpoint(SnapshotCheckpoint.OldestOrdinal, cancellationToken);
                var additionsToCheck = new Dictionary<Guid, IGrain>();
                var deletionsToCheck = new Dictionary<Guid, IGrain>();

                await foreach (var grain in snapshotDir.ListGrains<GrainTransportable>(cancellationToken: cancellationToken))
                {
                    if (null != grain)
                    {
#pragma warning disable IDE0042 // Deconstruct variable declaration
                        var status = (snapshot: GrainTrackingStatus.Uptodate, broker: GrainTrackingStatus.Uptodate);
#pragma warning restore IDE0042 // Deconstruct variable declaration
                        if (brokerModHash.TryGetValue(grain.Id, out IGrain? value))
                        {
                            status.broker = GrainTrackingStatus.Modified;
                            if (value.MTime < grain.MTime)
                            {
                                status.snapshot = GrainTrackingStatus.Modified;
                            }
                            brokerModHash.Remove(grain.Id);
                        }

                        var pending = false;
                        if (!conflated.Modifications.Contains(grain.Id))
                        {
                            status.snapshot = snapshotDir.SharedCheckpoint!.Modifications.Contains(grain.Id) ? GrainTrackingStatus.New : GrainTrackingStatus.Obscure;
                        }
                        else if (conflated.Deletions.Contains(grain.Id))
                        {
                            status.snapshot = GrainTrackingStatus.Obscure;
                        }
                        else if (GrainTrackingStatus.Uptodate == status.snapshot && GrainTrackingStatus.Uptodate == status.broker
                            && grain.MTime > (AssumeReset ? SnapshotCheckpoint.BuiltInGrainsMTime : snapshotDir.LocalCheckpoint.Latest))
                        {
                            status.snapshot = GrainTrackingStatus.Modified;
                            if (AssumeReset || !isCheckpointInSync && !snapshotDir.LocalCheckpoint.Modifications.Contains(grain.Id))
                            {
                                additionsToCheck[grain.Id] = grain;
                                pending = true;
                            }
                        }

                        if (GrainTrackingStatus.Uptodate < (status.snapshot | status.broker))
                        {
                            if (0 == result)
                            {
                                result = (int)CmdResultCode.SnapshotStatusOutofdate;
                            }
                        }
                        else if (!pending)
                        {
                            deletionsToCheck[grain.Id] = grain;
                        }
                        if (!pending)
                        {
                            PrintGrainInfo(grain, status.snapshot, status.broker);
                        }

                        conflated.Modifications.Remove(grain.Id);
                        conflated.Deletions.Remove(grain.Id);
                    }
                }

                if (0 < additionsToCheck.Count)
                {
                    var checkResults = await client.CheckGrainsExist(additionsToCheck.Keys, cancellationToken);
                    foreach (var checkResult in checkResults)
                    {
                        PrintGrainInfo(additionsToCheck[checkResult.Key], checkResult.Value ? GrainTrackingStatus.Modified : GrainTrackingStatus.New);
                    }
                }

                if (0 < deletionsToCheck.Count)
                {
                    var checkResults = await client.CheckGrainsExist(deletionsToCheck.Keys, cancellationToken);
                    foreach (var checkResult in checkResults)
                    {
                        if (!checkResult.Value)
                        {
                            PrintGrainInfo(deletionsToCheck[checkResult.Key], statusBroker: GrainTrackingStatus.Deleted);
                        }
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
                    var grain = await client.GetGrain(id, false, cancellationToken);
                    if (null == grain)
                    {
                        grain = await snapshotDir.LoadGrainById<GrainPlain>(id, cancellationToken: cancellationToken);
                        PrintGrainInfo(grain ?? DeletedGrain(id), statusBroker: GrainTrackingStatus.Deleted);
                    }
                    else
                    {
                        PrintGrainInfo(grain, GrainTrackingStatus.Missing);
                    }
                }
                foreach (var id in conflated.Deletions)
                {
                    var grain = await client.GetGrain(id, false, cancellationToken);
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
