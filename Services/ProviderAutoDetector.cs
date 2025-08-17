using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Logging;

namespace StreamingCatalogs.Services
{
    /// <summary>
    /// Detects streaming providers based on the content in the user's library.
    /// </summary>
    public class ProviderAutoDetector
    {
        private readonly ILogger _log;
        private readonly LibraryMatchingService? _lib;
        private readonly TmdbService? _tmdb;

        // DI-Konstruktor
        public ProviderAutoDetector(
            ILogManager logManager,
            LibraryMatchingService lib,
            TmdbService tmdb)
        {
            _log  = logManager.GetLogger(GetType().Name);
            _lib  = lib;
            _tmdb = tmdb;
        }

        /// <summary>
        /// Liefert eine Pipe-getrennte Liste von TMDB-Provider-IDs.
        /// Wenn Auto-Detection nicht möglich ist, werden die Defaults aus der Config zurückgegeben.
        /// </summary>
        public async Task<string> DetectAsync(PluginConfiguration cfg, CancellationToken ct)
        {
            // Fallback, falls kein DI vorhanden oder TMDB/Library nicht verdrahtet:
            if (_lib == null || _tmdb == null)
                return cfg?.DefaultProviderIds ?? "8|9|337";

            var region = string.IsNullOrWhiteSpace(cfg.DefaultWatchRegion) ? "CH" : cfg.DefaultWatchRegion;

            _log.Info("Starting auto-detection of streaming providers from library.");
            var tmdbIds = _lib.GetAllTmdbIdsInLibrary(ct).ToList();

            if (!tmdbIds.Any())
            {
                _log.Info("Auto-Detect: No TMDB IDs found in library. Keeping default providers: {0}", cfg.DefaultProviderIds);
                return cfg.DefaultProviderIds;
            }

            var counts = new Dictionary<int,int>();
            foreach (var id in tmdbIds.Take(Math.Max(10, Math.Min(cfg.AutoDetectSampleSize, 500))))
            {
                ct.ThrowIfCancellationRequested();

                foreach (var type in new[] { "movie", "tv" })
                {
                    try
                    {
                        var provs = await _tmdb.FetchFlatrateProviderIdsAsync(type, id, region, ct);
                        if (provs != null)
                        {
                            foreach (var p in provs)
                                counts[p] = (counts.TryGetValue(p, out var c) ? c : 0) + 1;
                            break; // ein Medientyp genügt
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log the error but continue to the next item/type.
                        // This prevents a single failed lookup from stopping the entire detection process.
                        _log.Warn("Could not fetch providers for TMDB ID {0} (type: {1}): {2}", id, type, ex.Message);
                    }
                }
            }

            if (counts.Count == 0)
            {
                _log.Info("Auto-Detect: Could not find any streaming providers for the library's content in region {0}. Keeping default providers: {1}", region, cfg.DefaultProviderIds);
                return cfg.DefaultProviderIds;
            }

            var top = counts.OrderByDescending(kv => kv.Value)
                            .Take(Math.Max(3, Math.Min(cfg.AutoDetectTopN, 10)))
                            .Select(kv => kv.Key.ToString());

            var pipe = string.Join("|", top);
            _log.Info("Auto-Detect: Detected top providers for region {0}: {1}", region, pipe);
            return pipe;
        }
    }
}