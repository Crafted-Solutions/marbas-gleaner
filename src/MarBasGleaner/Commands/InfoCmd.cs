using System.CommandLine;
using System.CommandLine.Invocation;
using MarBasGleaner.Tracking;
using MarBasSchema.Grain;

namespace MarBasGleaner.Commands
{
    internal class InfoCmd : GenericCmd
    {
        public InfoCmd()
            : base("info", InfoCmdL10n.CmdDesc)
        {
            Setup();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments", Justification = "The Setup() method is meant to be called once per lifetime")]
        protected override void Setup()
        {
            base.Setup();
            AddOption(new Option<bool>(new[] { "-c", "--validate-connection" }, InfoCmdL10n.ValidateConnectionDesc));
        }

        public new sealed class Worker(ITrackingService trackingService, ILogger<Worker> logger) : GenericCmd.Worker(trackingService, (ILogger)logger)
        {
            public bool ValidateConnection { get; set; } = false;

            public override async Task<int> InvokeAsync(InvocationContext context)
            {
                var ctoken = context.GetCancellationToken();
                var snapshotDir = await _trackingService.GetSnapshotDirectoryAsync(Directory, ctoken);

                var result = ValidateSnapshot(snapshotDir, false, false);
                if (0 != result)
                {
                    return result;
                }

                DisplayMessage(InfoCmdL10n.MsgHeadSnapshot, MessageSeparatorOption.Before | MessageSeparatorOption.After);

                var snapshot = snapshotDir.Snapshot;
                DisplayMessage(string.Format(InfoCmdL10n.InfoSnapshotVersion, snapshot!.Version));
                DisplayMessage(string.Format(InfoCmdL10n.InfoShanshotSchemaVersion, snapshot.SchemaVersion));

                var anchor = await snapshotDir.LoadGrainById<GrainPlain>(snapshot.Anchor.Last(), cancellationToken: ctoken);
                if (null == anchor)
                {
                    DisplayMessage(string.Format(InfoCmdL10n.InfoAnchorMissing, string.Join("/", snapshot.Anchor)));
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
                        using var client = _trackingService.GetBrokerClient(conn);
                        connState = await ValidateBrokerConnection(client, snapshotDir.Snapshot?.SchemaVersion, snapshotDir.BrokerInstanceId, ctoken);
                    }
                    DisplayMessage(string.Format(InfoCmdL10n.InfoConnectionURL, conn.BrokerUrl));
                    DisplayMessage(string.Format(InfoCmdL10n.InfoConnectionAuth, conn.Authenticator));
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

                    var checkpoints = await snapshotDir.ListCheckpoints(ctoken);
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
