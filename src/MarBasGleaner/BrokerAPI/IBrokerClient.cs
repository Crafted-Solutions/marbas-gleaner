﻿using MarBasSchema.Grain;
using MarBasSchema.Sys;
using MarBasSchema.Transport;

namespace MarBasGleaner.BrokerAPI
{
    internal interface IBrokerClient: IDisposable
    {
        Uri? APIUrl { get; }
        Task<IServerInfo?> GetSystemInfo(CancellationToken cancellationToken = default);
        Task<IGrain?> GetGrain(Guid id, CancellationToken cancellationToken = default);
        Task<IGrain?> GetGrain(string path, CancellationToken cancellationToken = default);
        Task<IEnumerable<IGrain>> ListGrains(Guid parentId, bool resursive = false, DateTime? mtimeFrom = null, DateTime? mtimeTo = null, CancellationToken cancellationToken = default);
        Task<IEnumerable<IGrain>> GetGrainPath(Guid id, CancellationToken cancellationToken = default);
        Task<IEnumerable<IGrainTransportable>> ImportGrains(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    }
}
