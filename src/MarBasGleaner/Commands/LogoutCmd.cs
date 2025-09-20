using CraftedSolutions.MarBasGleaner.Tracking;
using System.CommandLine.Invocation;

namespace CraftedSolutions.MarBasGleaner.Commands
{
    internal class LogoutCmd : GenericCmd
    {
        public LogoutCmd(): base("logout", LogoutCmdL10n.CmdDesc)
        {
            Setup();
        }

        public new sealed class Worker(ITrackingService trackingService, ILogger<Worker> logger) : GenericCmd.Worker(trackingService, (ILogger)logger)
        {
            public override async Task<int> InvokeAsync(InvocationContext context)
            {
                var ctoken = context.GetCancellationToken();
                var snapshotDir = await _trackingService.GetSnapshotDirectoryAsync(Directory, ctoken);

                var result = ValidateSnapshot(snapshotDir, true, false);
                if (0 != result)
                {
                    return result;
                }
                if (await _trackingService.LogoutFromBrokerAsync(snapshotDir.ConnectionSettings!, ctoken))
                {
                    DisplayInfo(string.Format(LogoutCmdL10n.MsgCmdSuccess, snapshotDir.FullPath, snapshotDir.ConnectionSettings!.BrokerUrl));
                }
                else
                {
                    result = (int)CmdResultCode.AuthProviderError;
                    DisplayWarning(string.Format(LogoutCmdL10n.WarnAuthProviderLogout, snapshotDir.FullPath));
                }
                await snapshotDir.StoreLocalState(false, ctoken);
                return result;
            }
        }
    }
}
