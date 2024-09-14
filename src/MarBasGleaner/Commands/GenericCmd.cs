using System.CommandLine;
using MarBasGleaner.BrokerAPI;
using MarBasGleaner.Tracking;
using MarBasSchema.Sys;

namespace MarBasGleaner.Commands
{
    internal abstract class GenericCmd(string name, string? description = null) : Command(name, description)
    {
        public static readonly string SeparatorLine = new('-', 50);
        public static readonly Option<string> DirectoryOpion = new(new[] { "--directory", "-d" }, () => SnapshotDirectory.DefaultPath, "Local directory containing tracking information");

        protected static int ReportError(CmdResultCode error, string message)
        {
            try
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(message);
            }
            finally
            {
                Console.ResetColor();
            }
            return (int)error;
        }

        protected static void ReportInfo(string message)
        {
            try
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(message);
            }
            finally
            {
                Console.ResetColor();
            }
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
