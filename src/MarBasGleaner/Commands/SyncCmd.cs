using CraftedSolutions.MarBasGleaner.Tracking;
using CraftedSolutions.MarBasGleaner.UI;
using CraftedSolutions.MarBasSchema.Transport;
using System.CommandLine.Invocation;

namespace CraftedSolutions.MarBasGleaner.Commands
{
    internal class SyncCmd
        : GenericCmd
    {
        public SyncCmd()
            : base("sync", SyncCmdL10n.CmdDesc)
        {
            Setup();
        }

        protected override void Setup()
        {
            base.Setup();
            AddOption(PushCmd.CheckpointOption);
            AddOption(PushCmd.StrategyOption);
            AddOption(PullCmd.OverwriteOption);
            AddOption(PullCmd.ForceCheckpointOption);
        }

        public sealed new class Worker(ITrackingService trackingService, ILogger<Worker> logger) : GenericCmd.Worker(trackingService, (ILogger)logger)
        {
            public int StartingCheckpoint { get; set; } = -1;
            public DuplicatesHandlingStrategy Strategy { get; set; } = DuplicatesHandlingStrategy.OverwriteSkipNewer;
            public bool Overwrite { get; set; }
            public bool ForceCheckpoint { get; set; }

            public override async Task<int> InvokeAsync(InvocationContext context)
            {
                var pushWorker = new PushCmd.Worker(_trackingService, _logger)
                {
                    Directory = Directory,
                    StartingCheckpoint = StartingCheckpoint,
                    Strategy = Strategy
                };
                var result = await pushWorker.InvokeAsync(context);
                if (0 == result)
                {
                    DisplayMessage(string.Empty, MessageSeparatorOption.Before);
                    var pullWorker = new PullCmd.Worker(_trackingService, _logger)
                    {
                        Directory = Directory,
                        Overwrite = Overwrite,
                        ForceCheckpoint = ForceCheckpoint
                    };
                    result = await pullWorker.InvokeAsync(context);
                }
                return result;
            }
        }
    }
}
