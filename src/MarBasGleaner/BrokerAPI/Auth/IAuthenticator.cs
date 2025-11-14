namespace CraftedSolutions.MarBasGleaner.BrokerAPI.Auth
{
    public enum AuthenticationScheme { Auto, Basic, OIDC }

    internal interface IAuthenticator: IDisposable
    {
        const string ParamStoreCredentials = "_storeCredentials";

        bool Authenticate(HttpClient client, ConnectionSettings? settings = null);
        Task<bool> AuthenticateAsync(HttpClient client, ConnectionSettings? settings = null, CancellationToken cancellationToken = default);
        bool Logout(ConnectionSettings settings);
        Task<bool> LogoutAsync(ConnectionSettings settings, CancellationToken cancellationToken = default);
    }
}
