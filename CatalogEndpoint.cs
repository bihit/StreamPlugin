using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;
using StreamingCatalogs.Data;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StreamingCatalogs.Api
{
    // Definiert die API-Route. Die Anfrage selbst benötigt keine Parameter.
    [Route("/StreamingCatalogs/Catalogs", "GET", Summary = "Ruft alle zwischengespeicherten Streaming-Kataloge ab")]
    public class GetCatalogs : IReturn<Dictionary<string, Dictionary<string, List<CatalogItem>>>>
    {
    }

    // Die Implementierung des API-Dienstes
    public class CatalogEndpoint : IService
    {
        // Die Methode wird für GET-Anfragen auf die oben definierte Route aufgerufen.
        public async Task<object> Get(GetCatalogs request)
        {
            var plugin = Plugin.Instance;
            if (plugin?.CacheService == null)
            {
                // Wenn der Cache-Service nicht bereit ist, ein leeres Objekt zurückgeben.
                return new Dictionary<string, Dictionary<string, List<CatalogItem>>>();
            }

            var allItems = await plugin.CacheService.GetAllCachedItems();

            // Gruppiert die Elemente zuerst nach Anbieter, dann nach Medientyp (Filme/Serien)
            var grouped = allItems
                .GroupBy(i => i.ProviderId)
                .ToDictionary(
                    providerGroup => providerGroup.Key,
                    providerGroup => providerGroup
                        .GroupBy(i => i.MediaType)
                        .ToDictionary(mediaTypeGroup => mediaTypeGroup.Key, mediaTypeGroup => mediaTypeGroup.ToList()));

            return grouped;
        }
    }
}