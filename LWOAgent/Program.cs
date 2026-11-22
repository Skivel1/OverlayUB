using lol_picksandbans;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LWOAgent
{
    /// <summary>Publica el estado del cliente de LoL en un LWOServer remoto por WebSocket.</summary>
    public class RemotePublisher : IStatePublisher
    {
        private readonly Uri _uri;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<string, string> _last = new ConcurrentDictionary<string, string>();
        private ClientWebSocket? _ws;

        public RemotePublisher(string url) { _uri = new Uri(url); }

        public async Task PublishAsync(string path, object? value)
        {
            var json = JsonConvert.SerializeObject(new
            {
                Type = "Update",
                Path = path,
                Content = JsonConvert.SerializeObject(value)
            });
            _last[path] = json;           // se reenvia al reconectar
            await SendAsync(json).ConfigureAwait(false);
        }

        /// <summary>Mantiene la conexion viva y reenvia el ultimo estado tras reconectar.</summary>
        public async Task RunKeepAliveAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try { await EnsureConnectedAsync(resend: true).ConfigureAwait(false); }
                catch (Exception ex) { Console.WriteLine($"[Agent] No se pudo conectar: {ex.Message}"); }
                await Task.Delay(3000, ct).ConfigureAwait(false);
            }
        }

        private async Task SendAsync(string json)
        {
            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_ws == null || _ws.State != WebSocketState.Open) return; // keep-alive reconecta y reenvia
                var bytes = Encoding.UTF8.GetBytes(json);
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Agent] Error enviando: {ex.Message}");
                _ws?.Dispose(); _ws = null;
            }
            finally { _lock.Release(); }
        }

        private async Task EnsureConnectedAsync(bool resend)
        {
            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_ws != null && _ws.State == WebSocketState.Open) return;
                _ws?.Dispose();
                _ws = new ClientWebSocket();
                _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                await _ws.ConnectAsync(_uri, CancellationToken.None).ConfigureAwait(false);
                Console.WriteLine("[Agent] Conectado al servidor remoto.");
                if (resend)
                    foreach (var json in _last.Values)
                        await _ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
            }
            catch { _ws?.Dispose(); _ws = null; throw; }
            finally { _lock.Release(); }
        }
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            // URL: argumento, variable LWO_URL o archivo agent.url.txt junto al exe
            var url = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("LWO_URL");
            var file = Path.Combine(AppContext.BaseDirectory, "agent.url.txt");
            if (string.IsNullOrWhiteSpace(url) && File.Exists(file)) url = File.ReadAllText(file).Trim();

            if (string.IsNullOrWhiteSpace(url))
            {
                Console.WriteLine("Uso: LWOAgent.exe wss://overlay.tudominio.com/ws?token=TU_TOKEN");
                Console.WriteLine("(o pon la URL en agent.url.txt junto al .exe)");
                Console.ReadLine();
                return;
            }

            var publisher = new RemotePublisher(url);
            using var cts = new CancellationTokenSource();
            _ = publisher.RunKeepAliveAsync(cts.Token);

            var hook = new LCUHook(publisher);
            Console.WriteLine("[Agent] Esperando al cliente de League of Legends...");
            await hook.ConnectToLeague().ConfigureAwait(false);

            await Task.Delay(Timeout.Infinite).ConfigureAwait(false);
        }
    }
}
