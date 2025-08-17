using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;

namespace StreamingCatalogs.Services
{
    public class LibraryMatchingService
    {
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;

        public LibraryMatchingService(ILogManager logManager, ILibraryManager libraryManager)
        {
            _logger = logManager.GetLogger(GetType().Name);
            _libraryManager = libraryManager;
        }

        public Dictionary<int, bool> GetLibraryStatusForTmdbIds(IEnumerable<int> tmdbIds, CancellationToken cancellationToken)
        {
            var tmdbIdList = tmdbIds.ToList();
            _logger.Info("Checking library status for {0} TMDB IDs.", tmdbIdList.Count);

            var results = tmdbIdList.ToDictionary(id => id, id => false);
            if (results.Count == 0)
            {
                return results;
            }

            // This is much more efficient than loading all items. We query only for items that have one of the desired TMDB IDs.
            var query = new InternalItemsQuery
            {
                AnyProviderIdEquals = tmdbIdList.Select(id => new KeyValuePair<string, string>("Tmdb", id.ToString())).ToList(),
                EnableTotalRecordCount = false,
                IncludeItemTypes = new[] { "Movie", "Series" } // Further optimization
            };

            var itemsInLibrary = _libraryManager.GetItemList(query);

            foreach (var item in itemsInLibrary)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var idStr = item.GetProviderId("Tmdb");
                if (!string.IsNullOrEmpty(idStr) && int.TryParse(idStr, out var tmdbId) && results.ContainsKey(tmdbId))
                {
                    results[tmdbId] = true;
                }
            }

            _logger.Info(
                "Found {0} of {1} items in the library.",
                results.Count(kvp => kvp.Value),
                results.Count
            );
            return results;
        }

        public IEnumerable<int> GetAllTmdbIdsInLibrary(CancellationToken cancellationToken)
        {
            var query = new InternalItemsQuery
            {
                EnableTotalRecordCount = false,
                IncludeItemTypes = new[] { "Movie", "Series" } // Optimization: only scan relevant types
            };
            var items = _libraryManager.GetItemList(query);

            _logger.Info("Scanning {0} library items for TMDB IDs.", items.Count());

            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = item.GetProviderId("Tmdb");
                if (!string.IsNullOrEmpty(id) && int.TryParse(id, out var tmdbId))
                {
                    yield return tmdbId;
                }
            }
        }
    }
}