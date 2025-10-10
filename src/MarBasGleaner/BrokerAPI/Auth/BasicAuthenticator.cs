
using CraftedSolutions.MarBasGleaner.UI;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace CraftedSolutions.MarBasGleaner.BrokerAPI.Auth
{
    internal class BasicAuthenticator(IServiceProvider serviceProvider) : IAuthenticator
    {
        public const string SchemeName = "Basic";

        private const string ParamAuth = "token";

        private readonly IFeedbackService _feedbackService = serviceProvider.GetRequiredService<IFeedbackService>();

        public bool Authenticate(HttpClient client, ConnectionSettings? settings = null)
        {
            var auth = null != settings && settings.AuthenticatorParams.TryGetValue(ParamAuth, out string? value) ? value : string.Empty;
            var authIsStored = !string.IsNullOrEmpty(auth);
            var storeCredentials = 0 < settings?.AuthenticatorParams.Count;
            if (!authIsStored)
            {
                var user = _feedbackService.GetText(BasicAuthenticatorL10n.PromptUserName);
                if (!string.IsNullOrEmpty(user))
                {
                    using var pwd = _feedbackService.GetSecureText(BasicAuthenticatorL10n.PromptPassword);
                    if (0 < pwd.Length)
                    {
                        var cred = new NetworkCredential(user, pwd);
                        auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{cred.UserName}:{cred.Password}"));
                    }
                }
            }
            if (string.IsNullOrEmpty(auth))
            {
                return false;
            }
            if (null != settings && storeCredentials && !authIsStored)
            {
                settings.AuthenticatorParams.Remove(IAuthenticator.ParamStoreCredentials);
                settings.AuthenticatorParams.Add(ParamAuth, auth);
            }
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(SchemeName, auth);
            return true;
        }

        public Task<bool> AuthenticateAsync(HttpClient client, ConnectionSettings? settings = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Authenticate(client, settings));
        }

        public bool Logout(ConnectionSettings settings)
        {
            return LogoutAsync(settings).Result;
        }

        public Task<bool> LogoutAsync(ConnectionSettings settings, CancellationToken cancellationToken = default)
        {
            settings.AuthenticatorParams.Remove(ParamAuth);
            return Task.FromResult(true);
        }

        public void Dispose()
        {
            // No-op
        }
    }
}
