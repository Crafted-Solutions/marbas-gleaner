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
            : base("connect", "Connects a tracking snapshot with MarBas broker instance")
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
            AddArgument(new Argument<Uri>("url", "Broker URL"));
            base.Setup();
            AddOption(new Option<string>("--auth", () => BasicAuthenticator.SchemeName, "Authentication type to use with MarBas broker connection"));
            AddOption(new Option<int>("--adopt-checkpoint", () => 0, "Adopt specified checkpoint (-1 for latest) as current one for this connection"));
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
                    return ReportError(CmdResultCode.ParameterError, $"'{Url}' is not a recognizable absolute URI");
                }
                var ctoken = context.GetCancellationToken();

                var snapshotDir = await _trackingService.GetSnapshotDirectoryAsync(Directory, cancellationToken: ctoken);
                if (!snapshotDir.IsDirectory || !snapshotDir.IsSnapshot)
                {
                    return ReportError(CmdResultCode.SnapshotStateError, $"'{snapshotDir.FullPath}' contains no tracking snapshots");
                }
                if (snapshotDir.IsConnected)
                {
                    return ReportError(CmdResultCode.SnapshotStateError, $"'{snapshotDir.FullPath}' is already tracking '{snapshotDir.ConnectionSettings?.BrokerUrl}'");
                }

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("ConnectCmd: SnapshotDirectory={fullPath}, Url={url}", snapshotDir.FullPath, Url);
                }

                DisplayMessage($"Connecting {Url} with snapshot {snapshotDir.FullPath}", MessageSeparatorOption.After);

                Guid instanceId = Guid.Empty;
                var connection = CreateConnectionSettings();
                using (var client = _trackingService.GetBrokerClient(connection))
                {
                    var brokerStat = await CheckBrokerConnection(client, snapshotDir.Snapshot?.SchemaVersion, ctoken);
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
                    DisplayInfo($"Snapshot successfully connected to '{Url}'");
                }
                catch (Exception e)
                {
                    if (_logger.IsEnabled(LogLevel.Error))
                    {
                        _logger.LogError(e, "Snapshot connect error");
                    }
                    snapshotDir.Disconnect();
                    return ReportError(CmdResultCode.SnapshotInitError, $"Error connecting snapshot '{snapshotDir.FullPath}' with {Url}: {e.Message}");
                }

                return 0;
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
