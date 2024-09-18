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
        public static readonly Option<string> DirectoryOpion = new(new[] { "--directory", "-d" }, () => SnapshotDirectory.DefaultPath, "Local directory containing tracking information");

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
            Console.Write(message);
            Console.Write(Environment.NewLine);
            if (MessageSeparatorOption.After == (MessageSeparatorOption.After & separatorOption))
            {
                Console.Write(SeparatorLine);
                Console.Write(Environment.NewLine);
            }
        }

        protected static int CheckSnapshot(SnapshotDirectory snapshotDir, bool mustBeConnected = true)
        {
            if (!snapshotDir.IsDirectory || !snapshotDir.IsSnapshot)
            {
                return ReportError(CmdResultCode.SnapshotStateError, $"'{snapshotDir.FullPath}' contains no tracking snapshots");
            }
            if (mustBeConnected && !snapshotDir.IsConnected)
            {
                return ReportError(CmdResultCode.SnapshotStateError, $"'{snapshotDir.FullPath}' is not connected to broker, execute 'connect' first");
            }
            return 0;
        }

        protected static async Task<ConnectionCheckResult> CheckBrokerConnection(IBrokerClient client, Version? snapshotVersion = null, CancellationToken cancellationToken = default)
        {
            var result = new ConnectionCheckResult
            {
                Code = CmdResultCode.Success,
                Info = await client.GetSystemInfo(cancellationToken)
            };
            if (null == result.Info)
            {
                result.Code = CmdResultCode.BrokerConnectionError;
                ReportError(result.Code, $"Failed querying broker API at '{client.APIUrl}'");
            }
            else if (null != snapshotVersion && result.Info.SchemaVersion != snapshotVersion)
            {
                result.Code = CmdResultCode.SchemaVersionError;
                ReportError(result.Code, $"Broker schema version {result.Info.SchemaVersion} is incompatible with snapshot {snapshotVersion}");
            }
            return result;
        }

        protected struct ConnectionCheckResult
        {
            public CmdResultCode Code;
            public IServerInfo? Info;
        }
    }
}
