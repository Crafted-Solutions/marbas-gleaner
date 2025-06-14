namespace CraftedSolutions.MarBasGleaner.BrokerAPI.Auth
{
    public enum AuthenticationScheme { Auto, Basic, OIDC }

    internal interface IAuthenticator: IDisposable
    {
        bool Authenticate(HttpClient client, ConnectionSettings? settings = null, bool storeCredentials = true);
        Task<bool> AuthenticateAsync(HttpClient client, ConnectionSettings? settings = null, bool storeCredentials = true, CancellationToken cancellationToken = default);
    }
}
