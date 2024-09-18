using System.CommandLine.Invocation;
using MarBasGleaner.Tracking;

namespace MarBasGleaner.Commands
{
    internal class PullCmd: GenericCmd
    {
        public PullCmd()
            : base("pull", "Pulls modified and new grains from MarBas broker into snapshot")
        {
            Setup();
        }

        public new class Worker(ITrackingService trackingService, ILogger<Worker> logger) : GenericCmd.Worker(trackingService, (ILogger)logger)
        {
            public override async Task<int> InvokeAsync(InvocationContext context)
            {
                return await Task.FromResult(0);
            }
        }
    }
}
