using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Logging;
using Polly;
using Polly.Retry;
using StreamingCatalogs.Services.Tmdb.Dto;

namespace StreamingCatalogs.Services
{
    public class TmdbService
    {
        private const string BaseUrl = "https://api.themoviedb.org/3/";
        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;
        private readonly PluginConfiguration _config;

        // TMDB API v3 allows 40 requests per 10 seconds.
        // To stay safe, we'll force requests to be sequential and have a minimum delay between them.
        // 10000ms / 40 reqs = 250ms per request. We'll use a slightly higher value to be safe.
        private static readonly SemaphoreSlim RateLimiter = new SemaphoreSlim(1, 1);
        private const int DelayBetweenRequestsMs = 275;

        private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;

        public TmdbService(ILogManager logManager, HttpClient httpClient, PluginConfiguration config)
        {
            _logger = logManager.GetLogger(GetType().Name);
            _httpClient = httpClient;
            _config = config ?? throw new ArgumentNullException(nameof(config));

            _retryPolicy = Policy
                .Handle<HttpRequestException>()
                .OrResult<HttpResponseMessage>(r => r.StatusCode == HttpStatusCode.TooManyRequests || r.StatusCode >= HttpStatusCode.InternalServerError)
                .WaitAndRetryAsync(3, retryAttempt =>
                    {
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                        _logger.Warn("TMDB request failed. Retrying in {0}...", delay);
                        return delay;
                    }
                );
        }

        public async Task<List<TmdbItem>> GetCatalogItemsAsync(string mediaType, string providerId, string countryCode, int limit, string sortBy, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_config.ApiKey))
            {
                _logger.Error("TMDB API Key is not configured. Please add it in the plugin settings.");
                return new List<TmdbItem>();
            }

            // Override with default values from plugin configuration if enabled
            if (_config.AlwaysEnableDefaultProviders)
            {
                if (!string.IsNullOrWhiteSpace(_config.DefaultProviderIds))
                {
                    providerId = _config.DefaultProviderIds;
                }
                if (!string.IsNullOrWhiteSpace(_config.DefaultWatchRegion))
                {
                    countryCode = _config.DefaultWatchRegion;
                }
                if (!string.IsNullOrWhiteSpace(_config.DefaultSortBy))
                {
                    sortBy = _config.DefaultSortBy;
                }
                _logger.Info("Using default providers from configuration: IDs '{0}', Region '{1}', SortBy '{2}'", providerId, countryCode, sortBy);
            }

            var allItems = new List<TmdbItem>();
            var page = 1;
            var totalPages = 1; // Start with 1 to enter the loop

            while (allItems.Count < limit && page <= totalPages && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var pagedResult = await GetCatalogPageAsync(mediaType, providerId, countryCode, sortBy, page, cancellationToken);
                    if (pagedResult?.Results != null && pagedResult.Results.Any())
                    {
                        allItems.AddRange(pagedResult.Results);
                        totalPages = pagedResult.TotalPages;
                        page++;
                    }
                    else
                    {
                        // No more results or an error occurred, break the loop
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Failed to fetch page {0} from TMDB for provider {1}", ex, page, providerId);
                    // Stop trying if one page fails to avoid hammering a broken endpoint
                    break;
                }
            }

            return allItems.Take(limit).ToList();
        }

        private async Task<TmdbPagedResult<TmdbItem>?> GetCatalogPageAsync(string mediaType, string providerId, string countryCode, string sortBy, int page, CancellationToken cancellationToken)
        {
            var sortParam = GetSortByParameter(mediaType, sortBy);
            var requestUri = $"discover/{mediaType}?api_key={_config.ApiKey}&language=en-US&sort_by={sortParam}&include_adult=false&include_video=false&page={page}&watch_region={countryCode}&with_watch_providers={providerId}&with_watch_monetization_types=flatrate";

            await RateLimiter.WaitAsync(cancellationToken);
            try
            {
                // A new request message must be created for each attempt, especially with retries.
                var response = await _retryPolicy.ExecuteAsync(() => _httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, requestUri), cancellationToken));

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.Error("TMDB API request failed with status code {0}. URI: {1}. Body: {2}", response.StatusCode, requestUri, errorBody);
                    return null;
                }

                await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var result = await JsonSerializer.DeserializeAsync<TmdbPagedResult<TmdbItem>>(contentStream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);
                return result;
            }
            finally
            {
                // We delay *after* the operation to ensure a minimum time between the starts of two consecutive operations.
                await Task.Delay(DelayBetweenRequestsMs, cancellationToken);
                RateLimiter.Release();
            }
        }

        public async Task<List<int>> FetchFlatrateProviderIdsAsync(string mediaType, int tmdbId, string region, CancellationToken ct)
        {
            // Beispiel-URL: /movie/{id}/watch/providers
            var url = $"{BaseUrl}{mediaType}/{tmdbId}/watch/providers?api_key={_config.ApiKey}";

            await RateLimiter.WaitAsync(ct);
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                using var res = await _retryPolicy.ExecuteAsync(() => _httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, url), ct));

                if (!res.IsSuccessStatusCode)
                {
                    // Log error but don't throw, as the auto-detector will just skip this item.
                    _logger.Warn("Failed to get watch providers for {0} ID {1}. Status: {2}", mediaType, tmdbId, res.StatusCode);
                    return new List<int>();
                }

                var json = await res.Content.ReadAsStringAsync(ct);
                var data = JsonSerializer.Deserialize<TmdbWatchProviderResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (data?.Results == null) return new List<int>();
                if (!data.Results.TryGetValue(region.ToUpperInvariant(), out var regionData) || regionData?.Flatrate == null)
                    return new List<int>();

                var ids = regionData.Flatrate.Select(p => p.ProviderId);
                return new HashSet<int>(ids).ToList();
            }
            finally
            {
                await Task.Delay(DelayBetweenRequestsMs, ct);
                RateLimiter.Release();
            }
        }

        private static string GetSortByParameter(string mediaType, string sortBy)
        {
            return sortBy?.ToLowerInvariant() switch
            {
                "new" => mediaType == "tv" ? "first_air_date.desc" : "primary_release_date.desc",
                "trending" => "popularity.desc",
                "popular" => "popularity.desc", // For TMDB, "popular" is the main sorting criteria
                _ => "popularity.desc",
            };
        }
    }
}