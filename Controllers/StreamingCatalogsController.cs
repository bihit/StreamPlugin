using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;
using StreamingCatalogs.Data;
using StreamingCatalogs.Services;

namespace StreamingCatalogs.Controllers
{
    [Route("/StreamingCatalogs/Items", "GET", Summary = "Gets items from a specific streaming catalog")]
    public class GetCatalogItems : IReturn<List<CachedItem>>
    {
        [ApiMember(Name = "ProviderId", Description = "The TMDB provider ID", IsRequired = true, DataType = "string", ParameterType = "query")]
        public string? ProviderId { get; set; }

        [ApiMember(Name = "MediaType", Description = "The media type (movie or tv)", IsRequired = true, DataType = "string", ParameterType = "query")]
        public string? MediaType { get; set; }
    }

    public class StreamingCatalogsController : IService
    {
        private readonly ILogger _logger;
        private readonly CacheService _cacheService;
        private readonly PluginConfiguration _config;

        public StreamingCatalogsController(ILogManager logManager, IServerConfigurationManager serverConfig)
        {
            _logger = logManager.GetLogger(GetType().Name);
            _cacheService = new CacheService(logManager, serverConfig);
            _config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        }

        public async Task<object> Get(GetCatalogItems request)
        {
            if (string.IsNullOrEmpty(request.ProviderId) || string.IsNullOrEmpty(request.MediaType))
            {
                _logger.Warn("GetCatalogItems request is missing ProviderId or MediaType.");
                return new List<CachedItem>();
            }

            try
            {
                var items = await _cacheService.GetItems(
                    request.MediaType,
                    request.ProviderId,
                    _config.DefaultWatchRegion,
                    _config.MaxItemsPerCatalog,
                    _config.DefaultSortBy);
                return items.ToList();
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Error getting catalog items from cache for provider {0}", ex, request.ProviderId);
                return new List<CachedItem>();
            }
        }
    }
}