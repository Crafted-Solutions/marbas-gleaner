using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using CraftedSolutions.MarBasSchema.Broker;
using CraftedSolutions.MarBasSchema.Grain;
using CraftedSolutions.MarBasSchema.Sys;
using CraftedSolutions.MarBasSchema.Transport;
using CraftedSolutions.MarBasGleaner.BrokerAPI.Models;
using CraftedSolutions.MarBasGleaner.Json;

namespace CraftedSolutions.MarBasGleaner.BrokerAPI
{
    internal sealed class BrokerClient(HttpClient httpClient, ILogger<BrokerClient> logger) : IBrokerClient
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonDefaults.SerializationOptions) { WriteIndented = false };

        private readonly HttpClient _httpClient = httpClient;
        private readonly ILogger _logger = logger;
        private bool _disposed;

        public const string ApiPrefix = "api/marbas/";

        #region Public Interface
        public Uri? APIUrl => _httpClient.BaseAddress;

        public async Task<IServerInfo?> GetSystemInfo(CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            using var resp = await _httpClient.GetAsync($"{ApiPrefix}SysInfo", cancellationToken);
            if (!HandleHttpError(resp))
            {
                return await resp.Content.ReadFromJsonAsync<ServerInfo>(cancellationToken);
            }
            return null;
        }

        public async Task<IGrain?> GetGrain(Guid id, bool notFoundIsError = true, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            using var resp = await _httpClient.GetAsync($"{ApiPrefix}Grain/{id:D}", cancellationToken);
            if (!(!notFoundIsError && HttpStatusCode.NotFound == resp.StatusCode || HandleHttpError(resp)))
            {
                var mbresult = await resp.Content.ReadFromJsonAsync<MarBasResult<GrainYield>>(cancellationToken);
                if (null != mbresult && mbresult.Success)
                {
                    return mbresult.Yield;
                }
            }
            return null;
        }

        public async Task<IGrain?> GetGrain(string path, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            using var resp = await _httpClient.GetAsync($"{ApiPrefix}Tree/{path}", cancellationToken);
            if (!HandleHttpError(resp))
            {
                var mbresult = await resp.Content.ReadFromJsonAsync<MarBasResult<IEnumerable<GrainYield>>>(cancellationToken);
                if (null != mbresult && mbresult.Success)
                {
                    return mbresult.Yield?.FirstOrDefault();
                }
            }
            return null;
        }

        public async Task<IDictionary<Guid, bool>> CheckGrainsExist(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested || !ids.Any())
            {
                return ImmutableDictionary<Guid, bool>.Empty;
            }


            var result = new Dictionary<Guid, bool>(ids.Select(x => new KeyValuePair<Guid, bool>(x, false)));

            using var resp = await _httpClient.PostAsJsonAsync($"{ApiPrefix}Grain/VerifyExist", ids, cancellationToken);
            if (!HandleHttpError(resp))
            {
                var mbresult = await resp.Content.ReadFromJsonAsync<MarBasResult<IDictionary<Guid, bool>>>(cancellationToken);
                if (true == mbresult?.Success && null != mbresult?.Yield)
                {
                    return mbresult.Yield;
                }
            }
            throw new ApplicationException($"API {resp.RequestMessage?.RequestUri} hasn't return expected result (IDs: {string.Join(", ", ids)})");
        }

        public async Task<IEnumerable<IGrain>> ListGrains(Guid parentId, bool recursive = false, DateTime? mtimeFrom = null, DateTime? mtimeTo = null, bool includeParent = false, CancellationToken cancellationToken = default)
        {
            IEnumerable<IGrain> result = new List<IGrain>();
            if (cancellationToken.IsCancellationRequested)
            {
                return result;
            }
            var sortOptions = new ListSortOption<GrainSortField>(GrainSortField.Path, ListSortOrder.Asc);
            var query = $"{ApiPrefix}Grain/{parentId:D}/List?sortOptions={EncodeJsonParameter(sortOptions)}";
            if (recursive)
            {
                query += "&recursive=true";
            }
            if (null != mtimeFrom)
            {
                query += $"&mTimeFrom={EncodeJsonParameter(mtimeFrom, true)}";
            }
            if (null != mtimeTo)
            {
                query += $"&mTimeTo={EncodeJsonParameter(mtimeTo, true)}";
            }
            using var resp = await _httpClient.GetAsync(query, cancellationToken);
            if (!HandleHttpError(resp))
            {
                var mbresult = await resp.Content.ReadFromJsonAsync<MarBasResult<IEnumerable<GrainYield>>>(cancellationToken);
                if (null != mbresult && mbresult.Success && null != mbresult.Yield)
                {
                    result = mbresult.Yield;
                }
            }
            if (includeParent && !recursive)
            {
                var parent = await GetGrain(parentId, cancellationToken: cancellationToken);
                if (null != parent && (null == mtimeFrom || parent.MTime > mtimeFrom) && (null == mtimeTo || parent.MTime < mtimeTo))
                {
                    result = result.Prepend(parent);
                }
            }
            return result;
        }
        public async Task<IEnumerable<IGrain>> GetGrainPath(Guid id, CancellationToken cancellationToken = default)
        {
            var result = Enumerable.Empty<IGrain>();
            if (cancellationToken.IsCancellationRequested)
            {
                return result;
            }
            using var resp = await _httpClient.GetAsync($"{ApiPrefix}Grain/{id:D}/Path?includeSelf=true", cancellationToken);
            if (!HandleHttpError(resp))
            {
                var mbresult = await resp.Content.ReadFromJsonAsync<MarBasResult<IEnumerable<GrainYield>>>(cancellationToken);
                if (null != mbresult && mbresult.Success && null != mbresult.Yield)
                {
                    result = mbresult.Yield;
                }
            }
            return result;
        }

        public async Task<IEnumerable<IGrainTransportable>> PullGrains(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
        {
            var result = Enumerable.Empty<IGrainTransportable>();
            if (cancellationToken.IsCancellationRequested)
            {
                return result;
            }
            using var resp = await _httpClient.PostAsJsonAsync($"{ApiPrefix}Transport/Out", ids, JsonOptions, cancellationToken);
            if (!HandleHttpError(resp))
            {
                var mbresult = await resp.Content.ReadFromJsonAsync<MarBasResult<IEnumerable<GrainTransportable>>>(JsonDefaults.DeserializationOptions, cancellationToken);
                if (null != mbresult && mbresult.Success && null != mbresult.Yield)
                {
                    result = mbresult.Yield;
                }
            }
            return result;
        }

        public async Task<IGrainImportResults?> PushGrains(ISet<IGrainTransportable> grains, ISet<Guid>? grainsToDelete = null, DuplicatesHandlingStrategy duplicatesHandling = DuplicatesHandlingStrategy.Merge, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            if (!grains.Any() && true != grainsToDelete?.Any())
            {
                var result = new GrainImportResults()
                {
                    ImportedCount = 0
                };
                result.AddFeedback(new BrokerOperationFeedback("Nothing to export"));
                return result;
            }

            var req = new GrainImportModel()
            {
                Grains = grains,
                GrainsToDelete = grainsToDelete,
                DuplicatesHandling = duplicatesHandling
            };
            using var resp = await _httpClient.PutAsJsonAsync($"{ApiPrefix}Transport/In", req, JsonOptions, cancellationToken);
            if (!HandleHttpError(resp))
            {
                var mbresult = await resp.Content.ReadFromJsonAsync<MarBasResult<GrainImportResults>>(JsonDefaults.DeserializationOptions, cancellationToken);
                return mbresult?.Yield;
            }
            return null;
        }

        #endregion

        #region Disposition
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _httpClient.Dispose();
            }

            _disposed = true;
        }
        #endregion

        #region Helper Methods
        public static bool HandleHttpError(HttpResponseMessage resp, ILogger logger)
        {
            if (!resp.IsSuccessStatusCode)
            {
                if (logger.IsEnabled(LogLevel.Error))
                {
                    var body = string.Empty;
                    try
                    {
                        body = resp.Content.ReadAsStringAsync().Result;
                    }
                    catch (Exception) { }
                    logger.LogError("Call to {uri} returned {errCode} ({reason}{body})", resp.RequestMessage?.RequestUri?.ToString() ?? "unknown"
                        , resp.StatusCode, resp.ReasonPhrase, string.IsNullOrEmpty(body) ? string.Empty : $": {body}");
                }
                return true;
            }
            return false;

        }

        private bool HandleHttpError(HttpResponseMessage resp)
        {
            return HandleHttpError(resp, _logger);
        }

        private static string EncodeJsonParameter<T>(T parameterObj, bool dequote = false)
        {
            var str = JsonSerializer.Serialize(parameterObj, JsonOptions);
            return HttpUtility.UrlEncode(dequote ? str.Trim('"') : str);
        }
        #endregion
    }
}
