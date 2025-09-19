using CraftedSolutions.MarBasGleaner.BrokerAPI;
using CraftedSolutions.MarBasSchema.Sys;

namespace CraftedSolutions.MarBasGleaner.Tracking
{
    internal interface ITrackingService
    {
        SnapshotDirectory GetSnapshotDirectory(string path = SnapshotDirectory.DefaultPath);
        Task<SnapshotDirectory> GetSnapshotDirectoryAsync(string path = SnapshotDirectory.DefaultPath, CancellationToken cancellationToken = default);
        IBrokerClient GetBrokerClient(ConnectionSettings settings, bool storeCredentials = true);
        Task<IBrokerClient> GetBrokerClientAsync(ConnectionSettings settings, bool storeCredentials = true, CancellationToken cancellationToken = default);
        Task<IServerInfo?> GetBrokerInfoAsync(ConnectionSettings settings, CancellationToken cancellationToken = default);
        IServerInfo? GetBrokerInfo(ConnectionSettings settings);
    }
}
