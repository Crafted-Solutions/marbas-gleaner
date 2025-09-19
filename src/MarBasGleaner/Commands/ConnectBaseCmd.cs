using System.CommandLine;
using CraftedSolutions.MarBasGleaner.BrokerAPI;
using CraftedSolutions.MarBasGleaner.BrokerAPI.Auth;
using CraftedSolutions.MarBasGleaner.Tracking;

namespace CraftedSolutions.MarBasGleaner.Commands
{
    internal class ConnectBaseCmd : GenericCmd
    {
        public ConnectBaseCmd(string name, string? description = null)
            : base(name, description)
        {
            Setup();
        }

        protected override void Setup()
        {
            AddArgument(new Argument<Uri>("url", ConnectBaseCmdL10n.URLArgDesc));
            base.Setup();
            AddOption(new Option<AuthenticationScheme>("--auth", () => AuthenticationScheme.Auto, ConnectBaseCmdL10n.AuthOptionDesc));
            AddOption(new Option<bool>("--ignore-ssl-errors", () => false, ConnectBaseCmdL10n.IgnoreSslErrorsOptionDesc));
            AddOption(new Option<bool>("--store-credentials", () => false, ConnectBaseCmdL10n.StoreCredentialsOptionDesc));
        }

        public abstract new class Worker : GenericCmd.Worker
        {
            public Worker(ITrackingService trackingService, ILogger<Worker> logger)
                : base(trackingService, (ILogger)logger)
            {

            }

            protected Worker(ITrackingService trackingService, ILogger logger)
                : base(trackingService, logger)
            {
            }

            public Uri? Url { get; set; }
            public AuthenticationScheme Auth { get; set; } = AuthenticationScheme.Auto;
            public bool IgnoreSslErrors { get; set; } = false;
            public bool StoreCredentials { get; set; } = false;


            protected ConnectionSettings CreateConnectionSettings()
            {
                var result = new ConnectionSettings
                {
                    BrokerUrl = Url!,
                    IgnoreSslErrors = IgnoreSslErrors
                };

                var builder = new UriBuilder(result.BrokerUrl);
                if (result.BrokerUrl.AbsolutePath.EndsWith($"/{BrokerClient.ApiPrefix}"))
                {
                    builder.Path = builder.Path[..^BrokerClient.ApiPrefix.Length];
                }
                else if (result.BrokerUrl.AbsolutePath.EndsWith($"/{BrokerClient.ApiPrefix[..(BrokerClient.ApiPrefix.Length - 1)]}"))
                {
                    builder.Path = builder.Path[..(builder.Path.Length - BrokerClient.ApiPrefix.Length + 1)];
                }
                if (!builder.Path.EndsWith('/'))
                {
                    builder.Path += '/';
                }
                result.BrokerUrl = builder.Uri;
                
                if (AuthenticationScheme.Auto != Auth)
                {
                    result.AuthenticatorType = AuthenticatorFactory.ResolveAuthenticatorType(Enum.GetName(Auth)!);
                }
                return result;
            }
        }
    }
}
