using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using MarBasCommon;
using MarBasGleaner.BrokerAPI;
using MarBasGleaner.Json;
using MarBasSchema;
using MarBasSchema.Broker;
using MarBasSchema.Grain;

namespace MarBasGleaner.Tracking
{
    internal partial class SnapshotDirectory(ILogger<SnapshotDirectory> logger, string path = SnapshotDirectory.DefaultPath)
    {
        public const string DefaultPath = ".";
        public const string SnapshotFileName = ".mbgsnapshot.json";
        public const string LocalStateFileName = ".mbglocal.json";
        public const string IgnoresFileName = ".mbgignore.json";
        public const string CheckpointBaseName = ".mbgcheckpoint";
        public const string FileNameFieldSeparator = ",";

        private readonly string _fullPath = Path.GetFullPath(path);
        private readonly ILogger _logger = logger;

        private Snapshot? _snapshot;
        private LocalStateModel? _localState;
        private IGrainBasicFilter? _igonres;
        private SnapshotCheckpoint? _sharedCheckpoint;

        public Snapshot? Snapshot => _snapshot;

        public SnapshotCheckpoint? LocalCheckpoint => _localState?.ActiveCheckpoint;

        public SnapshotCheckpoint? SharedCheckpoint => _sharedCheckpoint ?? LocalCheckpoint;

        public int LastPushCheckpoint { get => _localState?.LastPushCheckpoint ?? 0; set => (_localState ?? new()).LastPushCheckpoint = value; }

        public Guid? BrokerInstanceId => _localState?.InstanceId;

        public ConnectionSettings? ConnectionSettings => _localState?.Connection;

        public IGrainBasicFilter? Ignores { get => _igonres; set => _igonres = value; }

        public string FullPath => _fullPath;

        public bool IsDirectory => Directory.Exists(_fullPath);

        [MemberNotNullWhen(true, nameof(Snapshot))]
        public bool IsSnapshot => File.Exists(Path.Combine(_fullPath, SnapshotFileName));

        [MemberNotNullWhen(true, new[] { nameof(LocalCheckpoint), nameof(BrokerInstanceId), nameof(ConnectionSettings) })]
        public bool IsConnected => File.Exists(Path.Combine(_fullPath, LocalStateFileName));

        [MemberNotNullWhen(true, nameof(Ignores))]
        public bool HasIgnores => File.Exists(Path.Combine(_fullPath, IgnoresFileName));

        public bool IsReady => IsDirectory && (IsConnected == (null != _localState)) && (IsSnapshot == (null != _snapshot)) && (HasIgnores == (null != Ignores));

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

        public async Task Initialize(Guid instanceId, Snapshot snapshot, SourceControlFlavor scs = SourceControlFlavor.Git, ConnectionSettings? connection = null, CancellationToken cancellationToken = default)
        {
            if (IsSnapshot)
            {
                throw new ApplicationException($"{_fullPath} is already initialized");
            }
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Initializing napshot {id} path={path} schema={version} url={url}", instanceId, _fullPath, snapshot.SchemaVersion, connection?.BrokerUrl);
            }

            _snapshot = snapshot;
            if (null == _localState)
            {
                _localState = new LocalStateModel()
                {
                    Connection = connection,
                    InstanceId = instanceId,
                    LastPushCheckpoint = snapshot.Checkpoint,
                    ActiveCheckpoint = new SnapshotCheckpoint()
                    {
                        InstanceId = instanceId,
                        Ordinal = snapshot.Checkpoint,
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
                await File.WriteAllTextAsync(Path.Combine(_fullPath, ".gitignore"), LocalStateFileName, cancellationToken);
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
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Connecting snapshot {id} path={path} schema={version} url={url}", instanceId, _fullPath, _snapshot.SchemaVersion, connection?.BrokerUrl);
            }

            if (null != timestamp)
            {
                _snapshot.Updated = (DateTime)timestamp;
            }
            if (null == _localState)
            {
                _localState = new LocalStateModel()
                {
                    InstanceId = instanceId
                };
            }
            _localState.Connection = connection;
            if (0 < _snapshot.Checkpoint && !HasCheckpoint(_snapshot.Checkpoint))
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("Checkpoint {checkpoint} not found, creating it", _snapshot.Checkpoint);
                }
                _sharedCheckpoint = new SnapshotCheckpoint()
                {
                    InstanceId = instanceId,
                    Ordinal = _snapshot.Checkpoint
                };
                await foreach (var grain in ListGrains<GrainPlain>(cancellationToken: cancellationToken))
                {
                    if (null == grain)
                        continue;

                    _sharedCheckpoint.Modifications.Add(grain.Id);
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
                if (-1 == adoptCheckpoint)
                {
                    _localState.ActiveCheckpoint = _sharedCheckpoint!;
                }
                else
                {
                    var cp = await LoadCheckpoint(adoptCheckpoint, cancellationToken);
                    if (null == cp)
                    {
                        throw new ApplicationException($"Checkpoint {adoptCheckpoint} not found");
                    }
                    _localState.ActiveCheckpoint = cp;
                }
            }

            await StoreLocalState(0 != adoptCheckpoint, cancellationToken);
        }

        public void Disconnect()
        {
            if (IsConnected)
            {
                File.Delete(Path.Combine(_fullPath, LocalStateFileName));
            }
            _localState = null;
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
                await File.WriteAllTextAsync(Path.Combine(_fullPath, SnapshotFileName), JsonSerializer.Serialize(_snapshot, JsonDefaults.SerializationOptions), cancellationToken);
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
                await File.WriteAllTextAsync(Path.Combine(_fullPath, LocalStateFileName), JsonSerializer.Serialize(_localState, JsonDefaults.SerializationOptions), cancellationToken);
            }
            if (includeCheckpoint && null != _localState?.ActiveCheckpoint)
            {
                await StoreCheckpoint(_localState.ActiveCheckpoint, true, cancellationToken);
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
                await File.WriteAllTextAsync(Path.Combine(_fullPath, IgnoresFileName), JsonSerializer.Serialize(_igonres, JsonDefaults.SerializationOptions), cancellationToken);
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
                _localState = JsonSerializer.Deserialize<LocalStateModel>(await File.ReadAllTextAsync(Path.Combine(_fullPath, LocalStateFileName), cancellationToken), JsonDefaults.SerializationOptions);
            }
            if (IsSnapshot && !cancellationToken.IsCancellationRequested)
            {
                _snapshot = JsonSerializer.Deserialize<Snapshot>(await File.ReadAllTextAsync(Path.Combine(_fullPath, SnapshotFileName), cancellationToken), JsonDefaults.SerializationOptions);
                if (null != _snapshot && HasCheckpoint(_snapshot.Checkpoint))
                {
                    _sharedCheckpoint = await LoadCheckpoint(_snapshot.Checkpoint, cancellationToken);
                }
            }
            if (HasIgnores && !cancellationToken.IsCancellationRequested)
            {
                _igonres = JsonSerializer.Deserialize<GrainBasicFilter>(await File.ReadAllTextAsync(Path.Combine(_fullPath, IgnoresFileName), cancellationToken), JsonDefaults.SerializationOptions);
            }
        }

        public bool HasCheckpoint(int checkpointNum)
        {
            return File.Exists(Path.Combine(_fullPath, GetCheckpointFileName(checkpointNum)));
        }

        public async Task AdoptCheckpoint(int checkpointNum = -1, CancellationToken cancellationToken = default)
        {
            if (null == _localState)
            {
                throw new ApplicationException("Snapshot is not connected to broker");
            }
            if (-1 == checkpointNum)
            {
                _localState.ActiveCheckpoint = _sharedCheckpoint!;
            }
            else
            {
                var cp = await LoadCheckpoint(checkpointNum, cancellationToken);
                if (null == cp)
                {
                    throw new ApplicationException($"Checkpoint {checkpointNum} not found");
                }
                _localState.ActiveCheckpoint = cp;
            }
        }

        public async Task<SnapshotCheckpoint?> LoadCheckpoint(int checkpointNum = -1, CancellationToken cancellationToken = default)
        {
            if (!IsDirectory || cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            if (1 > checkpointNum)
            {
                checkpointNum = _localState?.ActiveCheckpoint.Ordinal ?? _snapshot?.Checkpoint ?? checkpointNum;
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

        public async Task<SnapshotCheckpoint> LoadConflatedCheckpoint(int startingWith = -1, CancellationToken cancellationToken = default)
        {
            var result = _localState?.ActiveCheckpoint.Clone(true) ?? new();
            if (!IsDirectory || cancellationToken.IsCancellationRequested || null == SharedCheckpoint || result.IsSame(SharedCheckpoint))
            {
                return result;
            }
            if (1 > startingWith)
            {
                startingWith = Math.Max(result.Ordinal, 1);
            }
            for (var i = startingWith; i <= SharedCheckpoint.Ordinal; i++)
            {
                var cp = await LoadCheckpoint(i, cancellationToken);
                if (null == cp || result.IsSame(cp))
                {
                    continue;
                }
                result.Modifications.UnionWith(cp.Modifications);
                result.Modifications.ExceptWith(cp.Deletions);
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
            foreach (var fname in Directory.EnumerateFiles(_fullPath, $"{CheckpointBaseName}{FileNameFieldSeparator}{new string('?', 8)}.json"))
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
                _localState.ActiveCheckpoint = checkpoint;
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
                        File.Delete(Path.Combine(_fullPath, LocalStateFileName));
                    }
                    if (IsSnapshot)
                    {
                        File.Delete(Path.Combine(_fullPath, SnapshotFileName));
                    }
                    if (HasIgnores)
                    {
                        File.Delete(Path.Combine(_fullPath, IgnoresFileName));
                    }
                    var di = new DirectoryInfo(_fullPath);
                    foreach (var file in di.EnumerateFiles($"g{FileNameFieldSeparator}*.json*"))
                    {
                        file.Delete();
                    }
                    foreach (var file in di.EnumerateFiles($"{CheckpointBaseName}{FileNameFieldSeparator}{new string('?', 8)}.json"))
                    {
                        file.Delete();
                    }
                }
                catch { }
            }
        }

        public async Task<string> StoreGrain<TGrain>(TGrain grain, bool updateCheckpoint = true, bool temp = false, CancellationToken cancellationToken = default)
            where TGrain: class, IGrain
        {
            if (!PreStoreChecks(cancellationToken))
            {
                return string.Empty;
            }
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Storing grain {id}", grain.Id);
            }
            if (updateCheckpoint && null != _localState?.ActiveCheckpoint)
            {
                _localState.ActiveCheckpoint.Modifications.Add(grain.Id);
                _localState.ActiveCheckpoint.Deletions.Remove(grain.Id);
            }
            var result = Path.Combine(_fullPath, GetGrainFileName(grain, temp));
            await File.WriteAllTextAsync(result, JsonSerializer.Serialize(grain, JsonDefaults.SerializationOptions), cancellationToken);
            return result;
        }

        public async Task<TGrain?> LoadGrainById<TGrain>(Guid id, bool temp = false, CancellationToken cancellationToken = default)
            where TGrain : IGrain
        {
            var fname = Path.Combine(_fullPath, GetGrainFileName(id, temp));
            if (File.Exists(fname))
            {
                using (var stream = File.OpenRead(fname))
                {
                    return await JsonSerializer.DeserializeAsync<TGrain>(stream, JsonDefaults.DeserializationOptions, cancellationToken);
                }
            }
            return default;
        }

        public void DeleteGrains(IEnumerable<IIdentifiable> grains)
        {
            Parallel.ForEach(grains, (grain, token) =>
            {
                var fname = Path.Combine(_fullPath, GetGrainFileName(grain));
                if (File.Exists(fname))
                {
                    if (_logger.IsEnabled(LogLevel.Information))
                    {
                        _logger.LogInformation("Deleting grain {id}", grain.Id);
                    }
                    File.Delete(fname);
                }
            });
        }

        public bool ContainsGrain(IIdentifiable grain)
        {
            return File.Exists(Path.Combine(_fullPath, GetGrainFileName(grain)));
        }

        public async IAsyncEnumerable<TGrain?> ListGrains<TGrain>(Func<Guid, bool>? prefilter = null,  [EnumeratorCancellation] CancellationToken cancellationToken = default)
            where TGrain: IGrain
        {
            foreach (var fname in Directory.EnumerateFiles(_fullPath, $"g{FileNameFieldSeparator}*.json"))
            {
                if (false == prefilter?.Invoke(Guid.Parse(GrainFileNameRegEx().Match(fname).Groups[1].Value)))
                {
                    continue;
                }
                using (var stream = File.OpenRead(fname))
                {
                    yield return await JsonSerializer.DeserializeAsync<TGrain>(stream, JsonDefaults.DeserializationOptions, cancellationToken);
                }
            }
        }


        private static string GetGrainFileName(IIdentifiable grain, bool temp = false) => GetGrainFileName(grain.Id, temp);
        private static string GetGrainFileName(Guid id, bool temp = false) => $"g{FileNameFieldSeparator}{id:D}.json{(temp ? ".tmp" : string.Empty)}";

        private static string GetCheckpointFileName(int checkpointNum) => $"{CheckpointBaseName}{FileNameFieldSeparator}{checkpointNum.ToString("D8", CultureInfo.InvariantCulture)}.json";

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

        [GeneratedRegex(@$"g{FileNameFieldSeparator}([^.]+)\.json", RegexOptions.Compiled)]
        private static partial Regex GrainFileNameRegEx();

        private class LocalStateModel
        {
            [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "We want Comment property serialized")]
            public string Comment => "!!!NEVER COMMMIT THIS FILE!!!";
            public ConnectionSettings? Connection { get; set; }
            public Guid InstanceId { get; set; }
            public int LastPushCheckpoint { get; set; }
            public SnapshotCheckpoint ActiveCheckpoint { get; set; } = new();
        }
    }
}
