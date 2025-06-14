﻿using CraftedSolutions.MarBasAPICore.Auth;
using CraftedSolutions.MarBasGleaner.BrokerAPI;
using CraftedSolutions.MarBasGleaner.BrokerAPI.Auth;
using CraftedSolutions.MarBasGleaner.Json;
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

        public IBrokerClient GetBrokerClient(ConnectionSettings settings, bool storeCredentials = true)
        {
            return GetBrokerClientAsync(settings, storeCredentials).Result;
        }

        public async Task<IBrokerClient> GetBrokerClientAsync(ConnectionSettings settings, bool storeCredentials = true, CancellationToken cancellationToken = default)
        {
            if (settings.IgnoreSslErrors && !_environment.IsDevelopment())
            {
                throw new NotSupportedException("Lax SSL handling (IgnoreSslErrors) is only supproted in development environment");
            }
            if (null == settings.BrokerAuthConfig)
            {
                settings = await BootstrapConfig(settings, cancellationToken);
            }
            var result = _httpClientFactory.CreateClient($"broker{(settings.IgnoreSslErrors ? "-lax-ssl" : string.Empty)}-client");
            result.BaseAddress = settings.BrokerUrl;
            using var authenticator = _authenticatorFactory.CreateAuthenticator(settings);
            await (authenticator?.AuthenticateAsync(result, settings, storeCredentials, cancellationToken) ?? Task.CompletedTask);
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

        private async Task<ConnectionSettings> BootstrapConfig(ConnectionSettings settings, CancellationToken cancellationToken = default)
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

        private HttpClient GetBootstrapHttpClient(ConnectionSettings settings)
        {
            var result = _httpClientFactory.CreateClient($"bootstrap{(settings.IgnoreSslErrors ? "-lax-ssl" : string.Empty)}-client");
            result.BaseAddress = settings.BrokerUrl;
            return result;
        }
    }
}
