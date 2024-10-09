using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Hosting;
using System.CommandLine.Parsing;
using MarBasGleaner.Commands;
using MarBasGleaner.Tracking;

namespace MarBasGleaner
{
    public class Program
    {
        public async static Task<int> Main(string[] args)
        {
            var runner = CreateCommandLineBuilder().UseHost(Host.CreateDefaultBuilder, (builder) =>
            {
                builder
                    .ConfigureAppConfiguration((config) =>
                    {
                        config.SetBasePath(AppDomain.CurrentDomain.BaseDirectory);
                    })
                    .ConfigureDefaults(null)
                    .ConfigureServices((_, services) =>
                    {
                        services
                            .AddOptions()
                            .AddHttpClient()
                            .AddSingleton<ITrackingService, TrackingService>();
                    })
                    .UseCommandHandler<TrackCmd, TrackCmd.Worker>()
                    .UseCommandHandler<ConnectCmd, ConnectCmd.Worker>()
                    .UseCommandHandler<StatusCmd, StatusCmd.Worker>()
                    .UseCommandHandler<InfoCmd, InfoCmd.Worker>()
                    .UseCommandHandler<DiffCmd, DiffCmd.Worker>()
                    .UseCommandHandler<PullCmd, PullCmd.Worker>()
                    .UseCommandHandler<PushCmd, PushCmd.Worker>();

            }).UseDefaults().Build();

            return await runner.InvokeAsync(args);
        }

        private static CommandLineBuilder CreateCommandLineBuilder()
        {
            var rootCmd = new RootCommand(CommonL10n.ProgramDesc);
            rootCmd.AddCommand(new TrackCmd());
            rootCmd.AddCommand(new ConnectCmd());
            rootCmd.AddCommand(new StatusCmd());
            rootCmd.AddCommand(new InfoCmd());
            rootCmd.AddCommand(new DiffCmd());
            rootCmd.AddCommand(new PullCmd());
            rootCmd.AddCommand(new PushCmd());
            return new CommandLineBuilder(rootCmd);
        }
    }
}