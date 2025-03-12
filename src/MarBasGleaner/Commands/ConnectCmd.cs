using System.CommandLine;
using System.CommandLine.Invocation;
using CraftedSolutions.MarBasGleaner.Tracking;

namespace CraftedSolutions.MarBasGleaner.Commands
{
    internal sealed class ConnectCmd() : ConnectBaseCmd("connect", ConnectCmdL10n.CmdDesc)
    {
        protected override void Setup()
        {
            base.Setup();
            AddOption(new Option<int>("--adopt-checkpoint", () => 0, ConnectCmdL10n.AdoptCheckpointOptionDesc));
        }

        public new class Worker(ITrackingService trackingService, ILogger<Worker> logger) : ConnectBaseCmd.Worker(trackingService, (ILogger)logger)
        {
            public int AdoptCheckpoint { get; set; } = 0;


            public override async Task<int> InvokeAsync(InvocationContext context)
            {
                if (null == Url || !Url.IsAbsoluteUri)
                {
                    return ReportError(CmdResultCode.ParameterError, string.Format(ConnectCmdL10n.ErrorURL, Url));
                }
                var ctoken = context.GetCancellationToken();

                var snapshotDir = await _trackingService.GetSnapshotDirectoryAsync(Directory, cancellationToken: ctoken);
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
                using (var client = _trackingService.GetBrokerClient(connection))
                {
                    var brokerStat = await ValidateBrokerConnection(client, snapshotDir.Snapshot?.SchemaVersion, snapshotDir.BrokerInstanceId, ctoken);
                    if (CmdResultCode.Success != brokerStat.Code)
                    {
                        return (int)brokerStat.Code;
                    }
                    if (null != brokerStat.Info)
                    {
                        instanceId = brokerStat.Info.InstanceId;
                    }
                }

                try
                {
                    await snapshotDir.Connect(connection, instanceId, AdoptCheckpoint, cancellationToken: ctoken);
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
