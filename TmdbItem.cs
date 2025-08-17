using System.Text.Json.Serialization;

namespace StreamingCatalogs.Services.Tmdb.Dto
{
    /// <summary>
    /// Represents a single movie or TV show item from the TMDB API.
    /// </summary>
    public class TmdbItem
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; } // For movies

        [JsonPropertyName("name")]
        public string? Name { get; set; } // For TV shows

        [JsonPropertyName("overview")]
        public string? Overview { get; set; }

        [JsonPropertyName("poster_path")]
        public string? PosterPath { get; set; }

        [JsonPropertyName("backdrop_path")]
        public string? BackdropPath { get; set; }

        [JsonPropertyName("popularity")]
        public double Popularity { get; set; }

        [JsonPropertyName("release_date")]
        public string? ReleaseDate { get; set; } // For movies

        [JsonPropertyName("first_air_date")]
        public string? FirstAirDate { get; set; } // For TV shows
    }
}