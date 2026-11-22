using LightWeightOverlay;
using LightWeightOverlay.Applications;
using System;
using System.IO;
using System.Threading.Tasks;
using WatsonWebserver;

namespace lol_picksandbans
{
    public class PicksAndBans : AApplication
    {
        private LCUHook? _hook;
        private SpecialRouteProcessor? _pbRoute;

        public override void Load(LWOServer s)
        {
            _server = s;
            _hook = new LCUHook(_server);

            var webDirectory = Path.Combine(s.AppDirectory, "lol_picksandbans", "web") + System.IO.Path.DirectorySeparatorChar;
            _pbRoute = new SpecialRouteProcessor("/lol_picksandbans/", webDirectory);
            _pbRoute.AddToWebserver(s.WebServer);

            // En Docker/servidor remoto el cliente de LoL no esta disponible: el agente local publica el estado.
            if (Environment.GetEnvironmentVariable("LWO_DISABLE_LCU") != "1")
                _hook.ConnectToLeague();
            else
                Console.WriteLine("[LCU] Deshabilitado (LWO_DISABLE_LCU=1). Esperando datos del agente remoto.");

            Console.WriteLine($"Pick&Ban Overlay: {_server.GetWebUrl("/lol_picksandbans/")}");
        }

        private Task RequestHandler(HttpContext arg)
        {
            return _pbRoute != null ? _pbRoute.Process(arg) : Task.CompletedTask;
        }

        public override void Start()
        {
        }

        public override string StatusHtml()
        {
            var connected = _hook?.currentAPI != null;
            return $"<p>LoL Picks & Bans: <strong>{(connected ? "Connected" : "Waiting for League Client")}</strong> — <code>/lol_picksandbans/</code></p>";
        }

        public override string StatusString()
        {
            var connected = _hook?.currentAPI != null;
            return $"LoL Picks & Bans ({(connected ? "Connected" : "Waiting for League Client")})";
        }

        public override void Stop()
        {
        }

        public override void Unload()
        {
            if (_hook?.currentAPI != null)
            {
                _hook.currentAPI.Disconnect();
            }
        }

        public override string GetName()
        {
            return "lol_picksandbans";
        }
    }
}