using CraftedSolutions.MarBasCommon;
using CraftedSolutions.MarBasGleaner.BrokerAPI;
using CraftedSolutions.MarBasGleaner.Tracking;
using CraftedSolutions.MarBasGleaner.UI;
using CraftedSolutions.MarBasSchema.Grain;
using CraftedSolutions.MarBasSchema.Transport;
using diVISION.CommandLineX;
using System.CommandLine;
using System.Text;

namespace CraftedSolutions.MarBasGleaner.Commands
{
    internal sealed class CleanUpCmd : GenericCmd
    {
        public CleanUpCmd()
            : base("clean-up", CleanUpCmdL10n.CmdDesc)
        {
            Setup();
        }

        protected override void Setup()
        {
            base.Setup();
            Add(new Option<bool>("--unattended", "-a")
            {
                Description = CleanUpCmdL10n.UnattendedOptionDesc
            });
        }

        public new sealed class Worker(ITrackingService trackingService, IFeedbackService feedbackService, ILogger<Worker> logger) :
            GenericCmd.Worker(trackingService, feedbackService, (ILogger)logger)
        {
            public bool Unatteded { get; set; } = false;

            public async override Task<int> InvokeAsync(CommandActionContext context, CancellationToken cancellationToken = default)
            {
                var snapshotDir = await _trackingService.GetSnapshotDirectoryAsync(Directory, cancellationToken);

                var result = ValidateSnapshot(snapshotDir);
                if (0 != result)
                {
                    return result;
                }

                var hasConnection = false;
                var brokerStat = await ValidateBrokerConnection(_trackingService, snapshotDir.ConnectionSettings!, snapshotDir.Snapshot?.SchemaVersion, snapshotDir.BrokerInstanceId, cancellationToken);
                if (CmdResultCode.Success == brokerStat.Code)
                {
                    hasConnection = true;
                    if (0 == snapshotDir.ConnectionSettings!.AuthenticatorParams.Count)
                    {
                        if (Unatteded)
                        {
                            brokerStat.Code = CmdResultCode.AuthProviderError;
                            ReportError(brokerStat.Code, CleanUpCmdL10n.ErrorUnattendedBrokerCredentials);
                            hasConnection = false;
                        }
                        else
                        {
                            snapshotDir.ConnectionSettings.AuthenticatorParams[ConnectionSettings.ParamSessionOnly] = "true";
                        }
                    }
                }
                if (Unatteded && !hasConnection)
                {
                    return (int)brokerStat.Code;
                }

                DisplayMessage(string.Format(CleanUpCmdL10n.MsgCmdStart, snapshotDir.FullPath), MessageSeparatorOption.After);

                if (!(await ScanCheckpoints(snapshotDir, cancellationToken)))
                {
                    return ReportError(CmdResultCode.SnapshotStateError, string.Format(CleanUpCmdL10n.ErrorUnreparedCheckpoints, snapshotDir.FullPath));
                }

                var conflated = await snapshotDir.LoadConflatedCheckpoint(SnapshotCheckpoint.BaseOrdinal, cancellationToken);
                var targetCheckpoint = snapshotDir.CreateDraftCheckpoint();

                DisplayMessage(CleanUpCmdL10n.StatuScanningGrains, MessageSeparatorOption.Before);
                foreach (var readId in snapshotDir.ListGrainIds())
                {
                    var isMod = conflated.Modifications.Contains(readId);
                    var isDel = conflated.Deletions.Contains(readId);
                    conflated.Modifications.Remove(readId);

                    var grain = await snapshotDir.LoadGrainById<GrainTransportable>(readId, cancellationToken: cancellationToken);
                    if (null == grain || grain.Id != readId)
                    {
                        DisplayMessage(string.Format(CleanUpCmdL10n.MsgCorruptGrain, readId));
                        var tryToDelete = false;
                        if (!isDel && hasConnection)
                        {
                            var choice = Unatteded ? 1 : _feedbackService.GetChoice(null, 2, CleanUpCmdL10n.ChoicePullGrain, CleanUpCmdL10n.ChoiceDeleteGrain, CleanUpCmdL10n.ChoiceNoop);
                            switch (choice)
                            {
                                case 0:
                                    if (!(await PullGrain(snapshotDir, readId, targetCheckpoint, !isMod, cancellationToken)))
                                    {
                                        tryToDelete = true;
                                    }
                                    break;
                                case 1:
                                    DeleteGrainWithCTA(snapshotDir, (Identifiable)readId, targetCheckpoint, isMod, true);
                                    break;
                            }
                        }
                        else
                        {
                            tryToDelete = true;
                        }
                        if (tryToDelete)
                        {
                            DeleteGrainWithCTA(snapshotDir, (Identifiable)readId, targetCheckpoint, !isDel, Unatteded);
                        }
                        continue;
                    }

                    if (isDel)
                    {
                        DisplayMessage(string.Format(CleanUpCmdL10n.MsgDanglingGrain, grain.Id, grain.Path ?? " / "));
                        DeleteGrainWithCTA(snapshotDir, grain, targetCheckpoint, false, Unatteded);
                    }
                    else if (!isMod)
                    {
                        DisplayMessage(string.Format(CleanUpCmdL10n.MsgOrphanGrain, grain.Id, grain.Path ?? " / "));
                        var choice = Unatteded ? 1 : _feedbackService.GetChoice(null, 2, CleanUpCmdL10n.ChoiceAddGrain, CleanUpCmdL10n.ChoiceDeleteGrain, CleanUpCmdL10n.ChoiceNoop);
                        switch (choice)
                        {
                            case 0:
                                targetCheckpoint.Modifications.Add(grain.Id);
                                break;
                            case 1:
                                DeleteGrainWithCTA(snapshotDir, grain, targetCheckpoint, false, true);
                                break;
                        }
                    }
                }

                if (0 < conflated.Modifications.Count)
                {
                    foreach (var id in conflated.Modifications)
                    {
                        var pulled = false;
                        if (Unatteded || _feedbackService.AskYesNo(string.Format(CleanUpCmdL10n.AskPullMissingGrain, id), true))
                        {
                            pulled = await PullGrain(snapshotDir, id, targetCheckpoint, false, cancellationToken);
                        }
                        if (!pulled)
                        {
                            DisplayMessage(string.Format(CleanUpCmdL10n.StatusGrainMarkDeleted, id));
                            targetCheckpoint.Deletions.Add(id);
                        }
                    }
                }

                if (0 < targetCheckpoint.Modifications.Count + targetCheckpoint.Deletions.Count)
                {
                    DisplayInfo(string.Format(CleanUpCmdL10n.MsgCmdSuccess,
                        targetCheckpoint.Modifications.Count + targetCheckpoint.Deletions.Count, snapshotDir.FullPath, targetCheckpoint.Ordinal));

                    await snapshotDir.StoreCheckpoint(targetCheckpoint, cancellationToken: cancellationToken);

                    snapshotDir.Snapshot!.Updated = DateTime.UtcNow;
                    snapshotDir.Snapshot.Checkpoint = targetCheckpoint.Ordinal;
                    await snapshotDir.StoreSnapshot(cancellationToken);
                }
                else
                {
                    DisplayInfo(string.Format(CleanUpCmdL10n.MsgCmdSuccessNoop, snapshotDir.FullPath));
                }
                if (hasConnection && !snapshotDir.ConnectionSettings!.AuthenticatorParams.ContainsKey(ConnectionSettings.ParamSessionOnly)
                    && 0 < snapshotDir.ConnectionSettings.AuthenticatorParams.Count)
                {
                    await snapshotDir.StoreLocalState(false, cancellationToken);
                }
                return result;
            }

            private async Task<bool> ScanCheckpoints(SnapshotDirectory snapshotDir, CancellationToken cancellationToken)
            {
                var result = true;
                DisplayMessage(CleanUpCmdL10n.StatusScanningCheckpoints);

                await snapshotDir.RestoreLocalCheckpointIfNeeded(cancellationToken);

                var missing = new HashSet<int>();
                var highestOrdinal = snapshotDir.Snapshot!.Checkpoint;
                for (var i = 1; i <= highestOrdinal; i++)
                {
                    if (!snapshotDir.HasCheckpoint(i))
                    {
                        missing.Add(i);
                    }
                }
                var localOk = false;
                var lastPushOk = 1 > snapshotDir.LastPushCheckpoint;
                var existing = new Dictionary<int, SnapshotCheckpoint>();
                foreach (var cp in await snapshotDir.ListCheckpoints(cancellationToken: cancellationToken))
                {
                    existing[cp.Ordinal] = cp;
                    if (snapshotDir.LocalCheckpoint!.Ordinal == cp.Ordinal)
                    {
                        localOk = true;
                    }
                    if (snapshotDir.LastPushCheckpoint == cp.Ordinal)
                    {
                        lastPushOk = true;
                    }
                    highestOrdinal = cp.Ordinal;
                }

                if (!localOk || !lastPushOk || highestOrdinal != snapshotDir.Snapshot.Checkpoint || 0 < missing.Count)
                {
                    result = false;
                    StringBuilder msg = new (string.Format(CleanUpCmdL10n.MsgCheckpointErrors, snapshotDir.FullPath, Environment.NewLine));
                    if (!localOk)
                    {
                        msg.Append(string.Format(CleanUpCmdL10n.MsgPartLocalCheckpointMissing, snapshotDir.LocalCheckpoint!.Ordinal));
                        msg.Append(Environment.NewLine);
                    }
                    if (!lastPushOk && snapshotDir.LocalCheckpoint!.Ordinal != snapshotDir.LastPushCheckpoint)
                    {
                        msg.Append(string.Format(CleanUpCmdL10n.MsgPartLastPushedMissing, snapshotDir.LastPushCheckpoint));
                        msg.Append(Environment.NewLine);
                    }
                    if (0 < missing.Count)
                    {
                        var remains = missing.Where(x => x != snapshotDir.LastPushCheckpoint && x != snapshotDir.LocalCheckpoint!.Ordinal);
                        if (remains.Any())
                        {
                            msg.Append(string.Format(CleanUpCmdL10n.MsgPartCheckpointsMissing, string.Join(", #", remains)));
                            msg.Append(Environment.NewLine);
                        }
                    }
                    if (highestOrdinal != snapshotDir.Snapshot.Checkpoint)
                    {
                        msg.Append(CleanUpCmdL10n.MsgPartLatestCheckpointNotRegistered);
                        msg.Append(Environment.NewLine);
                    }
                    DisplayMessage(msg.ToString());

                    if (Unatteded || _feedbackService.AskYesNo(CleanUpCmdL10n.AskCorrectCheckpointErrors))
                    {
                        SnapshotCheckpoint? prevCp = null;
                        var pushedCandidate = 1;
                        void CheckPushedCandidate(SnapshotCheckpoint candidate)
                        {
                            if (candidate.Ordinal <= snapshotDir.LastPushCheckpoint)
                            {
                                pushedCandidate = candidate.Ordinal;
                            }
                        }
                        for (var i = 1; i <= highestOrdinal; i++)
                        {
                            if (i == snapshotDir.LocalCheckpoint!.Ordinal)
                            {
                                if (!localOk)
                                {
                                    DisplayMessage(string.Format(CleanUpCmdL10n.StatusRestoreLocalCheckpoint, i));
                                    await snapshotDir.StoreCheckpoint(snapshotDir.LocalCheckpoint, cancellationToken: cancellationToken);
                                    localOk = true;
                                }
                                if (!lastPushOk)
                                {
                                    CheckPushedCandidate(snapshotDir.LocalCheckpoint);
                                }
                            }
                            else
                            {
                                if (existing.TryGetValue(i, out var existCp))
                                {
                                    prevCp = existCp;
                                    if (!lastPushOk)
                                    {
                                        CheckPushedCandidate(existCp);
                                    }
                                }
                                else
                                {
                                    DisplayMessage(string.Format(CleanUpCmdL10n.StatusCreateMissingCheckpoint, i));
                                    var cp = new SnapshotCheckpoint()
                                    {
                                        InstanceId = (Guid)snapshotDir.BrokerInstanceId!,
                                        Ordinal = i
                                    };
                                    if (null != prevCp)
                                    {
                                        cp.Latest = prevCp.Latest;
                                    }
                                    await snapshotDir.StoreCheckpoint(cp, cancellationToken: cancellationToken);
                                }
                            }
                        }
                        if (!lastPushOk || highestOrdinal != snapshotDir.Snapshot.Checkpoint)
                        {
                            if (!lastPushOk)
                            {
                                DisplayMessage(string.Format(CleanUpCmdL10n.StatusRepairLastPushed, pushedCandidate));
                                snapshotDir.LastPushCheckpoint = pushedCandidate;
                            }
                            if (highestOrdinal != snapshotDir.Snapshot.Checkpoint)
                            {
                                DisplayMessage(string.Format(CleanUpCmdL10n.StatusRegisterLatestCheckpoint, highestOrdinal));
                                snapshotDir.Snapshot.Checkpoint = highestOrdinal;
                            }
                            await snapshotDir.StoreMetadata(false, cancellationToken);
                        }
                        result = true;
                    }
                }
                return result;
            }

            private async Task<bool> PullGrain(SnapshotDirectory snapshotDir, Guid id, SnapshotCheckpoint checkpoint, bool addToModifications = true, CancellationToken cancellationToken = default)
            {
                DisplayMessage(string.Format(CleanUpCmdL10n.StatusPullGrain, id));
                using var client = await _trackingService.GetBrokerClientAsync(snapshotDir.ConnectionSettings!, cancellationToken);
                var grain = (await client.PullGrains([id], cancellationToken)).FirstOrDefault();
                if (null == grain)
                {
                    DisplayWarning(string.Format(CleanUpCmdL10n.WarnGrainNotFound, id, client.APIUrl));
                    return false;
                }
                await snapshotDir.StoreGrain(grain, false, cancellationToken: cancellationToken);
                if (addToModifications)
                {
                    checkpoint.Modifications.Add(id);
                }
                if (checkpoint.Latest < grain.MTime)
                {
                    checkpoint.Latest = grain.MTime;
                }
                return true;
            }

            private bool DeleteGrainWithCTA(SnapshotDirectory snapshotDir, IIdentifiable grain, SnapshotCheckpoint checkpoint, bool addToDeletions = true, bool suppressCTA = false)
            {
                var grainPath = grain is IGrain actual ? actual.Path : "-";
                var result = suppressCTA || _feedbackService.AskYesNo(string.Format(CleanUpCmdL10n.AskDeleteGrain, grain.Id, grainPath));
                if (result)
                {
                    DisplayMessage(string.Format(CleanUpCmdL10n.StatusDeleteGrain, grain.Id, grainPath));
                    snapshotDir.DeleteGrains([grain]);
                    checkpoint.Modifications.Remove(grain.Id);
                    if (addToDeletions)
                    {
                        checkpoint.Deletions.Add(grain.Id);
                    }
                }
                return result;
            }
        }
    }
}
