using System.Text.Json;
using MarBasGleaner.BrokerAPI;

namespace MarBasGleaner.Tracking
{
    internal interface ITrackingService
    {
        SnapshotDirectory GetSnapshotDirectory(string path = SnapshotDirectory.DefaultPath);
        Task<SnapshotDirectory> GetSnapshotDirectoryAsync(string path = SnapshotDirectory.DefaultPath, CancellationToken cancellationToken = default);
        IBrokerClient GetBrokerClient(ConnectionSettings settings, bool storeCredentials = true);
    }
}
