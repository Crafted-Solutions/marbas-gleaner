using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using MarBasGleaner.BrokerAPI.Models;
using MarBasGleaner.Json;
using MarBasSchema.Broker;
using MarBasSchema.Grain;
using MarBasSchema.Sys;
using MarBasSchema.Transport;

namespace MarBasGleaner.BrokerAPI
{
    internal class BrokerClient(HttpClient httpClient, ILogger<BrokerClient> logger) : IBrokerClient
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
            var resp = await _httpClient.GetAsync($"{ApiPrefix}SysInfo", cancellationToken);
            if (!HandleHttpError(resp))
            {
                return await resp.Content.ReadFromJsonAsync<ServerInfo>(cancellationToken);
            }
            return null;
        }

        public async Task<IGrain?> GetGrain(Guid id, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            var resp = await _httpClient.GetAsync($"{ApiPrefix}Grain/{id:D}", cancellationToken);
            if (!HandleHttpError(resp))
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
            var resp = await _httpClient.GetAsync($"{ApiPrefix}Tree/{path}", cancellationToken);
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

        public async Task<IEnumerable<IGrain>> ListGrains(Guid parentId, bool resursive = false, DateTime? mtimeFrom = null, DateTime? mtimeTo = null, CancellationToken cancellationToken = default)
        {
            var result = Enumerable.Empty<IGrain>();
            if (cancellationToken.IsCancellationRequested)
            {
                return result;
            }
            var sortOptions = new ListSortOption<GrainSortField>(GrainSortField.Path, ListSortOrder.Asc);
            var query = $"{ApiPrefix}Grain/{parentId:D}/List?sortOptions={EncodeJsonParameter(sortOptions)}";
            if (resursive)
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
            var resp = await _httpClient.GetAsync(query, cancellationToken);
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
        public async Task<IEnumerable<IGrain>> GetGrainPath(Guid id, CancellationToken cancellationToken = default)
        {
            var result = Enumerable.Empty<IGrain>();
            if (cancellationToken.IsCancellationRequested)
            {
                return result;
            }
            var resp = await _httpClient.GetAsync($"{ApiPrefix}Grain/{id:D}/Path?includeSelf=true", cancellationToken);
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

        public async Task<IEnumerable<IGrainTransportable>> ImportGrains(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
        {
            var result = Enumerable.Empty<IGrainTransportable>();
            if (cancellationToken.IsCancellationRequested)
            {
                return result;
            }
            var resp = await _httpClient.PostAsJsonAsync($"{ApiPrefix}Transport/Out", ids, JsonOptions, cancellationToken);
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

        #endregion

        #region Disposition
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
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
        private bool HandleHttpError(HttpResponseMessage resp)
        {
            if (!resp.IsSuccessStatusCode)
            {
                if (_logger.IsEnabled(LogLevel.Error))
                {
                    _logger.LogError("Call to {uri} returned {errCode} ({reason})", resp.RequestMessage?.RequestUri?.ToString() ?? "unknown", resp.StatusCode, resp.ReasonPhrase);
                }
                return true;
            }
            return false;
        }

        private static string EncodeJsonParameter<T>(T parameterObj, bool dequote = false)
        {
            var str = JsonSerializer.Serialize(parameterObj, JsonOptions);
            return HttpUtility.UrlEncode(dequote ? str.Trim('"') : str);
        }
        #endregion
    }
}
