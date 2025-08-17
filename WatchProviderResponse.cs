using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace StreamingCatalogs.Services.Tmdb.Dto
{
    public class WatchProviderResponse
    {
        [JsonPropertyName("results")]
        public Dictionary<string, WatchProviderRegionDetails> Results { get; set; } = new();
    }

    public class WatchProviderRegionDetails
    {
        [JsonPropertyName("flatrate")]
        public List<WatchProviderInfo>? Flatrate { get; set; }
    }

    public class WatchProviderInfo
    {
        [JsonPropertyName("provider_id")]
        public int ProviderId { get; set; }
    }
}