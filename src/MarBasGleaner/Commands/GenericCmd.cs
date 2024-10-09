using System.CommandLine;
using System.CommandLine.Invocation;
using MarBasGleaner.BrokerAPI;
using MarBasGleaner.Tracking;
using MarBasSchema.Sys;

namespace MarBasGleaner.Commands
{
    internal abstract class GenericCmd : Command
    {
        public static readonly string SeparatorLine = new('-', 50);
        public static readonly Option<string> DirectoryOpion = new(new[] { "--directory", "-d" }, () => SnapshotDirectory.DefaultPath, GenericCmdL10n.DirectoryOpionDesc);

        [Flags]
        public enum MessageSeparatorOption
        {
            None = 0x0, Before = 0x1, After = 0x2, Both = Before | After
        }

        protected GenericCmd(string name, string? description = null)
            : base(name, description)
        {
        }

        protected virtual void Setup()
        {
            Add(DirectoryOpion);
        }

        public abstract class Worker : ICommandHandler
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

            public string Directory { get; set; } = SnapshotDirectory.DefaultPath;

            public int Invoke(InvocationContext context)
            {
                return InvokeAsync(context).Result;
            }

            public abstract Task<int> InvokeAsync(InvocationContext context);

            protected async Task<ConnectionCheckResult> ValidateBrokerConnection(IBrokerClient client, Version? snapshotVersion = null, Guid? instanceId = null, CancellationToken cancellationToken = default)
            {
                var result = new ConnectionCheckResult
                {
                    Code = CmdResultCode.Success
                };

                try
                {
                    result.Info = await client.GetSystemInfo(cancellationToken);
                    if (null == result.Info)
                    {
                        ReportError(result.Code = CmdResultCode.BrokerConnectionError, string.Format(GenericCmdL10n.ErrorBrokerConnection, client.APIUrl));
                    }
                    else if (result.Info.Version < ConnectionSettings.MinimumAPIVersion)
                    {
                        ReportError(result.Code = CmdResultCode.APIVersionError, string.Format(GenericCmdL10n.ErrorAPIVersion, ConnectionSettings.MinimumAPIVersion, result.Info.Version));
                    }
                    else if (null != snapshotVersion && result.Info.SchemaVersion != snapshotVersion)
                    {
                        ReportError(result.Code = CmdResultCode.SchemaVersionError, string.Format(GenericCmdL10n.ErrorSchemaVersion, result.Info.SchemaVersion, snapshotVersion));
                    }
                    else if (null != instanceId && result.Info.InstanceId != instanceId)
                    {
                        ReportError(result.Code = CmdResultCode.InstanceIdError, string.Format(GenericCmdL10n.ErrorInstanceId, result.Info.InstanceId, instanceId));
                    }
                }
                catch (Exception e)
                {
                    if (_logger.IsEnabled(LogLevel.Error))
                    {
                        _logger.LogError(e, "Broker connection error");
                    }
                    ReportError(result.Code = CmdResultCode.BrokerConnectionError, string.Format(GenericCmdL10n.ErrorBrokerConnectionException, client.APIUrl, e.Message));
                }
                return result;
            }
        }

        internal static int ReportError(CmdResultCode error, string message)
        {
            try
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write(message);
                Console.Write(Environment.NewLine);
            }
            finally
            {
                Console.ResetColor();
            }
            return (int)error;
        }

        internal static void DisplayInfo(string message)
        {
            try
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write(message);
                Console.Write(Environment.NewLine);
            }
            finally
            {
                Console.ResetColor();
            }
        }

        internal static void DisplayWarning(string message)
        {
            try
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.Write(message);
                Console.Write(Environment.NewLine);
            }
            finally
            {
                Console.ResetColor();
            }
        }

        internal static void DisplayMessage(string message, MessageSeparatorOption separatorOption = MessageSeparatorOption.None)
        {
            Console.ResetColor();
            if (MessageSeparatorOption.Before == (MessageSeparatorOption.Before & separatorOption))
            {
                Console.Write(SeparatorLine);
                Console.Write(Environment.NewLine);
            }
            if (!string.IsNullOrEmpty(message))
            {
                Console.Write(message);
                Console.Write(Environment.NewLine);
            }
            if (MessageSeparatorOption.After == (MessageSeparatorOption.After & separatorOption))
            {
                Console.Write(SeparatorLine);
                Console.Write(Environment.NewLine);
            }
        }

        protected static int ValidateSnapshot(SnapshotDirectory snapshotDir, bool mustBeConnected = true, bool requiresCheckpoint = true)
        {
            if (!snapshotDir.IsSnapshot || !snapshotDir.IsReady)
            {
                return ReportError(CmdResultCode.SnapshotStateError, string.Format(GenericCmdL10n.ErrorReadyState, snapshotDir.FullPath));
            }
            if (null != snapshotDir.Snapshot && snapshotDir.Snapshot.Version != Snapshot.SupportedVersion)
            {
                return ReportError(CmdResultCode.SnapshotVersionError, string.Format(GenericCmdL10n.ErrorSnapshotVersion, snapshotDir.FullPath, snapshotDir.Snapshot.Version, Snapshot.SupportedVersion));
            }
            if (mustBeConnected && !snapshotDir.IsConnected)
            {
                return ReportError(CmdResultCode.SnapshotStateError, string.Format(GenericCmdL10n.ErrorConnectedState, snapshotDir.FullPath));
            }
            if (requiresCheckpoint && ((mustBeConnected && null == snapshotDir.LocalCheckpoint) || null == snapshotDir.SharedCheckpoint))
            {
                return ReportError(CmdResultCode.SnapshotStateError, string.Format(GenericCmdL10n.ErrorCheckpointMissing, SnapshotDirectory.LocalStateFileName));
            }
            return 0;
        }

        internal struct ConnectionCheckResult
        {
            public CmdResultCode Code;
            public IServerInfo? Info;
        }
    }
}
