using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace StreamingCatalogs.Services.Tmdb.Dto
{
    /// <summary>
    /// Represents a paginated result from the TMDB API.
    /// </summary>
    public class TmdbPagedResult<T>
    {
        [JsonPropertyName("page")]
        public int Page { get; set; }

        [JsonPropertyName("results")]
        public List<T> Results { get; set; } = new();

        [JsonPropertyName("total_pages")]
        public int TotalPages { get; set; }

        [JsonPropertyName("total_results")]
        public int TotalResults { get; set; }
    }
}