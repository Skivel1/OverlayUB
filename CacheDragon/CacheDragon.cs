using LightWeightOverlay;
using LightWeightOverlay.Applications;
using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WatsonWebserver;

namespace CacheDragon
{
    public class Dragon : AApplication
    {
        private static readonly string _cDragonBaseUrl = "https://ddragon.leagueoflegends.com/cdn/";
        private static readonly string _routePrefix = "/assets/cdragon/";
        private static readonly Regex _cdragonRegex = new Regex($"^{Regex.Escape(_routePrefix)}");
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        private async Task CDragonHandler(HttpContext ctx)
        {
            try
            {
                string rawPath = ctx.Request.RawUrlWithoutQuery ?? string.Empty;
                string requested = rawPath.StartsWith(_routePrefix, StringComparison.OrdinalIgnoreCase)
                    ? rawPath.Substring(_routePrefix.Length)
                    : rawPath.Replace(_routePrefix, string.Empty);

                requested = requested.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);

                if (string.IsNullOrWhiteSpace(requested))
                {
                    ctx.Response.StatusCode = 400;
                    await ctx.Response.Send("Invalid asset path");
                    return;
                }

                string cacheRoot = Path.GetFullPath(Path.Combine(GetApplicationPath(), "cache"));
                string cacheUri = Path.GetFullPath(Path.Combine(cacheRoot, requested));

                // Previene acceso fuera del directorio cache
                if (!cacheUri.StartsWith(cacheRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Response.StatusCode = 403;
                    await ctx.Response.Send("Forbidden");
                    return;
                }

                string? directoryPath = Path.GetDirectoryName(cacheUri);
                if (!string.IsNullOrEmpty(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                if (!File.Exists(cacheUri))
                {
                    string remotePath = requested.Replace(Path.DirectorySeparatorChar, '/');
                    string url = _cDragonBaseUrl + remotePath;

                    using HttpResponseMessage response = await _httpClient.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"[CacheDragon] Failed ({(int)response.StatusCode}): {url}");
                        ctx.Response.StatusCode = (int)response.StatusCode;
                        await ctx.Response.Send($"Failed to fetch asset: {response.StatusCode}");
                        return;
                    }

                    byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                    string tempFile = cacheUri + ".tmp";
                    await File.WriteAllBytesAsync(tempFile, bytes);

                    if (File.Exists(cacheUri))
                    {
                        File.Delete(cacheUri);
                    }
                    File.Move(tempFile, cacheUri);

                    Console.WriteLine($"[CacheDragon] Downloaded {url}");
                }

                byte[] fileBytes = await File.ReadAllBytesAsync(cacheUri);
                await ctx.Response.Send(fileBytes);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[CacheDragon] Error: {e}");
                ctx.Response.StatusCode = 500;
                await ctx.Response.Send("CacheDragon internal error");
            }
        }

        public override string GetName()
        {
            return "cache_dragon";
        }

        public override void Load(LWOServer s)
        {
            _server = s;
            _server.WebServer.DynamicRoutes.Add(WatsonWebserver.HttpMethod.GET, _cdragonRegex, CDragonHandler);
            Console.WriteLine($"[CacheDragon] {_server.GetWebUrl(_routePrefix)}");
        }

        public override void Start()
        {
        }

        public override string StatusHtml()
        {
            return $"<p>CacheDragon active on <code>{_routePrefix}</code></p>";
        }

        public override string StatusString()
        {
            return $"CacheDragon active on {_routePrefix}";
        }

        public override void Stop()
        {
        }

        public override void Unload()
        {
            _server?.WebServer?.DynamicRoutes?.Remove(WatsonWebserver.HttpMethod.GET, _cdragonRegex);
        }
    }
}