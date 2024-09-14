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

        public Snapshot? SharedSnapshot => _snapshot;

        public Snapshot? LocalSnapshot => _localState?.Snapshot;

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
                    Connection = connection
                };
            }
            await StoreMetadata(cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (SourceControlFlavor.Git == scs)
            {
                await File.WriteAllTextAsync(Path.Combine(_fullPath, ".gitignore"), LocalStateFile, cancellationToken);
            }
        }

        public async Task Connect(ConnectionSettings connection, Guid instanceId, DateTime? timestamp = null, CancellationToken cancellationToken = default)
        {
            if (IsConnected)
            {
                throw new ApplicationException($"{_fullPath} is already connected");
            }
            if (null == _localState)
            {
                _localState = new LocalStateModel()
                {
                    Snapshot = new Snapshot()
                    {
                        SchemaVersion = _snapshot!.SchemaVersion,
                        Anchor = _snapshot.Anchor,
                        Scope = _snapshot.Scope,
                        InstanceId = instanceId,
                        Latest = timestamp ?? _snapshot.Latest
                    }
                };
                foreach(var uid in _snapshot.AliveGrains)
                {
                    _localState.Snapshot.AliveGrains.Add(uid);
                }
                foreach (var uid in _snapshot.DeadGrains)
                {
                    _localState.Snapshot.DeadGrains.Add(uid);
                }
            }
            _localState.Connection = connection;

            await StoreLocalState(cancellationToken);
        }

        public async Task StoreMetadata(CancellationToken cancellationToken = default)
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
            await StoreLocalState(cancellationToken);
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

        public async Task StoreLocalState(CancellationToken cancellationToken = default)
        {
            if (!PreStoreChecks(cancellationToken))
            {
                return;
            }
            if (null != _localState && null != _localState.Connection)
            {
                await File.WriteAllTextAsync(Path.Combine(_fullPath, LocalStateFile), JsonSerializer.Serialize(_localState, JsonDefaults.SerializationOptions), cancellationToken);
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
            if (IsSnapshot)
            {
                _snapshot = JsonSerializer.Deserialize<Snapshot>(await File.ReadAllTextAsync(Path.Combine(_fullPath, SnapshotFile), cancellationToken), JsonDefaults.SerializationOptions);
            }
            if (IsConnected && !cancellationToken.IsCancellationRequested)
            {
                _localState = JsonSerializer.Deserialize<LocalStateModel>(await File.ReadAllTextAsync(Path.Combine(_fullPath, LocalStateFile), cancellationToken), JsonDefaults.SerializationOptions);
            }
            if (HasIgnores && !cancellationToken.IsCancellationRequested)
            {
                _igonres = JsonSerializer.Deserialize<GrainBasicFilter>(await File.ReadAllTextAsync(Path.Combine(_fullPath, IgnoresFile), cancellationToken), JsonDefaults.SerializationOptions);
            }
        }

        public void CleanUp()
        {
            if (Directory.Exists(_fullPath))
            {
                try
                {
                    var di = new DirectoryInfo(_fullPath);
                    foreach (var file in di.EnumerateFiles())
                    {
                        file.Delete();
                    }
                    foreach (var dir in di.EnumerateDirectories())
                    {
                        dir.Delete(true);
                    }
                }
                catch { }
            }
        }

        public async Task StoreGrain<TGrain>(TGrain grain, bool updateIndex = true, CancellationToken cancellationToken = default)
            where TGrain: class, IGrain
        {
            if (!PreStoreChecks(cancellationToken))
            {
                return;
            }
            Console.WriteLine($"Storing grain {grain.Id:D}");
            if (updateIndex)
            {
                _localState?.Snapshot.AliveGrains.Add(grain.Id);
                _localState?.Snapshot.DeadGrains.Remove(grain.Id);
                _snapshot?.AliveGrains.Add(grain.Id);
                _snapshot?.DeadGrains.Remove(grain.Id);
            }
            await File.WriteAllTextAsync(Path.Combine(_fullPath, GetGrainFileName(grain)), JsonSerializer.Serialize(grain, JsonDefaults.SerializationOptions), cancellationToken);
        }

        public async Task<TGrain?> LoadGrainById<TGrain>(Guid id, CancellationToken cancellationToken = default)
            where TGrain : IGrain
        {
            var fname = Directory.EnumerateFiles(_fullPath, $"g{FileNameFieldSeparator}*{FileNameFieldSeparator}{id:D}.json").FirstOrDefault();
            if (null != fname)
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
            return !string.IsNullOrEmpty(Directory.EnumerateFiles(_fullPath, $"g{FileNameFieldSeparator}*{FileNameFieldSeparator}{id:D}.json").FirstOrDefault());
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

        private static string GetGrainFileName(IGrain grain) => $"g{FileNameFieldSeparator}{grain.ParentId?.ToString("D") ?? "-"}{FileNameFieldSeparator}{grain.Id:D}.json";

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
            public Snapshot Snapshot { get; set; } = new ();
        }
    }
}
