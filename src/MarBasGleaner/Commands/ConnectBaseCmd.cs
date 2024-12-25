using System.CommandLine;
using MarBasGleaner.BrokerAPI;
using MarBasGleaner.BrokerAPI.Auth;
using MarBasGleaner.Tracking;

namespace MarBasGleaner.Commands
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
            AddOption(new Option<string>("--auth", () => BasicAuthenticator.SchemeName, ConnectBaseCmdL10n.AuthOptionDesc));
            AddOption(new Option<bool>("--ignore-ssl-errors", () => false, ConnectBaseCmdL10n.IgnoreSslErrorsOptionDesc));
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
            public string Auth { get; set; } = BasicAuthenticator.SchemeName;
            public bool IgnoreSslErrors { get; set; } = false;


            protected ConnectionSettings CreateConnectionSettings()
            {
                return new ConnectionSettings
                {
                    BrokerUrl = Url!,
                    AuthenticatorType = AuthenticatorFactory.ResolveAuthenticatorType(Auth) ?? typeof(BasicAuthenticator),
                    IgnoreSslErrors = IgnoreSslErrors
                };
            }
        }
    }
}
