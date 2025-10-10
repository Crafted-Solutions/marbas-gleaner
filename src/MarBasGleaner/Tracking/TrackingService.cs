using CraftedSolutions.MarBasAPICore.Auth;
using CraftedSolutions.MarBasGleaner.BrokerAPI;
using CraftedSolutions.MarBasGleaner.BrokerAPI.Auth;
using CraftedSolutions.MarBasGleaner.Json;
using CraftedSolutions.MarBasSchema.Sys;
using System.Net.Http.Json;

namespace CraftedSolutions.MarBasGleaner.Tracking
{
    internal sealed class TrackingService(IHttpClientFactory httpClientFactory, AuthenticatorFactory authenticatorFactory,
        IServiceProvider serviceProvider, IHostEnvironment environment, ILogger<TrackingService> logger)
        : ITrackingService
    {
        private readonly Dictionary<string, SnapshotDirectory> _snapshots = [];
        private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
        private readonly AuthenticatorFactory _authenticatorFactory = authenticatorFactory;
        private readonly IServiceProvider _services = serviceProvider;
        private readonly IHostEnvironment _environment = environment;
        private readonly ILogger<TrackingService> _logger = logger;

        public IBrokerClient GetBrokerClient(ConnectionSettings settings)
        {
            return GetBrokerClientAsync(settings).Result;
        }

        public async Task<IBrokerClient> GetBrokerClientAsync(ConnectionSettings settings, CancellationToken cancellationToken = default)
        {
            if (settings.IgnoreSslErrors && !_environment.IsDevelopment())
            {
                throw new NotSupportedException("Lax SSL handling (IgnoreSslErrors) is only supproted in development environment");
            }
            if (null == settings.BrokerAuthConfig)
            {
                settings = await GetBootstrapConfig(settings, cancellationToken);
            }
            var mainClient = GetBrokerHttpClient(settings);
            using var authenticator = _authenticatorFactory.CreateAuthenticator(settings);
            await (authenticator?.AuthenticateAsync(mainClient, settings, cancellationToken) ?? Task.CompletedTask);
            return new BrokerClient(mainClient, _services.GetRequiredService<ILogger<BrokerClient>>());
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

        public async Task<IServerInfo?> GetBrokerInfoAsync(ConnectionSettings settings, CancellationToken cancellationToken = default)
        {
            using (var client = GetBootstrapHttpClient(settings))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }
                using var resp = await client.GetAsync($"{BrokerClient.ApiPrefix}SysInfo", cancellationToken);
                if (!BrokerClient.HandleHttpError(resp, _logger))
                {
                    return await resp.Content.ReadFromJsonAsync<ServerInfo>(JsonDefaults.DeserializationOptions, cancellationToken);
                }
                return null;
            }
        }

        public IServerInfo? GetBrokerInfo(ConnectionSettings settings)
        {
            return GetBrokerInfoAsync(settings).Result;
        }

        public async Task<bool> LogoutFromBrokerAsync(ConnectionSettings settings, CancellationToken cancellationToken = default)
        {
            if (null == settings.BrokerAuthConfig)
            {
                settings = await GetBootstrapConfig(settings, cancellationToken);
            }
            using var authenticator = _authenticatorFactory.CreateAuthenticator(settings);
            return await (authenticator?.LogoutAsync(settings, cancellationToken) ?? Task.FromResult(false));
        }

        public bool LogoutFromBroker(ConnectionSettings settings)
        {
            return LogoutFromBrokerAsync(settings).Result;
        }

        private async Task<ConnectionSettings> GetBootstrapConfig(ConnectionSettings settings, CancellationToken cancellationToken = default)
        {
            try
            {
                using (var client = GetBootstrapHttpClient(settings))
                {
                    using var resp = await client.GetAsync($"{BrokerClient.ApiPrefix}SysInfo/AuthConfig", cancellationToken);
                    if (!BrokerClient.HandleHttpError(resp, _logger))
                    {
                        settings.BrokerAuthConfig = await resp.Content.ReadFromJsonAsync<IAuthConfig>(JsonDefaults.DeserializationOptions, cancellationToken);
                    }
                }
            }
            catch (Exception e)
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning(e, "Failed to retrieve AuthConfig from {brokerUrl}", settings.BrokerUrl);
                }
            }
            if (null == settings.BrokerAuthConfig)
            {
                settings.BrokerAuthConfig = new BasicAuthConfig();
            }
            return settings;
        }

        private HttpClient GetBrokerHttpClient(ConnectionSettings settings)
        {
            var result = _httpClientFactory.CreateClient($"broker{(settings.IgnoreSslErrors ? "-lax-ssl" : string.Empty)}-client");
            result.BaseAddress = settings.BrokerUrl;
            return result;
        }

        private HttpClient GetBootstrapHttpClient(ConnectionSettings settings)
        {
            var result = _httpClientFactory.CreateClient($"bootstrap{(settings.IgnoreSslErrors ? "-lax-ssl" : string.Empty)}-client");
            result.BaseAddress = settings.BrokerUrl;
            return result;
        }
    }
}
