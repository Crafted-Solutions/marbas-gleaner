using CraftedSolutions.MarBasGleaner.BrokerAPI;
using CraftedSolutions.MarBasGleaner.BrokerAPI.Auth;
using MarBasGleaner.Tracking;

namespace CraftedSolutions.MarBasGleaner.Tracking
{
    internal sealed class TrackingService(IHttpClientFactory httpClientFactory, IServiceProvider serviceProvider, IHostEnvironment environment) : ITrackingService
    {
        private readonly Dictionary<string, SnapshotDirectory> _snapshots = [];
        private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
        private readonly IServiceProvider _services = serviceProvider;
        private readonly IHostEnvironment _environment = environment;

        public IBrokerClient GetBrokerClient(ConnectionSettings settings, bool storeCredentials = true)
        {
            if (settings.IgnoreSslErrors && !_environment.IsDevelopment())
            {
                throw new NotSupportedException("Lax SSL handling (IgnoreSslErrors) is only supproted in development environment");
            }
            var result = settings.IgnoreSslErrors ? _httpClientFactory.CreateClient("lax-ssl-client") : _httpClientFactory.CreateClient();
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
                snapshot = new SnapshotDirectory(_services.GetRequiredService<ILogger<SnapshotDirectory>>(), path);
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
