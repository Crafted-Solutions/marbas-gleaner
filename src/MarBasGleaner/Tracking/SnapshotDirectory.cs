using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MarBasGleaner.BrokerAPI;
using MarBasGleaner.Json;
using MarBasSchema;
using MarBasSchema.Broker;
using MarBasSchema.Grain;

namespace MarBasGleaner.Tracking
{
    internal class SnapshotDirectory(string path = SnapshotDirectory.DefaultPath)
    {
        public const string DefaultPath = ".";
        public const string SnapshotFile = ".mbgsnapshot.json";
        public const string LocalStateFile = ".mbglocal.json";
        public const string IgnoresFile = ".mbgignore.json";
        public const string FileNameFieldSeparator = ",";

        private readonly string _fullPath = Path.GetFullPath(path);
        private Snapshot? _snapshot;
        private LocalStateModel? _localState;
        private IGrainBasicFilter? _igonres;
        private SnapshotCheckpoint? _sharedCheckpoint;

        public Snapshot? SharedSnapshot => _snapshot;

        public Snapshot? LocalSnapshot => _localState?.Snapshot;

        public SnapshotCheckpoint? LocalCheckpoint => _localState?.LatestCheckpoint;

        public SnapshotCheckpoint? SharedCheckpoint => _sharedCheckpoint ?? LocalCheckpoint;

        public ConnectionSettings? ConnectionSettings => _localState?.Connection;

        public IGrainBasicFilter? Ignores { get => _igonres; set => _igonres = value; }

        public string FullPath => _fullPath;

        public bool IsDirectory => Directory.Exists(_fullPath);

        public bool IsSnapshot => File.Exists(Path.Combine(_fullPath, SnapshotFile));

        public bool IsConnected => File.Exists(Path.Combine(_fullPath, LocalStateFile));

        public bool HasIgnores => File.Exists(Path.Combine(_fullPath, IgnoresFile));

        public bool IsIgnoredGrain(IGrain grain)
        {
            var result = false;
            if (null != _igonres && (null != _igonres.IdConstraints || null != _igonres.TypeConstraints))
            {
                result = true == _igonres.IdConstraints?.Any(x => x == grain.Id || x == grain.ParentId);
                if (!result)
                {
                    result = true == _igonres.TypeConstraints?.Any(x => x.TypeDefId == grain.TypeDefId || x.TypeName == (grain as ITypeConstraint)?.TypeName);
                }
            }
            return result;
        }

        public async Task Initialize(Snapshot snapshot, SourceControlFlavor scs = SourceControlFlavor.Git, ConnectionSettings? connection = null, CancellationToken cancellationToken = default)
        {
            if (IsSnapshot)
            {
                throw new ApplicationException($"{_fullPath} is already initialized");
            }

            _snapshot = snapshot;
            if (null == _localState)
            {
                _localState = new LocalStateModel()
                {
                    Snapshot = snapshot,
                    Connection = connection,
                    LatestCheckpoint = new SnapshotCheckpoint()
                    {
                        InstanceId = snapshot.InstanceId,
                        Ordinal = 1,
                        Latest = snapshot.Updated
                    }
                };
            }
            await StoreMetadata(cancellationToken: cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (SourceControlFlavor.Git == scs)
            {
                await File.WriteAllTextAsync(Path.Combine(_fullPath, ".gitignore"), LocalStateFile, cancellationToken);
            }
        }

        public async Task Connect(ConnectionSettings connection, Guid instanceId, int adoptCheckpoint = 0, DateTime? timestamp = null, CancellationToken cancellationToken = default)
        {
            if (IsConnected)
            {
                throw new ApplicationException($"{_fullPath} is already connected");
            }
            if (null == _snapshot)
            {
                throw new ApplicationException("Snapshot is not loaded");
            }
            if (null == _localState)
            {
                _localState = new LocalStateModel()
                {
                    Snapshot = new Snapshot()
                    {
                        SchemaVersion = _snapshot.SchemaVersion,
                        Anchor = _snapshot.Anchor,
                        Scope = _snapshot.Scope,
                        InstanceId = instanceId,
                        Updated = timestamp ?? DateTime.UtcNow
                    }
                };
            }
            _localState.Connection = connection;
            if (0 < _snapshot.Checkpoint && !HasCheckpoint(_snapshot.Checkpoint))
            {
                Console.Write($"LatestCheckpoint {_snapshot.Checkpoint} not found, creating it{Environment.NewLine}");
                _sharedCheckpoint = new SnapshotCheckpoint()
                {
                    InstanceId = instanceId,
                    Ordinal = _snapshot.Checkpoint
                };
                await foreach (var grain in ListGrains<GrainPlain>(cancellationToken))
                {
                    if (null == grain)
                        continue;

                    _sharedCheckpoint.Additions.Add(grain.Id);
                    if (grain.MTime > _sharedCheckpoint.Latest)
                    {
                        _sharedCheckpoint.Latest = grain.MTime;
                    }
                }
                if (adoptCheckpoint != _snapshot.Checkpoint)
                {
                    await StoreCheckpoint(_sharedCheckpoint, cancellationToken: cancellationToken);
                }
            }
            if (0 != adoptCheckpoint)
            {
                _localState.Snapshot.Checkpoint = -1 == adoptCheckpoint ? _snapshot.Checkpoint : adoptCheckpoint;
                if (-1 == adoptCheckpoint)
                {
                    _localState.LatestCheckpoint = _sharedCheckpoint!;
                }
                else
                {
                    var cp = await LoadCheckpoint(adoptCheckpoint, cancellationToken);
                    if (null == cp)
                    {
                        throw new ApplicationException($"Checkpoint {adoptCheckpoint} not found");
                    }
                    _localState.LatestCheckpoint = cp;
                }
            }

            await StoreLocalState(0 != adoptCheckpoint, cancellationToken);
        }

        public async Task StoreMetadata(bool includeCheckpoint = true, CancellationToken cancellationToken = default)
        {
            if ((null == _snapshot && null == _localState) || cancellationToken.IsCancellationRequested)
            {
                return;
            }
            if (!IsDirectory)
            {
                Directory.CreateDirectory(_fullPath);
            }
            if (!IsDirectory)
            {
                throw new ApplicationException($"{_fullPath} could not be created");
            }
            await StoreSnapshot(cancellationToken);
            await StoreLocalState(includeCheckpoint, cancellationToken);
        }

        public async Task StoreSnapshot(CancellationToken cancellationToken = default)
        {
            if (!PreStoreChecks(cancellationToken))
            {
                return;
            }
            if (null != _snapshot)
            {
                await File.WriteAllTextAsync(Path.Combine(_fullPath, SnapshotFile), JsonSerializer.Serialize(_snapshot, JsonDefaults.SerializationOptions), cancellationToken);
            }
        }

        public async Task StoreLocalState(bool includeCheckpoint = true, CancellationToken cancellationToken = default)
        {
            if (!PreStoreChecks(cancellationToken))
            {
                return;
            }
            if (null != _localState && null != _localState.Connection)
            {
                await File.WriteAllTextAsync(Path.Combine(_fullPath, LocalStateFile), JsonSerializer.Serialize(_localState, JsonDefaults.SerializationOptions), cancellationToken);
            }
            if (includeCheckpoint && null != _localState?.LatestCheckpoint)
            {
                await StoreCheckpoint(_localState.LatestCheckpoint, true, cancellationToken);
            }
        }

        public async Task StoreIgnores(CancellationToken cancellationToken = default)
        {
            if (!PreStoreChecks(cancellationToken))
            {
                return;
            }
            if (null != _igonres)
            {
                await File.WriteAllTextAsync(Path.Combine(_fullPath, IgnoresFile), JsonSerializer.Serialize(_igonres, JsonDefaults.SerializationOptions), cancellationToken);
            }
        }

        public async Task LoadMetadata(CancellationToken cancellationToken = default)
        {
            if (!IsDirectory || cancellationToken.IsCancellationRequested)
            {
                return;
            }
            if (IsConnected)
            {
                _localState = JsonSerializer.Deserialize<LocalStateModel>(await File.ReadAllTextAsync(Path.Combine(_fullPath, LocalStateFile), cancellationToken), JsonDefaults.SerializationOptions);
            }
            if (IsSnapshot && !cancellationToken.IsCancellationRequested)
            {
                _snapshot = JsonSerializer.Deserialize<Snapshot>(await File.ReadAllTextAsync(Path.Combine(_fullPath, SnapshotFile), cancellationToken), JsonDefaults.SerializationOptions);
                if (null != _snapshot && HasCheckpoint(_snapshot.Checkpoint))
                {
                    _sharedCheckpoint = await LoadCheckpoint(_snapshot.Checkpoint, cancellationToken);
                }
            }
            if (HasIgnores && !cancellationToken.IsCancellationRequested)
            {
                _igonres = JsonSerializer.Deserialize<GrainBasicFilter>(await File.ReadAllTextAsync(Path.Combine(_fullPath, IgnoresFile), cancellationToken), JsonDefaults.SerializationOptions);
            }
        }

        public bool HasCheckpoint(int checkpointNum)
        {
            return File.Exists(Path.Combine(_fullPath, GetCheckpointFileName(checkpointNum)));
        }

        public async Task<SnapshotCheckpoint?> LoadCheckpoint(int checkpointNum = -1, CancellationToken cancellationToken = default)
        {
            if (!IsDirectory || cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            if (1 > checkpointNum)
            {
                if (null != _localState)
                {
                    checkpointNum = _localState.Snapshot.Checkpoint;
                }
            }
            if (1 > checkpointNum)
            {
                throw new ApplicationException($"Invalid checkpoint number {checkpointNum}");
            }
            var result = JsonSerializer.Deserialize<SnapshotCheckpoint>(await File.ReadAllTextAsync(Path.Combine(_fullPath, GetCheckpointFileName(checkpointNum)), cancellationToken), JsonDefaults.SerializationOptions);
            if (null != result && result.Ordinal != checkpointNum)
            {
                throw new ApplicationException($"Wrong checkpoint number {result.Ordinal} <> expected {checkpointNum}");
            }
            return result;
        }

        public async Task<SnapshotCheckpoint> LoadIntegratedCheckpoint(CancellationToken cancellationToken = default)
        {
            var result = _localState?.LatestCheckpoint.Clone(true) ?? new();
            if (!IsDirectory || cancellationToken.IsCancellationRequested || null == SharedCheckpoint || result.IsSame(SharedCheckpoint))
            {
                return result;
            }
            for (var i = Math.Max(result.Ordinal, 1); i <= SharedCheckpoint.Ordinal; i++)
            {
                var cp = await LoadCheckpoint(i, cancellationToken);
                if (null == cp || result.IsSame(cp))
                {
                    continue;
                }
                result.Additions.UnionWith(cp.Additions);
                result.Additions.ExceptWith(cp.Deletions);
                result.Deletions.UnionWith(cp.Deletions);
                result.Ordinal = cp.Ordinal;
                result.Latest = cp.Latest;
            }
            if (1 > result.Ordinal)
            {
                result.Ordinal = 1;
            }
            return result;
        }

        public async Task<IList<SnapshotCheckpoint>> ListCheckpoints(CancellationToken cancellationToken = default)
        {
            var result = new SortedList<int, SnapshotCheckpoint>();
            foreach (var fname in Directory.EnumerateFiles(_fullPath, $"checkpoint{FileNameFieldSeparator}{new string('?', 8)}.json"))
            {
                using (var stream = File.OpenRead(fname))
                {
                    var cp = await JsonSerializer.DeserializeAsync<SnapshotCheckpoint>(stream, JsonDefaults.DeserializationOptions, cancellationToken);
                    if (null != cp)
                    {
                        result.Add(cp.Ordinal, cp);
                    }
                }
            }
            return result.Values;
        }

        public async Task StoreCheckpoint(SnapshotCheckpoint checkpoint, bool isCurrent = false, CancellationToken cancellationToken = default)
        {
            if (!PreStoreChecks(cancellationToken))
            {
                return;
            }
            await File.WriteAllTextAsync(Path.Combine(_fullPath, GetCheckpointFileName(checkpoint.Ordinal)), JsonSerializer.Serialize(checkpoint, JsonDefaults.SerializationOptions), cancellationToken);
            if (isCurrent && null != _localState)
            {
                _localState.LatestCheckpoint = checkpoint;
                _localState.Snapshot.Checkpoint = checkpoint.Ordinal;
            }
        }

        public void CleanUp()
        {
            if (Directory.Exists(_fullPath))
            {
                try
                {
                    if (IsConnected)
                    {
                        File.Delete(Path.Combine(_fullPath, LocalStateFile));
                    }
                    if (IsSnapshot)
                    {
                        File.Delete(Path.Combine(_fullPath, SnapshotFile));
                    }
                    if (HasIgnores)
                    {
                        File.Delete(Path.Combine(_fullPath, IgnoresFile));
                    }
                    var di = new DirectoryInfo(_fullPath);
                    foreach (var file in di.EnumerateFiles($"g{FileNameFieldSeparator}*.json"))
                    {
                        file.Delete();
                    }
                    foreach (var file in di.EnumerateFiles($"checkpoint{FileNameFieldSeparator}{new string('?', 8)}.json"))
                    {
                        file.Delete();
                    }
                }
                catch { }
            }
        }

        public async Task StoreGrain<TGrain>(TGrain grain, bool updateCheckpoint = true, CancellationToken cancellationToken = default)
            where TGrain: class, IGrain
        {
            if (!PreStoreChecks(cancellationToken))
            {
                return;
            }
            Console.Write($"Storing grain {grain.Id:D}{Environment.NewLine}");
            if (updateCheckpoint && null != _localState?.LatestCheckpoint)
            {
                _localState.LatestCheckpoint.Additions.Add(grain.Id);
                _localState.LatestCheckpoint.Deletions.Remove(grain.Id);
            }
            await File.WriteAllTextAsync(Path.Combine(_fullPath, GetGrainFileName(grain)), JsonSerializer.Serialize(grain, JsonDefaults.SerializationOptions), cancellationToken);
        }

        public async Task<TGrain?> LoadGrainById<TGrain>(Guid id, CancellationToken cancellationToken = default)
            where TGrain : IGrain
        {
            var fname = Path.Combine(_fullPath, GetGrainFileName(id));
            if (File.Exists(fname))
            {
                using (var stream = File.OpenRead(fname))
                {
                    return await JsonSerializer.DeserializeAsync<TGrain>(stream, JsonDefaults.DeserializationOptions, cancellationToken);
                }
            }
            return default;
        }

        public bool ContainsGrain(IGrain grain)
        {
            return File.Exists(Path.Combine(_fullPath, GetGrainFileName(grain)));
        }

        public bool ContainsGrain(Guid id)
        {
            return File.Exists(Path.Combine(_fullPath, GetGrainFileName(id)));
        }

        public async IAsyncEnumerable<TGrain?> ListGrains<TGrain>([EnumeratorCancellation] CancellationToken cancellationToken = default)
            where TGrain: IGrain
        {
            foreach (var fname in Directory.EnumerateFiles(_fullPath, $"g{FileNameFieldSeparator}*.json"))
            {
                using (var stream = File.OpenRead(fname))
                {
                    yield return await JsonSerializer.DeserializeAsync<TGrain>(stream, JsonDefaults.DeserializationOptions, cancellationToken);
                }
            }
        }

        private static string GetGrainFileName(IGrain grain) => GetGrainFileName(grain.Id);
        private static string GetGrainFileName(Guid id) => $"g{FileNameFieldSeparator}{id:D}.json";

        private static string GetCheckpointFileName(int checkpointNum) => $"checkpoint{FileNameFieldSeparator}{checkpointNum.ToString("D8", CultureInfo.InvariantCulture)}.json";

        private bool PreStoreChecks(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            if (!IsDirectory)
            {
                throw new ApplicationException("Snapshot directory hasn't been initialized");
            }
            return true;
        }

        private class LocalStateModel
        {
            [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "We want Comment property serialized")]
            public string Comment => "!!!NEVER COMMMIT THIS FILE!!!";
            public ConnectionSettings? Connection { get; set; }
            public Snapshot Snapshot { get; set; } = new();
            public SnapshotCheckpoint LatestCheckpoint { get; set; } = new();
        }
    }
}
