using CraftedSolutions.MarBasGleaner.Tracking;
using CraftedSolutions.MarBasGleaner.UI;
using CraftedSolutions.MarBasSchema;
using CraftedSolutions.MarBasSchema.Broker;
using diVISION.CommandLineX;
using System.CommandLine;

namespace CraftedSolutions.MarBasGleaner.Commands
{
    internal sealed class TrackCmd() : ConnectBaseCmd("track", TrackCmdL10n.CmdDesc)
    {
        protected override void Setup()
        {
            base.Setup();
            Add(new Argument<string>("path-or-id")
            {
                Description = TrackCmdL10n.IdArgDesc
            });
            Add(new Option<SnapshotScope>("--scope", "-s")
            {
                DefaultValueFactory = (_) => SnapshotScope.Recursive,
                Description = TrackCmdL10n.ScopeOptionDesc
            });
            Add(new Option<SourceControlFlavor>("--scs", "-c")
            {
                DefaultValueFactory = (_) => SourceControlFlavor.Git,
                Description = string.Format(TrackCmdL10n.ScsOptionDesc, Enum.GetName(SourceControlFlavor.Git))
            });
            Add(new Option<IEnumerable<Guid>>("--ignore-grains")
            {
                Description = TrackCmdL10n.IgnoreGrainsOptionDesc,
                Arity = ArgumentArity.OneOrMore,
                AllowMultipleArgumentsPerToken = true
            });
            Add(new Option<IEnumerable<Guid>>("--ignore-types")
            {
                Description = TrackCmdL10n.IgnoreTypesOptionDesc,
                Arity = ArgumentArity.OneOrMore,
                AllowMultipleArgumentsPerToken = true
            });
            Add(new Option<IEnumerable<string>>("--ignore-type-names")
            {
                Description = TrackCmdL10n.IgnoreTypeNamesOptionDesc,
                Arity = ArgumentArity.OneOrMore,
                AllowMultipleArgumentsPerToken = true
            });
        }

        public new sealed class Worker(ITrackingService trackingService, ILogger<Worker> logger) : ConnectBaseCmd.Worker(trackingService, (ILogger)logger)
        {
            public string? PathOrId { get; set; }
            public SnapshotScope Scope { get; set; } = SnapshotScope.Recursive;
            public SourceControlFlavor Scs { get; set; } = SourceControlFlavor.Git;
            public IEnumerable<Guid>? IgnoreGrains { get; set; }
            public IEnumerable<Guid>? IgnoreTypes { get; set; }
            public IEnumerable<string>? IgnoreTypeNames { get; set; }

            public override async Task<int> InvokeAsync(CommandActionContext context, CancellationToken cancellationToken = default)
            {
                if (null == Url || !Url.IsAbsoluteUri)
                {
                    return ReportError(CmdResultCode.ParameterError, string.Format(TrackCmdL10n.ErrorURL, Url));
                }

                var anchorId = Guid.Empty;
                if (!PathOrId!.StartsWith($"/{SchemaDefaults.RootName}") && !Guid.TryParse(PathOrId, out anchorId))
                {
                    return ReportError(CmdResultCode.ParameterError, string.Format(TrackCmdL10n.ErrorIdOrPath, PathOrId));
                }
                if ($"/{SchemaDefaults.RootName}" == PathOrId)
                {
                    anchorId = SchemaDefaults.RootID;
                }

                var snapshotDir = await _trackingService.GetSnapshotDirectoryAsync(Directory, cancellationToken: cancellationToken);
                if (snapshotDir.IsSnapshot)
                {
                    return ReportError(CmdResultCode.SnapshotStateError,
                        string.Format(snapshotDir.IsConnected ? TrackCmdL10n.ErrorSnapshotState : TrackCmdL10n.ErrorSnapshotExists, snapshotDir.FullPath));
                }

                DisplayMessage(string.Format(TrackCmdL10n.MsgCmdStart, Url, snapshotDir.FullPath), MessageSeparatorOption.After);

                var snapshot = new Snapshot()
                {
                    Anchor = new Guid[] { anchorId },
                    Scope = Scope,
                    Checkpoint = 1
                };
                var connection = CreateConnectionSettings();

                var brokerStat = await ValidateBrokerConnection(_trackingService, connection, cancellationToken: cancellationToken);
                if (CmdResultCode.Success != brokerStat.Code)
                {
                    return (int)brokerStat.Code;
                }
                snapshot.SchemaVersion = brokerStat.Info!.SchemaVersion;

                using var client = await _trackingService.GetBrokerClientAsync(connection, StoreCredentials, cancellationToken);

                var anchor = anchorId.Equals(Guid.Empty) ? await client.GetGrain(PathOrId.Remove(0, $"/{SchemaDefaults.RootName}/".Length), cancellationToken) : await client.GetGrain(anchorId, cancellationToken: cancellationToken);
                if (null == anchor)
                {
                    return ReportError(CmdResultCode.AnchorGrainError, string.Format(TrackCmdL10n.ErrorAnchorLoad, PathOrId));
                }
                var latest = anchor.MTime;

                snapshot.Anchor = (await client.GetGrainPath(anchor.Id, cancellationToken)).Select(grain => grain.Id);


                var grains = await client.ListGrains(anchor.Id, SnapshotScope.Recursive == (SnapshotScope.Recursive & Scope), cancellationToken: cancellationToken);

                try
                {
                    await snapshotDir.Initialize(brokerStat.Info.InstanceId, snapshot, Scs, connection, cancellationToken);
                }
                catch (Exception e)
                {
                    if (_logger.IsEnabled(LogLevel.Error))
                    {
                        _logger.LogError(e, "Initialization error");
                    }
                    snapshotDir.CleanUp();
                    return ReportError(CmdResultCode.SnapshotInitError, string.Format(TrackCmdL10n.ErrorInitializationException, snapshotDir.FullPath, e.Message));
                }

                if (BuildIgnoreFilter(snapshotDir))
                {
                    await snapshotDir.StoreIgnores(cancellationToken);
                }

                if (SnapshotScope.Anchor == (Scope & SnapshotScope.Anchor))
                {
                    if (snapshotDir.IsIgnoredGrain(anchor))
                    {
                        DisplayWarning(string.Format(TrackCmdL10n.WarnAnchorIgnored, anchor.Id));
                    }
                    else
                    {
                        var anchorImp = (await client.PullGrains(new[] { anchor.Id }, cancellationToken)).FirstOrDefault();
                        if (null == anchorImp)
                        {
                            return ReportError(CmdResultCode.AnchorGrainError, string.Format(TrackCmdL10n.ErrorAnchorImport));
                        }
                        DisplayMessage(string.Format(TrackCmdL10n.StatusGrainPull, anchorImp.Id, anchorImp.Path ?? "/"));
                        await snapshotDir.StoreGrain(anchorImp, cancellationToken: cancellationToken);
                    }
                }

                var ignoredParents = new HashSet<Guid>();
                var filteredIds = grains.Where((grain) =>
                {
                    if (null != grain.ParentId && ignoredParents.Contains((Guid)grain.ParentId))
                    {
                        return false;
                    }
                    if (snapshotDir.IsIgnoredGrain(grain))
                    {
                        ignoredParents.Add(grain.Id);
                        return false;
                    }

                    if (grain.MTime > latest)
                    {
                        latest = grain.MTime;
                    }
                    return true;
                }).Select(x => x.Id);

                var pages = (int)Math.Ceiling((decimal)filteredIds.Count() / 100);
                for (var page = 0; page < pages; page++)
                {
                    var block = 1 < pages ? filteredIds.Skip(page * 100).Take(100) : filteredIds;
                    var grainsImported = await client.PullGrains(block, cancellationToken);
                    await Parallel.ForEachAsync(grainsImported, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken }, async (grain, token) =>
                    {
                        DisplayMessage(string.Format(TrackCmdL10n.StatusGrainPull, grain.Id, grain.Path ?? "/"));
                        await snapshotDir.StoreGrain(grain, cancellationToken: token);
                    });
                }

                snapshotDir.LocalCheckpoint!.Latest = latest;
                snapshot.Updated = DateTime.UtcNow;
                await snapshotDir.StoreMetadata(cancellationToken: cancellationToken);

                DisplayInfo(string.Format(TrackCmdL10n.MsgCmdSuccess, Url));
                return 0;
            }

            private bool BuildIgnoreFilter(SnapshotDirectory snapshotDir)
            {
                var result = false;
                if (true == IgnoreGrains?.Any() || true == IgnoreTypes?.Any() || true == IgnoreTypeNames?.Any())
                {
                    if (null == snapshotDir.Ignores)
                    {
                        snapshotDir.Ignores = new GrainBasicFilter();
                    }
                    if (true == IgnoreGrains?.Any())
                    {
                        var idIgnores = snapshotDir.Ignores.IdConstraints?.ToList() ?? [];
                        idIgnores.AddRange(IgnoreGrains);
                        snapshotDir.Ignores.IdConstraints = idIgnores;
                        result = true;
                    }
                    if (true == IgnoreTypes?.Any() || true == IgnoreTypeNames?.Any())
                    {
                        var typeIgnores = snapshotDir.Ignores.TypeConstraints?.ToList() ?? [];
                        if (null != IgnoreTypes)
                        {
                            typeIgnores.AddRange(IgnoreTypes.Select(x => new SimpleTypeConstraint(x)));
                        }
                        if (null != IgnoreTypeNames)
                        {
                            typeIgnores.AddRange(IgnoreTypeNames.Select(x => new SimpleTypeConstraint(Guid.Empty, x)));
                        }
                    }
                }
                return result;
            }
        }
    }
}
