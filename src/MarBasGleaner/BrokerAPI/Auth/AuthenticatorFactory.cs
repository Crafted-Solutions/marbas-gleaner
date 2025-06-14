namespace CraftedSolutions.MarBasGleaner.BrokerAPI.Auth
{
    internal class AuthenticatorFactory(IServiceProvider serviceProvider)
    {
        private readonly IServiceProvider _serviceProvider = serviceProvider;

        public IAuthenticator? CreateAuthenticator(string schema)
        {
            return CreateAuthenticator(schema, _serviceProvider);
        }

        public IAuthenticator? CreateAuthenticator(ConnectionSettings settings)
        {
            return CreateAuthenticator(settings, _serviceProvider);
        }

        public static Type? ResolveAuthenticatorType(string schema)
        {
            var fqn = $"{typeof(AuthenticatorFactory).Namespace}.{schema}Authenticator";
            return Type.GetType(fqn);
        }

        public static IAuthenticator? CreateAuthenticator(string schema, IServiceProvider serviceProvider)
        {
            var type = ResolveAuthenticatorType(schema);
            return null == type ? null : Activator.CreateInstance(type, serviceProvider) as IAuthenticator;
        }

        public static IAuthenticator? CreateAuthenticator(ConnectionSettings settings, IServiceProvider serviceProvider)
        {
            IAuthenticator? result = null;
            if (null != settings.Authenticator)
            {
                result = null == settings.AuthenticatorType ? null : Activator.CreateInstance(settings.AuthenticatorType, serviceProvider) as IAuthenticator;
            }
            var schema = settings.BrokerAuthConfig?.Schema;
            if (null != result)
            {
                if (!string.IsNullOrEmpty(schema) && result.GetType() != ResolveAuthenticatorType(schema))
                {
                    throw new InvalidOperationException($"Broker authentication schema {schema} is incompatible with configured authenticator {result.GetType()}");
                }
                return result;
            }
            return string.IsNullOrEmpty(schema) ? null : CreateAuthenticator(schema, serviceProvider);
        }
    }
}
