using CraftedSolutions.MarBasGleaner.Tracking;
using CraftedSolutions.MarBasGleaner.UI;
using diVISION.CommandLineX;
using System.CommandLine;

namespace CraftedSolutions.MarBasGleaner.Commands
{
    internal sealed class ConnectCmd() : ConnectBaseCmd("connect", ConnectCmdL10n.CmdDesc)
    {
        protected override void Setup()
        {
            base.Setup();
            Add(new Option<int>("--adopt-checkpoint")
            {
                DefaultValueFactory = (_) => 0,
                Description = ConnectCmdL10n.AdoptCheckpointOptionDesc
            });
        }

        public new class Worker(ITrackingService trackingService, ILogger<Worker> logger) : ConnectBaseCmd.Worker(trackingService, (ILogger)logger)
        {
            public int AdoptCheckpoint { get; set; } = 0;


            public override async Task<int> InvokeAsync(CommandActionContext context, CancellationToken cancellationToken = default)
            {
                if (null == Url || !Url.IsAbsoluteUri)
                {
                    return ReportError(CmdResultCode.ParameterError, string.Format(ConnectCmdL10n.ErrorURL, Url));
                }

                var snapshotDir = await _trackingService.GetSnapshotDirectoryAsync(Directory, cancellationToken: cancellationToken);
                var result = ValidateSnapshot(snapshotDir, false);
                if (0 != result)
                {
                    return result;
                }
                if (snapshotDir.IsConnected)
                {
                    return ReportError(CmdResultCode.SnapshotStateError, string.Format(ConnectCmdL10n.ErrorConnectionState, snapshotDir.FullPath, snapshotDir.ConnectionSettings?.BrokerUrl));
                }

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("ConnectCmd: SnapshotDirectory={fullPath}, Url={url}", snapshotDir.FullPath, Url);
                }

                DisplayMessage(string.Format(ConnectCmdL10n.MsgCmdStart, Url, snapshotDir.FullPath), MessageSeparatorOption.After);

                Guid instanceId = Guid.Empty;
                var connection = CreateConnectionSettings();
                var brokerStat = await ValidateBrokerConnection(_trackingService, connection, snapshotDir.Snapshot?.SchemaVersion, snapshotDir.BrokerInstanceId, cancellationToken);
                if (CmdResultCode.Success != brokerStat.Code)
                {
                    return (int)brokerStat.Code;
                }
                if (null != brokerStat.Info)
                {
                    instanceId = brokerStat.Info.InstanceId;
                }
                using (var client = await _trackingService.GetBrokerClientAsync(connection, StoreCredentials, cancellationToken))
                {
                    var checks = await client.CheckGrainsExist(snapshotDir.Snapshot!.Anchor, cancellationToken);
                    var failedChecks = checks.Where(x => !x.Value && x.Key != snapshotDir.Snapshot.AnchorId).Select(x => x.Key.ToString("D"));
                    if (failedChecks.Any())
                    {
                        return ReportError(CmdResultCode.AnchorGrainError, string.Format(ConnectCmdL10n.ErrorAnchorPath, string.Join(", ", failedChecks)));
                    }
                }

                try
                {
                    await snapshotDir.Connect(connection, instanceId, AdoptCheckpoint, cancellationToken: cancellationToken);
                    DisplayInfo(string.Format(ConnectCmdL10n.MsgCmdSuccess, Url));
                }
                catch (Exception e)
                {
                    if (_logger.IsEnabled(LogLevel.Error))
                    {
                        _logger.LogError(e, "Snapshot connect error");
                    }
                    snapshotDir.Disconnect();
                    return ReportError(CmdResultCode.SnapshotInitError, string.Format(ConnectCmdL10n.ErrorConnectException, snapshotDir.FullPath, Url, e.Message));
                }

                return result;
            }
        }
    }
}
