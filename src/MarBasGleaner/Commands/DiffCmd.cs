using System.CommandLine;
using System.CommandLine.Invocation;
using DiffPlex.DiffBuilder.Model;
using DiffPlex.DiffBuilder;
using DiffPlex;
using MarBasGleaner.Json;
using System.Text.Json;
using MarBasGleaner.Tracking;
using MarBasSchema.Transport;

namespace MarBasGleaner.Commands
{
    internal sealed class DiffCmd: GenericCmd
    {
        public enum CompareMode
        {
            Auto = 0x0,
            Snapshot = 0x0001,
            Broker = 0x0002,
            Snapshot2Broker = Broker << 8 | Snapshot,
            Broker2Snapshot = Snapshot << 8 | Broker
        }

        public DiffCmd():
            base("diff", DiffCmdL10n.CmdDesc)
        {
            Setup();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments", Justification = "The Setup() method is meant to be called once per lifetime")]
        protected override void Setup()
        {
            AddArgument(new Argument<Guid>("grain-ids", "ID of the grain to use as comparison base, single argument version compares snapshot with broker, 2 different IDs indicate snapshot version of both by defaulft (s. --mode option)")
            {
                Arity = new ArgumentArity(1, 2)
            });
            base.Setup();
            AddOption(new Option<CompareMode>(new[] { "--mode", "-m" }, () => CompareMode.Auto, $"Comparison mode applied to grains identified by <grain-ids> argument, {Enum.GetName(CompareMode.Snapshot2Broker)} is default for single ID, {Enum.GetName(CompareMode.Snapshot)} - for 2 distinct IDs"));
        }

        internal static void DisplayDiff(IGrainTransportable source, IGrainTransportable target)
        {
            var localText = JsonSerializer.Serialize(source, JsonDefaults.SerializationOptions);
            var brokerText = JsonSerializer.Serialize(target, JsonDefaults.SerializationOptions);

            var diffBuilder = new InlineDiffBuilder(new Differ());
            var diff = diffBuilder.BuildDiffModel(localText, brokerText);
            
            if (diff.HasDifferences)
            {
                foreach (var line in diff.Lines)
                {
                    Console.ForegroundColor = ConsoleColor.Blue;
                    if (line.Position.HasValue) Console.Write(line.Position.Value);
                    Console.Write('\t');
                    switch (line.Type)
                    {
                        case ChangeType.Inserted:
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.Write("+ ");
                            break;
                        case ChangeType.Deleted:
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.Write("- ");
                            break;
                        default:
                            Console.ForegroundColor = ConsoleColor.Gray;
                            Console.Write("  ");
                            break;
                    }

                    Console.Write(line.Text);
                    Console.Write(Environment.NewLine);
                }
            }
            else
            {
                DisplayInfo($"Grains are identical");
            }
            Console.ResetColor();
        }

        public new class Worker(ITrackingService trackingService, ILogger<Worker> logger) :
            GenericCmd.Worker(trackingService, (ILogger)logger)
        {
            public IEnumerable<Guid> GrainIds { get; set; } = Enumerable.Empty<Guid>();
            public CompareMode Mode { get; set; } = CompareMode.Auto;
            private CompareMode SourceGrainMode => Mode & (CompareMode)~((int)(CompareMode.Broker | CompareMode.Snapshot) << 8);
            private CompareMode TargetGrainMode => CompareMode.Broker < Mode ? (CompareMode)((int)Mode >> 8) : Mode;

            public async override Task<int> InvokeAsync(InvocationContext context)
            {
                var ctoken = context.GetCancellationToken();
                var snapshotDir = await _trackingService.GetSnapshotDirectoryAsync(Directory, ctoken);

                var result = CheckSnapshot(snapshotDir);
                if (0 != result)
                {
                    return result;
                }

                var ids = (GrainIds.FirstOrDefault(), GrainIds.LastOrDefault());
                if ((CompareMode.Snapshot == Mode || CompareMode.Broker == Mode) && ids.Item1 == ids.Item2)
                {
                    return ReportError(CmdResultCode.ParameterError, $"Missing second grain ID for comparison mode {Enum.GetName(Mode)}");
                }

                if (CompareMode.Auto == Mode)
                {
                    Mode = ids.Item1 == ids.Item2 ? CompareMode.Snapshot2Broker : CompareMode.Snapshot;
                }

                DisplayMessage($"Comparing {ids.Item1:D} ({Enum.GetName(SourceGrainMode)}) to {(ids.Item1 == ids.Item2 ? "itself" : $"{ids.Item2:D}")} ({Enum.GetName(TargetGrainMode)})", MessageSeparatorOption.After);

                (IGrainTransportable?, IGrainTransportable?) grains = (null, null);
                if (CompareMode.Broker == SourceGrainMode || CompareMode.Broker == TargetGrainMode)
                {
                    var idsToFetch = new HashSet<Guid>();
                    if (CompareMode.Broker == SourceGrainMode)
                    {
                        idsToFetch.Add(ids.Item1);
                    }
                    if (CompareMode.Broker == TargetGrainMode)
                    {
                        idsToFetch.Add(ids.Item2);
                    }

                    using var client = _trackingService.GetBrokerClient(snapshotDir.ConnectionSettings!);
                    var brokerGrains = await client.ImportGrains(idsToFetch, ctoken);
                    foreach (var grain in brokerGrains)
                    {
                        if (CompareMode.Broker == SourceGrainMode && grain.Id == ids.Item1)
                        {
                            grains.Item1 = grain;
                        }
                        else if (CompareMode.Broker == TargetGrainMode && grain.Id == ids.Item2)
                        {
                            grains.Item2 = grain;
                        }
                    }
                }
                if (CompareMode.Snapshot == SourceGrainMode)
                {
                    grains.Item1 = await snapshotDir.LoadGrainById<GrainTransportable>(ids.Item1, cancellationToken: ctoken);
                }
                if (CompareMode.Snapshot == TargetGrainMode && ids.Item1 != ids.Item2)
                {
                    grains.Item2 = await snapshotDir.LoadGrainById<GrainTransportable>(ids.Item2, cancellationToken: ctoken);
                }

                if (null == grains.Item1)
                {
                    return ReportError(CmdResultCode.GrainLoadError, $"Failed to load grain {ids.Item1:D} from {Enum.GetName(SourceGrainMode)}");
                }
                if (null == grains.Item2)
                {
                    return ReportError(CmdResultCode.GrainLoadError, $"Failed to load grain {ids.Item2:D} from {Enum.GetName(TargetGrainMode)}");
                }

                DisplayDiff(grains.Item1, grains.Item2);

                return result;
            }

        }
    }
}
