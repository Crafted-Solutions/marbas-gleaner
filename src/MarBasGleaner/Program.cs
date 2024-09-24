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
            var runner = GetCommandLineBuilder().UseHost(Host.CreateDefaultBuilder, (builder) =>
            {
                builder.ConfigureServices((_, services) =>
                {
                    services
                        .AddOptions()
                        .AddHttpClient()
                        .AddSingleton<ITrackingService, TrackingService>();
                })
                .UseCommandHandler<TrackCmd, TrackCmd.Worker>()
                .UseCommandHandler<ConnectCmd, ConnectCmd.Worker>()
                .UseCommandHandler<StatusCmd, StatusCmd.Worker>()
                .UseCommandHandler<DiffCmd, DiffCmd.Worker>()
                .UseCommandHandler<PullCmd, PullCmd.Worker>();
            }).UseDefaults().Build();

            return await runner.InvokeAsync(args);
        }

        private static CommandLineBuilder GetCommandLineBuilder()
        {
            var rootCmd = new RootCommand(CommonL10n.ProgramDesc);
            rootCmd.AddCommand(new TrackCmd());
            rootCmd.AddCommand(new ConnectCmd());
            rootCmd.AddCommand(new StatusCmd());
            rootCmd.AddCommand(new DiffCmd());
            rootCmd.AddCommand(new PullCmd());
            return new CommandLineBuilder(rootCmd);
        }
    }
}