using System.CommandLine;
using System.CommandLine.Parsing;
using System.Reflection;
using System.Runtime.CompilerServices;
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
            Add(new Argument<Uri>("url")
            {
                Description = ConnectBaseCmdL10n.URLArgDesc,
                CustomParser = ParseStringConstructible<Uri>
            });
            base.Setup();
            Add(new Option<AuthenticationScheme>("--auth")
            {
                DefaultValueFactory = (_) => AuthenticationScheme.Auto,
                Description = ConnectBaseCmdL10n.AuthOptionDesc
            });
            Add(new Option<bool>("--ignore-ssl-errors")
            {
                DefaultValueFactory = (_) => false,
                Description = ConnectBaseCmdL10n.IgnoreSslErrorsOptionDesc
            });
            Add(new Option<bool>("--store-credentials")
            {
                DefaultValueFactory = (_) => false,
                Description = ConnectBaseCmdL10n.StoreCredentialsOptionDesc
            });
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
