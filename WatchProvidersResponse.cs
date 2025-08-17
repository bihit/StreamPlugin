using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace StreamingCatalogs.Services.Tmdb.Dto
{
    /// <summary>
    /// Models the top-level response from the TMDB /watch/providers endpoint.
    /// </summary>
    public class TmdbWatchProviderResponse
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("results")]
        public Dictionary<string, TmdbRegionProviders> Results { get; set; } = new();
    }

    public class TmdbRegionProviders
    {
        [JsonPropertyName("link")]
        public string? Link { get; set; }

        [JsonPropertyName("flatrate")]
        public List<TmdbProviderInfo>? Flatrate { get; set; }
    }

    public class TmdbProviderInfo
    {
        [JsonPropertyName("provider_id")]
        public int ProviderId { get; set; }
    }
}