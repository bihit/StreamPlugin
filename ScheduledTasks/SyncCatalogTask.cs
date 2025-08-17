// ScheduledTasks/SyncCatalogTask.cs
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;
using StreamingCatalogs.Data;
using StreamingCatalogs.Services;

namespace StreamingCatalogs
{
    public class SyncCatalogTask : IScheduledTask, IConfigurableScheduledTask
    {
        private readonly ILogger _logger;
        private readonly Plugin _plugin;

        public string Name => "Sync Streaming Catalogs";
        public string Key => "SyncStreamingCatalogs";
        public string Description => "Lädt Kataloge (TMDB/Provider) und aktualisiert den Cache.";
        public string Category => "StreamingCatalogs";

        // Inject the plugin instance directly to avoid static singleton issues.
        public SyncCatalogTask(ILogManager logManager, Plugin plugin)
        {
            _logger = logManager.GetLogger(GetType().Name);
            _plugin = plugin;
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => new[]
        {
            new TaskTriggerInfo { Type = "DailyTrigger", TimeOfDayTicks = TimeSpan.FromHours(3).Ticks }
        };

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.Info("Sync Streaming Catalogs task started.");

            // Use the injected plugin instance.
            var plugin = _plugin;
            int retries = 0;
            const int maxRetries = 12; // Wait for up to 60 seconds (12 * 5s)

            // It's possible for the task to run before the plugin's Run() method has completed.
            // We'll wait a short period for the services to become available.
            while ((plugin.AutoDetector is null || plugin.TmdbService is null || plugin.CacheService is null || plugin.LibraryMatchingService is null) && retries < maxRetries)
            {
                retries++;
                _logger.Debug("Plugin services not ready yet. Waiting 5 seconds... (Attempt {0}/{1})", retries, maxRetries);
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }

            if (plugin.AutoDetector is null || plugin.TmdbService is null || plugin.CacheService is null || plugin.LibraryMatchingService is null)
            {
                _logger.Warn("Plugin instance or services not available after waiting. Skipping task. This can happen if the plugin is disabled or failed to start.");
                return;
            }

            var autoDetector = plugin.AutoDetector;
            var tmdb = plugin.TmdbService;
            var cache = plugin.CacheService;
            var library = plugin.LibraryMatchingService;
            var configuration = plugin.Configuration;

            // Step 1: Auto-detect providers if enabled
            if (configuration.AutoDetectProvidersFromLibrary)
            {
                progress.Report(5);
                _logger.Info("Running auto-detection of providers as part of scheduled task.");
                try
                {
                    var pipe = await autoDetector.DetectAsync(configuration, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(pipe) && pipe != configuration.DefaultProviderIds)
                    {
                        _logger.Info("Sync task detected new provider list. Old: {0}, New: {1}. Updating configuration.", configuration.DefaultProviderIds, pipe);
                        configuration.DefaultProviderIds = pipe;
                        plugin.UpdateConfiguration(configuration);
                    }
                    else
                    {
                        _logger.Info("Auto-detected providers match current configuration or no new providers found. No update needed.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Auto-detect providers failed during scheduled sync.", ex);
                }
            }

            // Step 2: Sync catalogs for each provider
            var providers = configuration.DefaultProviderIds?.Split('|', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            var totalProgress = providers.Length * 2; // 2 media types per provider
            var currentProgress = 0;

            foreach (var providerId in providers)
            {
                foreach (var mediaType in new[] { "movie", "tv" })
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var progressValue = 20 + (60.0 * ++currentProgress / totalProgress);
                    progress.Report(progressValue);

                    _logger.Info("Fetching catalog for provider {0}, type {1}", providerId, mediaType);
                    var items = await tmdb.GetCatalogItemsAsync(mediaType, providerId, configuration.DefaultWatchRegion, configuration.MaxItemsPerCatalog, configuration.DefaultSortBy, cancellationToken);
                    await cache.SaveCatalogItemsAsync(providerId, configuration.DefaultWatchRegion, mediaType, items);
                }
            }

            // Step 3: Update library status for all cached items
            progress.Report(85);
            _logger.Info("Updating library status for all cached items.");
            var allTmdbIds = await cache.GetDistinctTmdbIdsFromCache();

            var libraryStatus = library.GetLibraryStatusForTmdbIds(allTmdbIds, cancellationToken);
            await cache.UpdateLibraryStatusAsync(libraryStatus);

            _logger.Info("Sync Streaming Catalogs task finished.");
            progress.Report(100.0);
        }

        public bool IsHidden => false;
        public bool IsEnabled => true;
        public bool IsLogged  => true;
    }
}