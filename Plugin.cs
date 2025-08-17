using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Tasks;
using StreamingCatalogs.Services;
using Polly;
using System.Threading;

namespace StreamingCatalogs
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        // Forces default providers (UI hooks are omitted)
        public bool AlwaysEnableDefaultProviders { get; set; } = true;

        // Pipe-list of TMDB provider IDs, e.g. Netflix|Prime|Disney+ => "8|9|337"
        public string DefaultProviderIds { get; set; } = "8|9|337";

        // Region for watch providers
        public string DefaultWatchRegion { get; set; } = "CH";

        // Sorting (optional)
        public string DefaultSortBy { get; set; } = "trending";

        // TMDB API-Key (already existed)
        public string ApiKey { get; set; } = string.Empty;

        // NEW: Automatically determine from library
        public bool AutoDetectProvidersFromLibrary { get; set; } = true;

        // NEW: Limits for auto-scan (requests/performance)
        public int AutoDetectSampleSize { get; set; } = 500; // max. items for sampling
        public int AutoDetectTopN { get; set; } = 6;         // e.g. Top 6 providers

        // Used by controller, not on UI, but needed for compilation
        public int MaxItemsPerCatalog { get; set; } = 48;
    }

    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages, IServerEntryPoint
    {
        public static Plugin? Instance { get; private set; }

        // Public services for access from other parts of the plugin, like scheduled tasks.
        public TmdbService? TmdbService { get; private set; }
        public LibraryMatchingService? LibraryMatchingService { get; private set; }
        public ProviderAutoDetector? AutoDetector { get; private set; }
        public CacheService? CacheService { get; private set; }

        private ILogger? _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly ILogManager _logManager;
        private readonly IServerConfigurationManager _serverConfigurationManager;

        // Inject core Emby services that are guaranteed to be available
        public Plugin(IApplicationPaths applicationPaths,
                      IXmlSerializer xmlSerializer,
                      ILogManager logManager,
                      ILibraryManager libraryManager,
                      IServerConfigurationManager serverConfigurationManager)
            : base(applicationPaths, xmlSerializer)
        {
            // Dummy reference to Polly to ensure it's copied to the plugins directory.
            _ = Policy.Handle<Exception>().Retry(1);

            Instance = this;
            // Store injected services, but defer initialization of our own services to the Run method
            // to avoid issues with the application not being fully initialized yet.
            _logManager = logManager;
            _libraryManager = libraryManager;
            _serverConfigurationManager = serverConfigurationManager;
        }

        public override Guid Id => new Guid("6209a112-8c4a-4977-bb1b-75d649f9e256");
        public override string Name => "StreamingCatalogs";
        public override string Description => "Konfiguration für Streaming-Kataloge";

        public IEnumerable<PluginPageInfo> GetPages() => new[]
        {
            new PluginPageInfo
            {
                Name = "StreamingCatalogs",
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.catalog.html",
                // MenuSection = "MyMedia", // By removing this, it becomes a top-level menu item.
                MenuIcon = "theaters",
                DisplayName = "Streaming"
            },
            // Configuration Page
            new PluginPageInfo
            {
                Name = "streamingcatalogs_config",
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.ui.html",
            }
        };

        public void Run()
        {
            // It's safer to initialize services here, as the application is fully loaded.
            _logger = _logManager.GetLogger(GetType().Name);

            // Create HttpClient manually to avoid DI issues with IHttpClientFactory
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10)
            };
            var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

            // Construct and expose our services
            TmdbService = new TmdbService(_logManager, httpClient, this.Configuration);
            LibraryMatchingService = new LibraryMatchingService(_logManager, _libraryManager);
            CacheService = new CacheService(_logManager, _serverConfigurationManager);
            AutoDetector = new ProviderAutoDetector(_logManager, LibraryMatchingService, TmdbService);

            var cfg = Configuration;

            if (cfg.AutoDetectProvidersFromLibrary)
            {
                _ = Task.Run(async () =>
                {
                    if (_logger is null || AutoDetector is null) return;
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                        _logger.Info("Starting automatic detection of streaming providers from library on startup.");
                        var pipe = await AutoDetector.DetectAsync(cfg, cts.Token);
                        if (!string.IsNullOrWhiteSpace(pipe) && pipe != cfg.DefaultProviderIds)
                        {
                            _logger.Info("New provider list detected. Old: {0}, New: {1}. Updating configuration.", cfg.DefaultProviderIds, pipe);
                            cfg.DefaultProviderIds = pipe;
                            UpdateConfiguration(cfg);
                        }
                        else
                        {
                            _logger.Info("Auto-detected providers match current configuration or no new providers found. No update needed.");
                        }
                    }
                    catch (OperationCanceledException) { _logger?.Warn("Auto-detection of providers timed out."); }
                    catch (Exception ex) { _logger?.ErrorException("Auto-detect providers failed.", ex); }
                });
            }
        }

        public void Dispose() => Instance = null;
    }
}