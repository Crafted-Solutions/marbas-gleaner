using CraftedSolutions.MarBasGleaner.BrokerAPI.Auth;
using CraftedSolutions.MarBasGleaner.Tracking;
using CraftedSolutions.MarBasGleaner.UI;
using CraftedSolutions.MarBasSchema.Grain;
using diVISION.CommandLineX;
using System.CommandLine;

namespace CraftedSolutions.MarBasGleaner.Commands
{
    internal class InfoCmd : GenericCmd
    {
        public InfoCmd()
            : base("info", InfoCmdL10n.CmdDesc)
        {
            Setup();
        }

        protected override void Setup()
        {
            base.Setup();
            Add(new Option<bool>("--validate-connection", "-c")
            {
                Description = InfoCmdL10n.ValidateConnectionDesc
            });
        }

        public new sealed class Worker(ITrackingService trackingService, ILogger<Worker> logger) : GenericCmd.Worker(trackingService, (ILogger)logger)
        {
            public bool ValidateConnection { get; set; } = false;

            public override async Task<int> InvokeAsync(CommandActionContext context, CancellationToken cancellationToken = default)
            {
                var snapshotDir = await _trackingService.GetSnapshotDirectoryAsync(Directory, cancellationToken);

                var result = ValidateSnapshot(snapshotDir, false, false);
                if (0 != result)
                {
                    return result;
                }

                DisplayMessage(InfoCmdL10n.MsgHeadSnapshot, MessageSeparatorOption.Before | MessageSeparatorOption.After);

                var snapshot = snapshotDir.Snapshot;
                DisplayMessage(string.Format(InfoCmdL10n.InfoSnapshotVersion, snapshot!.Version));
                DisplayMessage(string.Format(InfoCmdL10n.InfoShanshotSchemaVersion, snapshot.SchemaVersion));

                var anchor = await snapshotDir.LoadGrainById<GrainPlain>(snapshot.Anchor.First(), cancellationToken: cancellationToken);
                if (null == anchor)
                {
                    DisplayMessage(string.Format(InfoCmdL10n.InfoAnchorMissing, string.Join("/", snapshot.Anchor.Reverse())));
                }
                else
                {
                    DisplayMessage(string.Format(InfoCmdL10n.InfoAnchor, anchor.Id, anchor.Path ?? "/"));
                }
                DisplayMessage(string.Format(InfoCmdL10n.InfoSnapshotScope, snapshot.Scope));
                DisplayMessage(string.Format(InfoCmdL10n.InfoSnapshotUpdated, snapshot.Updated));
                DisplayMessage(string.Format(InfoCmdL10n.InfoSnapshotCheckpoint, snapshot.Checkpoint));

                DisplayMessage(InfoCmdL10n.MsgHeadConnection, MessageSeparatorOption.Before | MessageSeparatorOption.After);

                var conn = snapshotDir.ConnectionSettings;
                if (null == conn)
                {
                    DisplayMessage(InfoCmdL10n.MsgNotConnected);
                }
                else
                {
                    var connState = new ConnectionCheckResult { Code = CmdResultCode.SnapshotStateError };
                    if (ValidateConnection)
                    {
                        connState = await ValidateBrokerConnection(_trackingService, conn, snapshotDir.Snapshot?.SchemaVersion, snapshotDir.BrokerInstanceId, cancellationToken);
                    }
                    DisplayMessage(string.Format(InfoCmdL10n.InfoConnectionURL, conn.BrokerUrl));
                    DisplayMessage(string.Format(InfoCmdL10n.InfoConnectionAuth, string.IsNullOrEmpty(conn.Authenticator) ? Enum.GetName(AuthenticationScheme.Auto) : conn.Authenticator));
                    DisplayMessage(string.Format(InfoCmdL10n.InfoConnectionStatus, CmdResultCode.SnapshotStateError == connState.Code ? InfoCmdL10n.ConnectionStatusUnknown : $"{connState.Code}"));
                    if (null != connState.Info)
                    {
                        DisplayMessage(string.Format(InfoCmdL10n.InfoConnectionAPIVersion, connState.Info.Version));
                        DisplayMessage(string.Format(InfoCmdL10n.InfoConnectionSchemaVersion, connState.Info.SchemaVersion));
                        DisplayMessage(string.Format(InfoCmdL10n.InfoConnectionInstanceID, connState.Info.InstanceId));
                    }
                    else
                    {
                        DisplayMessage(string.Format(InfoCmdL10n.InfoConnectionInstanceID, snapshotDir.BrokerInstanceId));
                    }
                }

                if (snapshotDir.IsConnected)
                {
                    DisplayMessage(InfoCmdL10n.MsgHeadSynchronization, MessageSeparatorOption.Before | MessageSeparatorOption.After);

                    var checkpoints = await snapshotDir.ListCheckpoints(cancellationToken);
                    DisplayMessage(string.Format(InfoCmdL10n.InfoNumberCheckpoints, checkpoints.Count));
                    DisplayMessage(string.Format(InfoCmdL10n.InfoActiveCheckpoint, snapshotDir.LocalCheckpoint!.Ordinal, snapshotDir.LocalCheckpoint.Latest));
                    if (null != snapshotDir.SharedCheckpoint)
                    {
                        DisplayMessage(string.Format(InfoCmdL10n.InfoSharedCheckpoint, snapshotDir.SharedCheckpoint.Ordinal, snapshotDir.SharedCheckpoint.Latest));
                    }
                    DisplayMessage(string.Format(InfoCmdL10n.InfoPushedCheckpoint, snapshotDir.LastPushCheckpoint));
                }

                return result;
            }
        }
    }
}
