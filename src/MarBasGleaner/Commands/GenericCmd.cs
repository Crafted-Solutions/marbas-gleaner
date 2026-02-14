using CraftedSolutions.MarBasGleaner.BrokerAPI;
using CraftedSolutions.MarBasGleaner.Tracking;
using CraftedSolutions.MarBasGleaner.UI;
using CraftedSolutions.MarBasSchema.Sys;
using diVISION.CommandLineX;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Reflection;

namespace CraftedSolutions.MarBasGleaner.Commands
{
    internal abstract class GenericCmd(string name, string? description = null) : Command(name, description)
    {
        public static readonly Option<string> DirectoryOption = new("--directory", "-d")
        {
            DefaultValueFactory = (_) => SnapshotDirectory.DefaultPath,
            Description = GenericCmdL10n.DirectoryOpionDesc
        };

        protected virtual void Setup()
        {
            Add(DirectoryOption);
        }

        public abstract class Worker : ICommandAction
        {
            protected readonly ILogger _logger;
            protected readonly ITrackingService _trackingService;
            protected readonly IFeedbackService _feedbackService;

            public Worker(ITrackingService trackingService, IFeedbackService feedbackService, ILogger<Worker> logger)
            {
                _logger = logger;
                _trackingService = trackingService;
                _feedbackService = feedbackService;
            }

            protected Worker(ITrackingService trackingService, IFeedbackService feedbackService, ILogger logger)
            {
                _logger = logger;
                _trackingService = trackingService;
                _feedbackService = feedbackService;
            }

            public string Directory { get; set; } = SnapshotDirectory.DefaultPath;

            public int Invoke(CommandActionContext context)
            {
                return InvokeAsync(context).Result;
            }

            public abstract Task<int> InvokeAsync(CommandActionContext context, CancellationToken cancellationToken = default);

            protected async Task<ConnectionCheckResult> ValidateBrokerConnection(ITrackingService trackingService, ConnectionSettings settings,
                Version? snapshotVersion = null, Guid? instanceId = null, CancellationToken cancellationToken = default)
            {
                var result = new ConnectionCheckResult
                {
                    Code = CmdResultCode.Success
                };

                try
                {
                    result.Info = await trackingService.GetBrokerInfoAsync(settings, cancellationToken);
                    if (null == result.Info)
                    {
                        ReportError(result.Code = CmdResultCode.BrokerConnectionError, string.Format(GenericCmdL10n.ErrorBrokerConnection, settings.BrokerUrl));
                    }
                    else if (result.Info.Version < ConnectionSettings.MinimumAPIVersion)
                    {
                        ReportError(result.Code = CmdResultCode.APIVersionError, string.Format(GenericCmdL10n.ErrorAPIVersion, ConnectionSettings.MinimumAPIVersion, result.Info.Version));
                    }
                    else if (null != snapshotVersion && result.Info.SchemaVersion != snapshotVersion)
                    {
                        ReportError(result.Code = CmdResultCode.SchemaVersionError, string.Format(GenericCmdL10n.ErrorSchemaVersion, result.Info.SchemaVersion, snapshotVersion));
                    }
                    else if (null != instanceId && result.Info.InstanceId != instanceId)
                    {
                        ReportError(result.Code = CmdResultCode.InstanceIdError, string.Format(GenericCmdL10n.ErrorInstanceId, result.Info.InstanceId, instanceId));
                    }
                }
                catch (Exception e)
                {
                    if (_logger.IsEnabled(LogLevel.Error))
                    {
                        _logger.LogError(e, "Broker connection error");
                    }
                    ReportError(result.Code = CmdResultCode.BrokerConnectionError, string.Format(GenericCmdL10n.ErrorBrokerConnectionException, settings.BrokerUrl, e.Message));
                }
                return result;
            }

            protected int ValidateSnapshot(SnapshotDirectory snapshotDir, bool mustBeConnected = true, bool requiresCheckpoint = true)
            {
                if (!snapshotDir.IsSnapshot || !snapshotDir.IsReady)
                {
                    return ReportError(CmdResultCode.SnapshotStateError, string.Format(GenericCmdL10n.ErrorReadyState, snapshotDir.FullPath));
                }
                if (null != snapshotDir.Snapshot && snapshotDir.Snapshot.Version != Snapshot.SupportedVersion)
                {
                    return ReportError(CmdResultCode.SnapshotVersionError, string.Format(GenericCmdL10n.ErrorSnapshotVersion, snapshotDir.FullPath, snapshotDir.Snapshot.Version, Snapshot.SupportedVersion));
                }
                if (mustBeConnected && !snapshotDir.IsConnected)
                {
                    return ReportError(CmdResultCode.SnapshotStateError, string.Format(GenericCmdL10n.ErrorConnectedState, snapshotDir.FullPath));
                }
                if (requiresCheckpoint && (mustBeConnected && null == snapshotDir.LocalCheckpoint || null == snapshotDir.SharedCheckpoint))
                {
                    return ReportError(CmdResultCode.SnapshotStateError, string.Format(GenericCmdL10n.ErrorCheckpointMissing, SnapshotDirectory.LocalStateFileName));
                }
                return 0;
            }

            internal int ReportError(CmdResultCode error, string message)
            {
                _feedbackService.DisplayError(message);
                return (int)error;
            }

            internal void DisplayInfo(string message)
            {
                _feedbackService.DisplayInfo(message);
            }

            internal void DisplayWarning(string message)
            {
                _feedbackService.DisplayWarning(message);
            }

            internal void DisplayMessage(string message, MessageSeparatorOption separatorOption = MessageSeparatorOption.None)
            {
                _feedbackService.DisplayMessage(message, separatorOption);
            }
        }

        internal static TResult? ParseStringConstructible<TResult>(SymbolResult result)
            where TResult : class
        {
            try
            {
                return (TResult?)Activator.CreateInstance(typeof(TResult), result.Tokens[0].Value);
            }
            catch (TargetInvocationException e)
            {
                if (null == e.InnerException)
                {
                    throw;
                }
                Symbol symbol = result switch
                {
                    CommandResult commandResult => commandResult.Command,
                    ArgumentResult argumentResult => argumentResult.Argument,
                    OptionResult optionResult => optionResult.Option,
                    _ => throw new NotSupportedException($"Type {result.GetType()} is not supported")
                };

                result.AddError(string.Format(GenericCmdL10n.ErrorSymbolParse, symbol.Name, string.Join(' ', result.Tokens)
                    , (string.IsNullOrEmpty(e.InnerException.Message) ? $"unexpected error: {e.InnerException}" : e.InnerException.Message)));
                return null;
            }
        }

        internal struct ConnectionCheckResult
        {
            public CmdResultCode Code;
            public IServerInfo? Info;
        }
    }
}
