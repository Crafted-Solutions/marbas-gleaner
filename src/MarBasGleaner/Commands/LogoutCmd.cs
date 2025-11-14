using CraftedSolutions.MarBasGleaner.Tracking;
using diVISION.CommandLineX;

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
            public override async Task<int> InvokeAsync(CommandActionContext context, CancellationToken cancellationToken = default)
            {
                var snapshotDir = await _trackingService.GetSnapshotDirectoryAsync(Directory, cancellationToken);

                var result = ValidateSnapshot(snapshotDir, true, false);
                if (0 != result)
                {
                    return result;
                }
                if (await _trackingService.LogoutFromBrokerAsync(snapshotDir.ConnectionSettings!, cancellationToken))
                {
                    DisplayInfo(string.Format(LogoutCmdL10n.MsgCmdSuccess, snapshotDir.FullPath, snapshotDir.ConnectionSettings!.BrokerUrl));
                }
                else
                {
                    result = (int)CmdResultCode.AuthProviderError;
                    DisplayWarning(string.Format(LogoutCmdL10n.WarnAuthProviderLogout, snapshotDir.FullPath));
                }
                await snapshotDir.StoreLocalState(false, cancellationToken);
                return result;
            }
        }
    }
}
