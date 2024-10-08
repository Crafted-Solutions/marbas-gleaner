using System.CommandLine;
using System.CommandLine.Invocation;
using MarBasGleaner.Tracking;
using MarBasSchema.Grain;
using MarBasSchema.Transport;

namespace MarBasGleaner.Commands
{
    internal class PushCmd : GenericCmd
    {

        public PushCmd()
            : base("push", PushCmdL10n.CmdDesc)
        {
            Setup();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments", Justification = "The Setup() method is meant to be called once per lifetime")]
        protected override void Setup()
        {
            base.Setup();
            AddOption(new Option<int>(new[] { "-c", "--starting-checkpoint" }, () => -1, PushCmdL10n.StartingCheckpointOptionDesc));
            AddOption(new Option<DuplicatesHandlingStrategy>(new[] { "-s", "--strategy" }, () => DuplicatesHandlingStrategy.OverwriteSkipNewer, PushCmdL10n.StrategyOptionDesc));
        }

        public new class Worker(ITrackingService trackingService, ILogger<Worker> logger) : GenericCmd.Worker(trackingService, (ILogger)logger)
        {
            public int StartingCheckpoint { get; set; } = -1;
            public DuplicatesHandlingStrategy Strategy { get; set; } = DuplicatesHandlingStrategy.OverwriteSkipNewer;

            public async override Task<int> InvokeAsync(InvocationContext context)
            {
                var ctoken = context.GetCancellationToken();
                var snapshotDir = await _trackingService.GetSnapshotDirectoryAsync(Directory, ctoken);

                var result = ValidateSnapshot(snapshotDir);
                if (0 != result)
                {
                    return result;
                }

                using var client = _trackingService.GetBrokerClient(snapshotDir.ConnectionSettings!);

                var brokerStat = await ValidateBrokerConnection(client, snapshotDir.Snapshot!.SchemaVersion, ctoken);
                if (CmdResultCode.Success != brokerStat.Code)
                {
                    return (int)brokerStat.Code;
                }

                DisplayMessage(string.Format(PushCmdL10n.MsgCmdStart, snapshotDir.FullPath, client.APIUrl), MessageSeparatorOption.After);

                var isSafeCheckpoint = snapshotDir.LocalCheckpoint!.IsSame(snapshotDir.SharedCheckpoint);
                if (-1 < StartingCheckpoint)
                {
                    snapshotDir.LastPushCheckpoint = StartingCheckpoint;
                }
                if (isSafeCheckpoint && snapshotDir.LastPushCheckpoint == snapshotDir.SharedCheckpoint!.Ordinal)
                {
                    DisplayInfo(string.Format(PushCmdL10n.MsgCmdSuccessNoop, snapshotDir.LastPushCheckpoint));
                    return result;
                }

                var conflated = await snapshotDir.LoadConflatedCheckpoint(Math.Min(snapshotDir.LastPushCheckpoint + 1, snapshotDir.SharedCheckpoint!.Ordinal), ctoken);

                var grainsToStore = new HashSet<IGrainTransportable>();
                var grainsToDelete = conflated.Deletions;

                foreach (var id in conflated.Modifications)
                {
                    var grain = await snapshotDir.LoadGrainById<GrainTransportable>(id, cancellationToken: ctoken);
                    if (null == grain)
                    {
                        DisplayWarning(string.Format(PushCmdL10n.WarnGrainNotFound, id, snapshotDir.SharedCheckpoint.Ordinal));
                        snapshotDir.SharedCheckpoint.Modifications.Remove(id);
                        snapshotDir.LocalCheckpoint.Modifications.Remove(id);
                    }
                    else
                    {
                        grainsToStore.Add(grain);
                    }
                }
                if (0 < grainsToStore.Count)
                {
                    DisplayMessage(PushCmdL10n.StatusQueueStore);
                    foreach (var g in grainsToStore)
                    {
                        DisplayMessage($"{g.Id:D} ({g.Path ?? "\\"}");
                    }
                }
                if (0 < grainsToDelete.Count)
                {
                    DisplayMessage(PushCmdL10n.StatusQueueDelete);
                    foreach (var id in grainsToDelete)
                    {
                        DisplayMessage($"{id:D}");
                    }
                }

                try
                {
                    var importResult = await client.PushGrains(grainsToStore, grainsToDelete, Strategy, ctoken);
                    if (null == importResult)
                    {
                        return ReportError(CmdResultCode.BrokerPushError, string.Format(PushCmdL10n.ErrorBrokerRequest, client.APIUrl));
                    }

                    if (null != importResult.Feedback && importResult.Feedback.Any(x => LogLevel.Warning <= x.FeedbackType))
                    {
                        DisplayMessage(string.Empty, MessageSeparatorOption.Before);
                        DisplayWarning(string.Format(PushCmdL10n.WarnCmdResult, importResult.ImportedCount, importResult.DeletedCount, client.APIUrl));
                    }
                    else
                    {
                        DisplayMessage(string.Format(PushCmdL10n.MsgCmdSuccess, importResult.ImportedCount, importResult.DeletedCount, client.APIUrl), MessageSeparatorOption.Before);
                    }
                    if (null != importResult.Feedback)
                    {
                        foreach (var feedback in importResult.Feedback)
                        {
                            if (_logger.IsEnabled(feedback.FeedbackType))
                            {
                                _logger.Log(feedback.FeedbackType, "Import feedback on {object}: {message} ({code})", feedback.ObjectId?.ToString("D") ?? "unspecified", feedback.Message, feedback.Code);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    if (_logger.IsEnabled(LogLevel.Error))
                    {
                        _logger.LogError(e, "Error pushing grains");
                    }
                    return ReportError(CmdResultCode.BrokerPushError, string.Format(PushCmdL10n.ErrorBrokerRequestException, grainsToStore.Count, grainsToDelete.Count, client.APIUrl, e.Message));
                }

                snapshotDir.LastPushCheckpoint = conflated.Ordinal;
                if (!isSafeCheckpoint && 0 == snapshotDir.LocalCheckpoint.Ordinal)
                {
                    await snapshotDir.AdoptCheckpoint(cancellationToken: ctoken);
                }
                await snapshotDir.StoreMetadata(false, ctoken);

                return result;
            }
        }
    }
}
