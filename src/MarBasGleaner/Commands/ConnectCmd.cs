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

        protected virtual void Setup()
        {
            AddArgument(new Argument<Uri>("url", "Broker URL"));
            AddOption(DirectoryOpion);
            AddOption(new Option<string>("--auth", () => BasicAuthenticator.SchemeName, "Authentication type to use with MarBas broker connection"));
        }

        public class Worker : ICommandHandler
        {
            protected readonly ILogger _logger;
            protected readonly ITrackingService _trackingService;

            public Worker(ITrackingService trackingService, ILogger<Worker> logger)
            {
                _logger = logger;
                _trackingService = trackingService;
            }

            protected Worker(ITrackingService trackingService, ILogger logger)
            {
                _logger = logger;
                _trackingService = trackingService;
            }

            public Uri? Url { get; set; }
            public string Auth { get; set; } = BasicAuthenticator.SchemeName;
            public string Directory { get; set; } = SnapshotDirectory.DefaultPath;


            public int Invoke(InvocationContext context)
            {
                return InvokeAsync(context).Result;
            }

            public virtual async Task<int> InvokeAsync(InvocationContext context)
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
                    return ReportError(CmdResultCode.SnapshotStateError, $"'{snapshotDir.IsConnected}' is already tracking '{snapshotDir.ConnectionSettings?.BrokerUrl}'");
                }

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("ConnectCmd: SnapshotDirectory={fullPath}, Url={url}", snapshotDir.FullPath, Url);
                }

                Console.WriteLine("Connecting {0} with snapshot {1}", Url, snapshotDir.FullPath);
                Console.WriteLine(SeparatorLine);

                Guid instanceId = Guid.Empty;
                var connection = CreateConnectionSettings();
                using (var client = _trackingService.GetBrokerClient(connection))
                {
                    var brokerStat = await CheckBrokerConnection(client, snapshotDir.SharedSnapshot?.SchemaVersion, ctoken);
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
                    await snapshotDir.Connect(connection, instanceId, cancellationToken: ctoken);
                }
                catch (Exception e)
                {
                    if (_logger.IsEnabled(LogLevel.Error))
                    {
                        _logger.LogError(e, "Snapshot connect error");
                    }
                    snapshotDir.CleanUp();
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
