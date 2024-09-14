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
                .UseCommandHandler<StatusCmd, StatusCmd.Worker>();
            }).UseDefaults().Build();

            return await runner.InvokeAsync(args);
        }

        private static CommandLineBuilder GetCommandLineBuilder()
        {
            var rootCmd = new RootCommand("Gleans and synchronizes changes on MarBas grains");
            rootCmd.AddCommand(new TrackCmd());
            rootCmd.AddCommand(new ConnectCmd());
            rootCmd.AddCommand(new StatusCmd());
            return new CommandLineBuilder(rootCmd);
        }
    }
}