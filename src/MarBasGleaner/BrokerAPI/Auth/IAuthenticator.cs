namespace MarBasGleaner.BrokerAPI.Auth
{
    internal interface IAuthenticator
    {
        bool Authenticate(HttpClient client, ConnectionSettings? settings = null, bool storeCredentials = true);
    }
}
