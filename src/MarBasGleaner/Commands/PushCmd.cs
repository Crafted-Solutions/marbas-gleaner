using CraftedSolutions.MarBasGleaner.Tracking;
using CraftedSolutions.MarBasGleaner.UI;
using CraftedSolutions.MarBasSchema.Transport;
using diVISION.CommandLineX;
using System.CommandLine;

namespace CraftedSolutions.MarBasGleaner.Commands
{
    internal class PushCmd : GenericCmd
    {
        public static readonly Option<int> CheckpointOption = new("--starting-checkpoint", "-c")
        {
            DefaultValueFactory = (_) => SnapshotCheckpoint.NewestOrdinal,
            Description = string.Format(PushCmdL10n.StartingCheckpointOptionDesc, SnapshotCheckpoint.NewestOrdinal)
        };
        public static readonly Option<DuplicatesHandlingStrategy> StrategyOption = new("--strategy", "-s")
        {
            DefaultValueFactory = (_) => DuplicatesHandlingStrategy.OverwriteSkipNewer,
            Description = PushCmdL10n.StrategyOptionDesc
        };

        public PushCmd()
            : base("push", PushCmdL10n.CmdDesc)
        {
            Setup();
        }

        protected override void Setup()
        {
            base.Setup();
            Add(CheckpointOption);
            Add(StrategyOption);
        }

        public new sealed class Worker : GenericCmd.Worker
        {
            public int StartingCheckpoint { get; set; } = SnapshotCheckpoint.NewestOrdinal;
            public DuplicatesHandlingStrategy Strategy { get; set; } = DuplicatesHandlingStrategy.OverwriteSkipNewer;

            public Worker(ITrackingService trackingService, ILogger<Worker> logger)
                : this(trackingService, (ILogger)logger)
            {
            }

            internal Worker(ITrackingService trackingService, ILogger logger)
                : base(trackingService, logger)
            {
            }

            public async override Task<int> InvokeAsync(CommandActionContext context, CancellationToken cancellationToken = default)
            {
                var snapshotDir = await _trackingService.GetSnapshotDirectoryAsync(Directory, cancellationToken);

                var result = ValidateSnapshot(snapshotDir);
                if (0 != result)
                {
                    return result;
                }

                var brokerStat = await ValidateBrokerConnection(_trackingService, snapshotDir.ConnectionSettings!, snapshotDir.Snapshot!.SchemaVersion, snapshotDir.BrokerInstanceId, cancellationToken);
                if (CmdResultCode.Success != brokerStat.Code)
                {
                    return (int)brokerStat.Code;
                }

                using var client = await _trackingService.GetBrokerClientAsync(snapshotDir.ConnectionSettings!, cancellationToken);
                await snapshotDir.StoreLocalState(false, cancellationToken);

                DisplayMessage(string.Format(PushCmdL10n.MsgCmdStart, snapshotDir.FullPath, client.APIUrl), MessageSeparatorOption.After);

                var isSafeCheckpoint = snapshotDir.LocalCheckpoint!.IsSame(snapshotDir.SharedCheckpoint);
                if (SnapshotCheckpoint.NewestOrdinal < StartingCheckpoint)
                {
                    snapshotDir.LastPushCheckpoint = StartingCheckpoint;
                }
                else if (snapshotDir.LastPushHasErrors)
                {
                    --snapshotDir.LastPushCheckpoint;
                }
                if (isSafeCheckpoint && snapshotDir.LastPushCheckpoint == snapshotDir.SharedCheckpoint!.Ordinal)
                {
                    DisplayInfo(string.Format(PushCmdL10n.MsgCmdSuccessNoop, snapshotDir.LastPushCheckpoint));
                    return result;
                }

                var conflated = await snapshotDir.LoadConflatedCheckpoint(Math.Min(snapshotDir.LastPushCheckpoint + 1, snapshotDir.SharedCheckpoint!.Ordinal), cancellationToken);

                var grainsToStore = new HashSet<IGrainTransportable>();
                var grainsToDelete = conflated.Deletions;

                foreach (var id in conflated.Modifications)
                {
                    var grain = await snapshotDir.LoadGrainById<GrainTransportable>(id, cancellationToken: cancellationToken);
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
                        DisplayMessage($"{g.Id:D} ({g.Path ?? "\\"})");
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

                var hasErrors = false;
                try
                {
                    var importResult = await client.PushGrains(grainsToStore, grainsToDelete, Strategy, cancellationToken);
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
                            if (LogLevel.Warning < feedback.FeedbackType)
                            {
                                hasErrors = true;
                            }
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
                snapshotDir.LastPushHasErrors = hasErrors;
                if (!isSafeCheckpoint && 0 == snapshotDir.LocalCheckpoint.Ordinal)
                {
                    await snapshotDir.AdoptCheckpoint(cancellationToken: cancellationToken);
                }
                await snapshotDir.StoreMetadata(false, cancellationToken);

                return result;
            }
        }
    }
}
