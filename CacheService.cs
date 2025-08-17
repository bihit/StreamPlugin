using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using MediaBrowser.Model.Logging;
using ProviderInfo = StreamingCatalogs.Data.ProviderInfo;
using StreamingCatalogs.Data;
using StreamingCatalogs.Services.Tmdb.Dto;

namespace StreamingCatalogs.Services
{
    public class CacheService
    {
        private readonly ILogger _logger;
        private readonly string _dbPath;

        public CacheService(ILogManager logger, MediaBrowser.Controller.Configuration.IServerConfigurationManager serverConfigurationManager)
        {
            _logger = logger.GetLogger(GetType().Name);
            var pluginDataPath = Path.Combine(serverConfigurationManager.ApplicationPaths.PluginConfigurationsPath, "StreamingCatalogs");
            Directory.CreateDirectory(pluginDataPath); // Ensure the directory exists
            _dbPath = Path.Combine(pluginDataPath, "cache.db");
        }

        private SqliteConnection CreateConnection() => new SqliteConnection($"Data Source={_dbPath}");

        public async Task InitializeAsync()
        {
            _logger.Info("Initializing streaming catalog cache database at {0}", _dbPath);
            using var connection = CreateConnection();
            
            var sql = @"
                CREATE TABLE IF NOT EXISTS CatalogItems (
                    TmdbId              INTEGER NOT NULL,
                    ProviderId          TEXT    NOT NULL,
                    CountryCode         TEXT    NOT NULL,
                    MediaType           TEXT    NOT NULL,
                    Title               TEXT,
                    Overview            TEXT,
                    PosterPath          TEXT,
                    BackdropPath        TEXT,
                    ReleaseYear         INTEGER,
                    Popularity          REAL,
                    IsInLibrary         INTEGER NOT NULL DEFAULT 0,
                    LastUpdatedUtc      TEXT    NOT NULL,
                    PRIMARY KEY (TmdbId, ProviderId, CountryCode)
                );
                CREATE INDEX IF NOT EXISTS IX_CatalogItems_Provider_Country_Media ON CatalogItems (ProviderId, CountryCode, MediaType);
            ";
            
            await connection.ExecuteAsync(sql);
        }

        public async Task SaveCatalogItemsAsync(string providerId, string countryCode, string mediaType, IEnumerable<TmdbItem> items)
        {
            var now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            var catalogItems = items.Select(item => new CatalogItem
            {
                TmdbId = item.Id,
                ProviderId = providerId,
                CountryCode = countryCode,
                MediaType = mediaType,
                Title = (mediaType == "tv" ? item.Name : item.Title) ?? string.Empty,
                Overview = item.Overview,
                PosterPath = item.PosterPath,
                BackdropPath = item.BackdropPath,
                ReleaseYear = GetYearFromDateString(mediaType == "tv" ? item.FirstAirDate : item.ReleaseDate),
                Popularity = item.Popularity,
                IsInLibrary = false, // This will be updated later in a separate step
                LastUpdatedUtc = now
            }).ToList();

            if (!catalogItems.Any()) return;

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                // Atomically delete old items for this specific catalog and insert new ones
                var deleteSql = "DELETE FROM CatalogItems WHERE ProviderId = @ProviderId AND CountryCode = @CountryCode AND MediaType = @MediaType;";
                await connection.ExecuteAsync(deleteSql, new { ProviderId = providerId, CountryCode = countryCode, MediaType = mediaType }, transaction);

                var insertSql = @"
                    INSERT INTO CatalogItems (TmdbId, ProviderId, CountryCode, MediaType, Title, Overview, PosterPath, BackdropPath, ReleaseYear, Popularity, IsInLibrary, LastUpdatedUtc)
                    VALUES (@TmdbId, @ProviderId, @CountryCode, @MediaType, @Title, @Overview, @PosterPath, @BackdropPath, @ReleaseYear, @Popularity, @IsInLibrary, @LastUpdatedUtc);
                ";
                await connection.ExecuteAsync(insertSql, catalogItems, transaction);

                transaction.Commit();
                _logger.Info("Successfully cached {0} {1} items for provider {2} in {3}.", catalogItems.Count, mediaType, providerId, countryCode);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Failed to save catalog items to cache. Rolling back transaction.", ex);
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<IEnumerable<CatalogItem>> GetItems(string mediaType, string providerId, string countryCode, int limit, string sort)
        {
            using var connection = CreateConnection();
            var sqlBuilder = new StringBuilder("SELECT * FROM CatalogItems WHERE ProviderId = @ProviderId AND CountryCode = @CountryCode AND MediaType = @MediaType ");

            sqlBuilder.Append(sort?.ToLowerInvariant() switch
            {
                "new" => "ORDER BY ReleaseYear DESC, Popularity DESC ",
                _ => "ORDER BY Popularity DESC ",
            });

            sqlBuilder.Append("LIMIT @Limit;");

            return await connection.QueryAsync<CatalogItem>(sqlBuilder.ToString(), new { ProviderId = providerId, CountryCode = countryCode, MediaType = mediaType, Limit = limit });
        }

        public async Task<IEnumerable<ProviderInfo>> GetAvailableProviders(string countryCode)
        {
            using var connection = CreateConnection();
            var sql = @"
                SELECT ProviderId, COUNT(TmdbId) as ItemCount
                FROM CatalogItems
                WHERE CountryCode = @CountryCode
                GROUP BY ProviderId
                HAVING ItemCount > 0
                ORDER BY ProviderId;
            ";
            return await connection.QueryAsync<ProviderInfo>(sql, new { CountryCode = countryCode });
        }

        public async Task UpdateLibraryStatusAsync(Dictionary<int, bool> libraryStatus)
        {
            if (libraryStatus == null || !libraryStatus.Any())
            {
                return;
            }

            _logger.Info("Updating 'IsInLibrary' status for {0} items in the cache.", libraryStatus.Count);

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            var sql = "UPDATE CatalogItems SET IsInLibrary = @IsInLibrary WHERE TmdbId = @TmdbId;";

            // Dapper can execute this against a list of objects for a bulk update
            var updateParams = libraryStatus.Select(kvp => new { TmdbId = kvp.Key, IsInLibrary = kvp.Value });

            await connection.ExecuteAsync(sql, updateParams, transaction);

            transaction.Commit();
        }

        public async Task<IEnumerable<CatalogItem>> GetAllCachedItems()
        {
            using var connection = CreateConnection();
            // Return all items for grouping in the API.
            return await connection.QueryAsync<CatalogItem>("SELECT * FROM CatalogItems;");
        }

        public async Task<IEnumerable<int>> GetDistinctTmdbIdsFromCache()
        {
            using var connection = CreateConnection();
            return await connection.QueryAsync<int>("SELECT DISTINCT TmdbId FROM CatalogItems;");
        }

        private static int? GetYearFromDateString(string? date)
        {
            if (string.IsNullOrEmpty(date) || date.Length < 4)
            {
                return null;
            }

            if (int.TryParse(date.AsSpan(0, 4), out var year))
            {
                return year;
            }

            return null;
        }
    }
}