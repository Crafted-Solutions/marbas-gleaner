using CraftedSolutions.MarBasSchema.Grain;
using CraftedSolutions.MarBasSchema.Sys;
using CraftedSolutions.MarBasSchema.Transport;

namespace CraftedSolutions.MarBasGleaner.BrokerAPI
{
    internal interface IBrokerClient : IDisposable
    {
        Uri? APIUrl { get; }
        Task<IServerInfo?> GetSystemInfo(CancellationToken cancellationToken = default);
        Task<IGrain?> GetGrain(Guid id, bool notFoundIsError = true, CancellationToken cancellationToken = default);
        Task<IGrain?> GetGrain(string path, CancellationToken cancellationToken = default);
        Task<IDictionary<Guid, bool>> CheckGrainsExist(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
        Task<IEnumerable<IGrain>> ListGrains(Guid parentId, bool recursive = false, DateTime? mtimeFrom = null, DateTime? mtimeTo = null, bool includeParent = false, CancellationToken cancellationToken = default);
        Task<IEnumerable<IGrain>> GetGrainPath(Guid id, CancellationToken cancellationToken = default);
        Task<IEnumerable<IGrainTransportable>> PullGrains(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
        Task<IGrainImportResults?> PushGrains(ISet<IGrainTransportable> grains, ISet<Guid>? grainsToDelete = null, DuplicatesHandlingStrategy duplicatesHandling = DuplicatesHandlingStrategy.Merge, CancellationToken cancellationToken = default);
    }
}
