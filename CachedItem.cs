namespace StreamingCatalogs.Data
{
    public class CachedItem
    {
        public int TmdbId { get; set; }
        public string ProviderId { get; set; } = string.Empty;
        public string CountryCode { get; set; } = string.Empty;
        public string MediaType { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Overview { get; set; }
        public string? PosterPath { get; set; }
        public string? BackdropPath { get; set; }
        public int? ReleaseYear { get; set; }
        public double Popularity { get; set; }
        public bool IsInLibrary { get; set; }
        public string LastUpdatedUtc { get; set; } = string.Empty;
    }
}