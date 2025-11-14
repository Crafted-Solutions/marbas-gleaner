using CraftedSolutions.MarBasGleaner.BrokerAPI;
using CraftedSolutions.MarBasSchema.Sys;

namespace CraftedSolutions.MarBasGleaner.Tracking
{
    internal interface ITrackingService
    {
        SnapshotDirectory GetSnapshotDirectory(string path = SnapshotDirectory.DefaultPath);
        Task<SnapshotDirectory> GetSnapshotDirectoryAsync(string path = SnapshotDirectory.DefaultPath, CancellationToken cancellationToken = default);
        IBrokerClient GetBrokerClient(ConnectionSettings settings);
        Task<IBrokerClient> GetBrokerClientAsync(ConnectionSettings settings, CancellationToken cancellationToken = default);
        Task<IServerInfo?> GetBrokerInfoAsync(ConnectionSettings settings, CancellationToken cancellationToken = default);
        IServerInfo? GetBrokerInfo(ConnectionSettings settings);
        Task<bool> LogoutFromBrokerAsync(ConnectionSettings settings, CancellationToken cancellationToken = default);
        bool LogoutFromBroker(ConnectionSettings settings);
    }
}
