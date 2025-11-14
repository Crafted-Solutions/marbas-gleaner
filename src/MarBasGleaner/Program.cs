using CraftedSolutions.MarBasGleaner.BrokerAPI.Auth;
using CraftedSolutions.MarBasGleaner.Commands;
using CraftedSolutions.MarBasGleaner.Tracking;
using CraftedSolutions.MarBasGleaner.UI;
using diVISION.CommandLineX.Hosting;
using System.CommandLine;

namespace CraftedSolutions.MarBasGleaner
{
    public class Program
    {
        public async static Task<int> Main(string[] args)
        {
            var rootCmd = new RootCommand(CommonL10n.ProgramDesc);

            var builder = Host.CreateDefaultBuilder(args);
            builder
                .ConfigureAppConfiguration((config) =>
                {
                    config.SetBasePath(AppDomain.CurrentDomain.BaseDirectory);
                })
                .ConfigureDefaults(null)
                .UseConsoleLifetime()
                .ConfigureServices((ctx, services) =>
                {
                    services
                        .AddOptions()
                        .AddHttpClient()
                        .AddSingleton<AuthenticatorFactory>()
                        .AddSingleton<IFeedbackService, ConsoleFeedbackService>()
                        .AddSingleton<ITrackingService, TrackingService>();

                    services.AddHttpClient("bootstrap-client");
                    services.AddHttpClient("broker-client");
                    if (ctx.HostingEnvironment.IsDevelopment() && ctx.Configuration.GetValue("HttpClient:UseLaxSSLHandler", false))
                    {
                        services.AddHttpClient("bootstrap-lax-ssl-client").ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler()
                        {
                            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                        });
                        services.AddHttpClient("broker-lax-ssl-client").ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler()
                        {
                            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                        });
                    }
                })
                .UseCommandLine(rootCmd)
                .UseCommandWithAction<TrackCmd.Worker>(rootCmd, new TrackCmd())
                .UseCommandWithAction<ConnectCmd.Worker>(rootCmd, new ConnectCmd())
                .UseCommandWithAction<LogoutCmd.Worker>(rootCmd, new LogoutCmd())
                .UseCommandWithAction<StatusCmd.Worker>(rootCmd, new StatusCmd())
                .UseCommandWithAction<InfoCmd.Worker>(rootCmd, new InfoCmd())
                .UseCommandWithAction<DiffCmd.Worker>(rootCmd, new DiffCmd())
                .UseCommandWithAction<PullCmd.Worker>(rootCmd, new PullCmd())
                .UseCommandWithAction<PushCmd.Worker>(rootCmd, new PushCmd())
                .UseCommandWithAction<SyncCmd.Worker>(rootCmd, new SyncCmd());

            using var host = builder.Build();

            return await host.RunCommandLineAsync(args);
        }
    }
}