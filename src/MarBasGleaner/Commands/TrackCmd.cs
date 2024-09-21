using System.CommandLine;
using System.CommandLine.Invocation;
using MarBasGleaner.Tracking;
using MarBasSchema;
using MarBasSchema.Broker;

namespace MarBasGleaner.Commands
{
    internal class TrackCmd(): ConnectCmd("track", "Sets up tracking of MarBas grains in local directory")
    {
        //public TrackCmd()
        //    : base("track", "Sets up tracking of MarBas grains in local directory")
        //{
        //}

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments", Justification = "The Setup() method is meant to be called once per lifetime")]
        protected override void Setup()
        {
            base.Setup();
            AddArgument(new Argument<string>("path-or-id", "Identifier of the top grain to track"));
            AddOption(new Option<SnapshotScope>(new [] { "--scope", "-s" }, () => SnapshotScope.Recursive, "Tracking scope"));
            AddOption(new Option<SourceControlFlavor>(new [] { "--scs", "-c" }, () => SourceControlFlavor.Git, "Source control system used for snapshots"));
            AddOption(new Option<Guid>("--ignore-grains", "List of grain IDs to ignore")
            {
                Arity = ArgumentArity.OneOrMore,
                AllowMultipleArgumentsPerToken = true
            });
            AddOption(new Option<Guid>("--ignore-types", "List of type IDs of grains to ignore")
            {
                Arity = ArgumentArity.OneOrMore,
                AllowMultipleArgumentsPerToken = true
            });
            AddOption(new Option<string>("--ignore-type-names", "List of type names of grains to ignore")
            {
                Arity = ArgumentArity.OneOrMore,
                AllowMultipleArgumentsPerToken = true
            });
        }

        public new class Worker(ITrackingService trackingService, ILogger<Worker> logger) : ConnectCmd.Worker(trackingService, (ILogger)logger)
        {
            public string? PathOrId { get; set; }
            public SnapshotScope Scope { get; set; } = SnapshotScope.Recursive;
            public SourceControlFlavor Scs { get; set; } = SourceControlFlavor.Git;
            public IEnumerable<Guid>? IgnoreGrains { get; set; }
            public IEnumerable<Guid>? IgnoreTypes { get; set; }
            public IEnumerable<string>? IgnoreTypeNames { get; set; }

            public override async Task<int> InvokeAsync(InvocationContext context)
            {
                if (null == Url || !Url.IsAbsoluteUri)
                {
                    return ReportError(CmdResultCode.ParameterError, $"'{Url}' is not a recognizable absolute URI");
                }
                var ctoken = context.GetCancellationToken();

                var anchorId = Guid.Empty;
                if (!PathOrId!.StartsWith($"/{SchemaDefaults.RootName}") && !Guid.TryParse(PathOrId, out anchorId))
                {
                    return ReportError(CmdResultCode.ParameterError, $"'{PathOrId}' is not a valid grain path nor a UID");
                }
                if ($"/{SchemaDefaults.RootName}" == PathOrId)
                {
                    anchorId = SchemaDefaults.RootID;
                }

                var snapshotDir = await _trackingService.GetSnapshotDirectoryAsync(Directory, cancellationToken: ctoken);
                if (snapshotDir.IsSnapshot)
                {
                    var errMsg = $"'{snapshotDir.FullPath}' already contains a tracking snapshot";
                    if (!snapshotDir.IsConnected)
                    {
                        errMsg += " which is unconnected, execute 'connect' command";
                    }
                    return ReportError(CmdResultCode.SnapshotStateError, errMsg);
                }

                DisplayMessage($"Setting up tracking of {Url} in {snapshotDir.FullPath}", MessageSeparatorOption.After);

                var snapshot = new Snapshot()
                {
                    Anchor = new Guid[] { anchorId },
                    Scope = Scope,
                    Checkpoint = 1
                };
                var connection = CreateConnectionSettings();

                using var client = _trackingService.GetBrokerClient(connection);

                var brokerStat = await CheckBrokerConnection(client, cancellationToken: ctoken);
                if (CmdResultCode.Success != brokerStat.Code)
                {
                    return (int)brokerStat.Code;
                }
                snapshot.SchemaVersion = brokerStat.Info!.SchemaVersion;

                var anchor = anchorId.Equals(Guid.Empty) ? await client.GetGrain(PathOrId.Remove(0, $"/{SchemaDefaults.RootName}/".Length), ctoken) : await client.GetGrain(anchorId, ctoken);
                if (null == anchor)
                {
                    return ReportError(CmdResultCode.AnchorGrainError, $"Anchor grain '{PathOrId}' could not be loaded");
                }
                var latest = anchor.MTime;

                snapshot.Anchor = (await client.GetGrainPath(anchor.Id, ctoken)).Select(grain => grain.Id);


                var grains = await client.ListGrains(anchor.Id, SnapshotScope.Recursive == (SnapshotScope.Recursive & Scope), cancellationToken: ctoken);

                try
                {
                    await snapshotDir.Initialize(brokerStat.Info.InstanceId, snapshot, Scs, connection, ctoken);
                }
                catch (Exception e)
                {
                    if (_logger.IsEnabled(LogLevel.Error))
                    {
                        _logger.LogError(e, "Initialization error");
                    }
                    snapshotDir.CleanUp();
                    return ReportError(CmdResultCode.SnapshotInitError, $"Error initializing snapshot directory '{snapshotDir.FullPath}': {e.Message}");
                }

                if (BuildIgnoreFilter(snapshotDir))
                {
                    await snapshotDir.StoreIgnores(ctoken);
                }

                if (SnapshotScope.Anchor == (Scope & SnapshotScope.Anchor))
                {
                    if (snapshotDir.IsIgnoredGrain(anchor))
                    {
                        DisplayWarning($"Anchor grain {anchor.Id} is in the ignore list");
                    }
                    else
                    {
                        var anchorImp = (await client.ImportGrains(new[] { anchor.Id }, ctoken)).FirstOrDefault();
                        if (null == anchorImp)
                        {
                            return ReportError(CmdResultCode.AnchorGrainError, $"Anchor grain {anchor.Id} doesn't seem to be exportable");
                        }
                        DisplayMessage($"Pulling grain {anchorImp.Id:D} ({anchorImp.Path ?? "/"})");
                        await snapshotDir.StoreGrain(anchorImp, cancellationToken: ctoken);
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

                var grainsImported = await client.ImportGrains(filteredIds, ctoken);
                await Parallel.ForEachAsync(grainsImported, ctoken, async (grain, token) =>
                {
                    DisplayMessage($"Pulling grain {grain.Id:D} ({grain.Path ?? "/"})");
                    await snapshotDir.StoreGrain(grain, cancellationToken: token);
                });

                snapshotDir.LocalCheckpoint!.Latest = latest;
                snapshot.Updated = DateTime.UtcNow;
                await snapshotDir.StoreMetadata(cancellationToken: ctoken);

                DisplayInfo($"Snapshot of {Url} created successfully");
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
