using CraftedSolutions.MarBasAPICore.Auth;
using CraftedSolutions.MarBasGleaner.UI;
using Duende.IdentityModel.Client;
using Duende.IdentityModel.OidcClient;
using Duende.IdentityModel.OidcClient.Results;
using System.Text;

namespace CraftedSolutions.MarBasGleaner.BrokerAPI.Auth
{
    internal enum TokenStatus { Unknown, Valid, RefreshRequired, RefreshTokenMissing, ExpirationMissing, ExpirationInvalid }

    internal class OIDCAuthenticator : IAuthenticator
    {
        public const string ParamAccesToken = "accessToken";
        public const string ParamRefreshToken = "refreshToken";
        public const string ParamTokenExpiration = "expiration";

        private readonly ILogger<OIDCAuthenticator> _logger;
        private readonly IConfiguration _configuration;
        private readonly IFeedbackService _feedbackService;
        private readonly HttpAuthenticationHandler _authenticationHandler;
        private readonly HttpClient _httpClient;

        private bool _disposed = false;

        public OIDCAuthenticator(IServiceProvider serviceProvider)
        {
            _logger = serviceProvider.GetRequiredService<ILogger<OIDCAuthenticator>>();
            _configuration = serviceProvider.GetRequiredService<IConfiguration>();
            _feedbackService = serviceProvider.GetRequiredService<IFeedbackService>();
            _authenticationHandler = new HttpAuthenticationHandler(_logger, _configuration.GetValue("Auth:OAuthListenerPort", 5500), _configuration.GetValue<string>("Auth:OAuthListenerPath"));

            _httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient();
            _httpClient.DefaultRequestHeaders.Add("Origin", $"http://localhost:{_authenticationHandler.Port}");
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            if (disposing)
            {
                _httpClient.Dispose();
            }
            _disposed = true;
        }

        public bool Authenticate(HttpClient client, ConnectionSettings? settings = null, bool storeCredentials = true)
        {
            return AuthenticateAsync(client, settings, storeCredentials).Result;
        }

        public async Task<bool> AuthenticateAsync(HttpClient client, ConnectionSettings? settings = null, bool storeCredentials = true, CancellationToken cancellationToken = default)
        {
            if (settings?.BrokerAuthConfig is IOIDCAuthConfig oidcConfig)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var token = settings.AuthenticatorParams.TryGetValue(ParamAccesToken, out string? value) ? value : string.Empty;
                var tokenStatus = CheckTokenStatus(settings, token);

                if (TokenStatus.RefreshRequired == tokenStatus)
                {
                    var result = await RefreshToken(settings.AuthenticatorParams[ParamRefreshToken], oidcConfig, cancellationToken);
                    if (result.IsError)
                    {
                        tokenStatus = TokenStatus.Unknown;
                    }
                    else
                    {
                        _feedbackService.DisplayMessage($"Refreshed authentication by {oidcConfig.Authority}");
                        token = result.AccessToken;
                        if (!string.IsNullOrEmpty(token))
                        {
                            tokenStatus = TokenStatus.Valid;
                        }
                        if (storeCredentials)
                        {
                            StoreCredentials(settings, result);
                        }
                    }
                }

                if (TokenStatus.Valid != tokenStatus)
                {
                    var result = await Authorize(oidcConfig, cancellationToken);
                    if (!result.IsError)
                    {
                        _feedbackService.DisplayMessage($"Authenticated by {oidcConfig.Authority} as {result.User.Identity?.Name}");
                        token = result.AccessToken;
                        if (storeCredentials)
                        {
                            StoreCredentials(settings, result);
                        }
                    }
                }
                if (!string.IsNullOrEmpty(token))
                {
                    client.SetBearerToken(token);
                    return true;
                }
            }
            return false;
        }

        private TokenStatus CheckTokenStatus(ConnectionSettings settings, string? token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return TokenStatus.Unknown;
            }
            if (!settings.AuthenticatorParams.TryGetValue(ParamRefreshToken, out var refreshToken) || string.IsNullOrEmpty(refreshToken))
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning("No refresh token was stored");
                }
                return TokenStatus.RefreshTokenMissing;
            }
            if (!settings.AuthenticatorParams.TryGetValue(ParamTokenExpiration, out var expiration) || string.IsNullOrEmpty(expiration))
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning("No token expiration time was stored");
                }
                return TokenStatus.ExpirationMissing;
            }
            if (!DateTimeOffset.TryParse(expiration, null, System.Globalization.DateTimeStyles.RoundtripKind, out var expirationOffset))
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning("Invalid token expiration time {expiration}", expiration);
                }
                return TokenStatus.ExpirationInvalid;
            }
            var diff = expirationOffset - DateTimeOffset.UtcNow;
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Token expires at {expriation}, remaining {diff} sec", expiration, diff.TotalSeconds);
            }
            return 10 > diff.TotalSeconds ? TokenStatus.RefreshRequired : TokenStatus.Valid;
        }

        private async Task<LoginResult> Authorize(IOIDCAuthConfig config, CancellationToken cancellationToken = default)
        {
            _feedbackService.DisplayInfo("Acquiring authorization (new browser window will open)...");

            var oidcClient = new OidcClient(GetOidcClientOptions(config));
            var result = await oidcClient.LoginAsync(new LoginRequest() { BrowserTimeout = _configuration.GetValue("Auth:OAuthListenerTimeout", 3 * 60) }, cancellationToken);
            LogAuthResult(result);
            return result;
        }

        private async Task<RefreshTokenResult> RefreshToken(string refreshToken, IOIDCAuthConfig config, CancellationToken cancellationToken = default)
        {
            var oidcClient = new OidcClient(GetOidcClientOptions(config));
            var result = await oidcClient.RefreshTokenAsync(refreshToken, cancellationToken: cancellationToken);
            LogAuthResult(result);
            return result;
        }

        private OidcClientOptions GetOidcClientOptions(IOIDCAuthConfig config)
        {
            var result = new OidcClientOptions()
            {
                Authority = config.Authority,
                ClientId = config.ClientId,
                Scope = string.Join(string.IsNullOrEmpty(config.ScopeSeparator) ? " " : config.ScopeSeparator,
                    config.Scopes.Where(x => x.Value).Select(x => x.Key)),
                FilterClaims = false,
                LoadProfile = false,
                RefreshDiscoveryDocumentForLogin = false,
                RefreshDiscoveryOnSignatureFailure = false,
                ProviderInformation = new ProviderInformation()
                {
                    IssuerName = config.Authority,
                    AuthorizeEndpoint = config.AuthorizationUrl,
                    TokenEndpoint = config.TokenUrl
                },
                Browser = _authenticationHandler,
                RedirectUri = _authenticationHandler.RedirectURL,
                HttpClientFactory = (options) =>
                {
                    return _httpClient;
                }
            };
            result.Policy.Discovery.RequireKeySet = false;
            if (config.AuthorizationUrl.StartsWith("http:") || config.TokenUrl.StartsWith("http:"))
            {
                result.Policy.Discovery.RequireHttps = false;
            }
            if (!string.IsNullOrEmpty(config.LogoutUrl))
            {
                result.ProviderInformation.EndSessionEndpoint = config.LogoutUrl;
            }
            if (!string.IsNullOrEmpty(config.UserInfoUrl))
            {
                result.ProviderInformation.UserInfoEndpoint = config.UserInfoUrl;
            }
            return result;
        }

        private static void StoreCredentials(ConnectionSettings settings, dynamic authResult)
        {
            settings.AuthenticatorParams[ParamAccesToken] = authResult.AccessToken;
            settings.AuthenticatorParams[ParamRefreshToken] = authResult.RefreshToken;
            settings.AuthenticatorParams[ParamTokenExpiration] = authResult.AccessTokenExpiration.ToString("o");
        }

        private void LogAuthResult(dynamic result)
        {
            if (result.IsError && _logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError("Login error: {error} ({errDesc})", (string)result.Error, (string)result.ErrorDescription);
                return;
            }
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                var msg = new StringBuilder("Claims:\n");
                if (result is LoginResult loginResult)
                {
                    foreach (var claim in loginResult.User.Claims)
                    {
                        msg.Append(claim.Type);
                        msg.Append(": ");
                        msg.Append(claim.Value);
                        msg.Append('\n');
                    }
                }
                if (result is LoginResult || result is RefreshTokenResult)
                {
                    msg.Append($"\nidentity token: {result.IdentityToken}");
                    msg.Append($"\naccess token:   {result.AccessToken}");
                    msg.Append($"\nrefresh token:  {result.RefreshToken ?? "none"}");

                }
                _logger.LogTrace("{msg}", msg.ToString());
            }
        }

    }

}