using System.Text.Json;
using CraftedSolutions.MarBasGleaner.BrokerAPI;
using MarBasGleaner.Tracking;

namespace CraftedSolutions.MarBasGleaner.Tracking
{
    internal interface ITrackingService
    {
        SnapshotDirectory GetSnapshotDirectory(string path = SnapshotDirectory.DefaultPath);
        Task<SnapshotDirectory> GetSnapshotDirectoryAsync(string path = SnapshotDirectory.DefaultPath, CancellationToken cancellationToken = default);
        IBrokerClient GetBrokerClient(ConnectionSettings settings, bool storeCredentials = true);
    }
}
