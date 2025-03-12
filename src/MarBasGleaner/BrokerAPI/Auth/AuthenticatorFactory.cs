using CraftedSolutions.MarBasGleaner.BrokerAPI;

namespace CraftedSolutions.MarBasGleaner.BrokerAPI.Auth
{
    internal static class AuthenticatorFactory
    {
        public static Type? ResolveAuthenticatorType(string schema)
        {
            var fqn = $"{nameof(MarBasGleaner)}.{nameof(BrokerAPI)}.{schema}Authenticator";
            return Type.GetType(fqn);
        }

        public static IAuthenticator? CreateAuthenticator(string schema)
        {
            var type = ResolveAuthenticatorType(schema);
            return (null == type ? null : Activator.CreateInstance(type)) as IAuthenticator;
        }

        public static IAuthenticator? CreateAuthenticator(ConnectionSettings settings)
        {
            var type = Type.GetType(settings.Authenticator);
            return (null == type ? null : Activator.CreateInstance(type)) as IAuthenticator;
        }

    }
}
