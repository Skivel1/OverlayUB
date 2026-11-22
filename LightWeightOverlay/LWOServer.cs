using LightWeightOverlay.Applications;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using WatsonWebserver;
using WatsonWebsocket;

namespace LightWeightOverlay
{
    public class LWOServer
    {
        public List<AApplication> Applications { get; set; } = new List<AApplication>();

        public Server WebServer { get; set; }
        public WatsonWsServer WebSocket { get; set; }
        public AppConfiguration Config { get; set; }

        public Dictionary<String, List<String>> Subscriptions { get; set; } = new Dictionary<string, List<string>>();
        public SharedState State { get; set; }

        public String AppDirectory = "apps";

        public string GetWebUrl(string path)
        {
            var host = (Config.IP == "0.0.0.0" || Config.IP == "*" || Config.IP == "+")
                ? "127.0.0.1"
                : Config.IP;
            var route = path.StartsWith("/", StringComparison.Ordinal)
                ? path
                : "/" + path;
            return $"http://{host}:{Config.WebserverPort}{route}";
        }

        public void PrintWebLinks()
        {
            Console.WriteLine("Web links:");
            Console.WriteLine($"  Admin:   {GetWebUrl("/lol_picksandbans/admin.html")}");
            Console.WriteLine($"  Overlay: {GetWebUrl("/lol_picksandbans/overlay.html")}");
            Console.WriteLine($"  Cache:   {GetWebUrl("/assets/cdragon/")}");
        }

        private static string ResolveInputPath(string path)
        {
            if (Path.IsPathRooted(path))
                return path;

            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, path),
                Path.Combine(Directory.GetCurrentDirectory(), path)
            };

            var resolved = candidates.FirstOrDefault(File.Exists);
            return resolved ?? candidates[0];
        }

        private static string ResolveAppDirectory()
        {
            var candidates = new List<string>();

            foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                var directory = new DirectoryInfo(start);

                while (directory != null)
                {
                    candidates.Add(Path.Combine(directory.FullName, "apps"));
                    candidates.Add(Path.Combine(directory.FullName, "bin", "apps"));
                    directory = directory.Parent;
                }
            }

            var resolved = candidates.FirstOrDefault(Directory.Exists);
            return resolved ?? Path.Combine(AppContext.BaseDirectory, "apps");
        }

        public LWOServer(String settingsPath, String statePath)
        {
            settingsPath = ResolveInputPath(settingsPath);
            statePath = ResolveInputPath(statePath);

            Config = JsonConvert.DeserializeObject<AppConfiguration>(File.ReadAllText(settingsPath));

            // Overrides por variable de entorno (Docker / Portainer)
            var envIp = Environment.GetEnvironmentVariable("LWO_IP");
            if (!string.IsNullOrWhiteSpace(envIp)) Config.IP = envIp;
            if (int.TryParse(Environment.GetEnvironmentVariable("LWO_WEB_PORT"), out var envWeb)) Config.WebserverPort = envWeb;
            if (int.TryParse(Environment.GetEnvironmentVariable("LWO_WS_PORT"), out var envWs)) Config.WebsocketPort = envWs;

            State = SharedState.FromJson(statePath);

            WebServer = new Server(Config.IP, Config.WebserverPort, false, defaultRoute);
            WebSocket = new WatsonWsServer(Config.IP, Config.WebsocketPort, false);

            WebSocket.MessageReceived += WsMessageReceived;
            WebSocket.ClientConnected += WsClientConnected;
            WebSocket.ClientDisconnected += WsClientDisconnected;

            WebSocket.Start();

            WebServer.ContentRoutes.Add("admin.html", false);


            AppDirectory = ResolveAppDirectory();
            var plugins = Directory.Exists(AppDirectory)
                ? Directory.GetDirectories(AppDirectory)
                : Array.Empty<string>();
            var ti = typeof(AApplication);

            foreach (var plugin in plugins)
            {
                var pFolder = Path.GetFileName(plugin.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var dllName = Path.Combine(plugin, pFolder + ".dll");
                if (!File.Exists(dllName))
                {
                    Console.WriteLine($"[Plugin] Skipping '{pFolder}' because '{pFolder}.dll' was not found.");
                    continue;
                }

                var dll = Assembly.LoadFrom(dllName);

                foreach (Type type in dll.GetExportedTypes().Where(x => ti.IsAssignableFrom(x) && !x.IsAbstract))
                {
                    AApplication c = Activator.CreateInstance(type) as AApplication;
                    if (c != null)
                    {
                        Applications.Add(c);
                        c.Load(this);
                    }
                }

            }

        }

        public async Task<bool> WsClientConnected(string arg1, HttpListenerRequest arg2)
        {
            // Si LWO_TOKEN esta definido, todo cliente WebSocket (overlay, admin, agente) debe enviar ?token=...
            var expected = Environment.GetEnvironmentVariable("LWO_TOKEN");
            if (string.IsNullOrEmpty(expected)) return true;

            var query = arg2?.Url?.Query ?? string.Empty;
            var token = System.Web.HttpUtility.ParseQueryString(query)["token"];
            var ok = string.Equals(token, expected, StringComparison.Ordinal);
            if (!ok) Console.WriteLine($"[WS] Conexion rechazada de {arg1}: token invalido");
            return ok;
        }

        public async Task WsClientDisconnected(string arg)
        {
            foreach (var type in Subscriptions.Keys)
            {
                var list = Subscriptions[type];
                if (list.Contains(arg)) list.Remove(arg);
            }
        }

        public void AddSubscription(String client, String subscription)
        {
            if (!Subscriptions.ContainsKey(subscription))
                Subscriptions[subscription] = new List<string>();

            Subscriptions[subscription].Add(client);
        }

        public async Task Broadcast(Message msg, string ipport)
        {
            foreach (var k in Subscriptions.Keys)
            {
                if (SharedState.ComparePaths(k, msg.Path))
                {
                    foreach (var sub in Subscriptions[k])
                    {
                        if (sub != ipport)
                            await WebSocket.SendAsync(sub, JsonConvert.SerializeObject(msg));
                    }
                }
            }
        }

        public async Task WsMessageReceived(string ipport, byte[] data)
        {
            var messageAsString = Encoding.UTF8.GetString(data);

            var message = JsonConvert.DeserializeObject<Message>(messageAsString);

            if (message.Type == "Subscribe")
            {
                AddSubscription(ipport, message.Content);
                var obj = State.RetrievePath(message.Content);
                if (obj != null)
                {
                    var m = new Message() { Content = JsonConvert.SerializeObject(obj), Path = message.Content, Type = "Update" };
                    await WebSocket.SendAsync(ipport, JsonConvert.SerializeObject(m));
                }
            }
            else
            {
                if (message.Type == "Update")
                {
                    State.UpdatePath(message.Path, JsonHelper.Deserialize(message.Content));
                    State.Save();
                }

                await Broadcast(message, ipport);
            }

        }

        public static async Task defaultRoute(HttpContext arg)
        {
            arg.Response.StatusCode = 404;
            await arg.Response.Send("NO DICE!");
        }
    }
}
