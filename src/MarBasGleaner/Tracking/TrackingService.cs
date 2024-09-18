using MarBasGleaner.BrokerAPI;
using MarBasGleaner.BrokerAPI.Auth;

namespace MarBasGleaner.Tracking
{
    internal sealed class TrackingService(IHttpClientFactory httpClientFactory, IServiceProvider serviceProvider) : ITrackingService
    {
        private readonly IDictionary<string, SnapshotDirectory> _snapshots = new Dictionary<string, SnapshotDirectory>();
        private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
        private readonly IServiceProvider _services = serviceProvider;

        public IBrokerClient GetBrokerClient(ConnectionSettings settings, bool storeCredentials = true)
        {
            var result = _httpClientFactory.CreateClient();
            result.BaseAddress = settings.BrokerUrl;
            var authenticator = AuthenticatorFactory.CreateAuthenticator(settings);
            authenticator?.Authenticate(result, settings, storeCredentials);
            return new BrokerClient(result, _services.GetRequiredService<ILogger<BrokerClient>>());
        }

        public SnapshotDirectory GetSnapshotDirectory(string path = SnapshotDirectory.DefaultPath)
        {
            return GetSnapshotDirectoryAsync(path).Result;
        }

        public async Task<SnapshotDirectory> GetSnapshotDirectoryAsync(string path = SnapshotDirectory.DefaultPath, CancellationToken cancellationToken = default)
        {
            if (!_snapshots.TryGetValue(path, out var snapshot))
            {
                snapshot = new SnapshotDirectory(path);
                if (snapshot.IsDirectory)
                {
                    await snapshot.LoadMetadata(cancellationToken);
                }
                _snapshots[path] = snapshot;
            }
            return snapshot!;
        }
    }
}
