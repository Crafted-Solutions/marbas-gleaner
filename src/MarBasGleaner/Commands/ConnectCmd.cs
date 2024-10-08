using System.CommandLine;
using System.CommandLine.Invocation;
using MarBasGleaner.BrokerAPI;
using MarBasGleaner.BrokerAPI.Auth;
using MarBasGleaner.Tracking;

namespace MarBasGleaner.Commands
{
    internal class ConnectCmd: GenericCmd
    {
        public ConnectCmd()
            : base("connect", ConnectCmdL10n.CmdDesc)
        {
            Setup();
        }

        protected ConnectCmd(string name, string? description = null)
            :base(name, description)
        {
            Setup();
        }

        protected override void Setup()
        {
            AddArgument(new Argument<Uri>("url", ConnectCmdL10n.URLArgDesc));
            base.Setup();
            AddOption(new Option<string>("--auth", () => BasicAuthenticator.SchemeName, ConnectCmdL10n.AuthOptionDesc));
            AddOption(new Option<int>("--adopt-checkpoint", () => 0, ConnectCmdL10n.AdoptCheckpointOptionDesc));
        }

        public new class Worker : GenericCmd.Worker
        {
            public Worker(ITrackingService trackingService, ILogger<Worker> logger)
                : base(trackingService, (ILogger)logger)
            {

            }

            protected Worker(ITrackingService trackingService, ILogger logger)
                : base(trackingService, logger)
            {
            }

            public Uri? Url { get; set; }
            public string Auth { get; set; } = BasicAuthenticator.SchemeName;
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
                    return ReportError(CmdResultCode.SnapshotStateError, String.Format(ConnectCmdL10n.ErrorConnectionState, snapshotDir.FullPath, snapshotDir.ConnectionSettings?.BrokerUrl));
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
                    var brokerStat = await ValidateBrokerConnection(client, snapshotDir.Snapshot?.SchemaVersion, ctoken);
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

            protected ConnectionSettings CreateConnectionSettings()
            {
                return new ConnectionSettings
                {
                    BrokerUrl = Url!,
                    AuthenticatorType = AuthenticatorFactory.ResolveAuthenticatorType(Auth) ?? typeof(BasicAuthenticator)
                };
            }
        }
    }
}
