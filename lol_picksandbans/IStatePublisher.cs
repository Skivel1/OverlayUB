using LightWeightOverlay;
using System.Threading.Tasks;

namespace lol_picksandbans
{
    /// <summary>Destino donde LCUHook publica el estado (servidor local o agente remoto).</summary>
    public interface IStatePublisher
    {
        Task PublishAsync(string path, object? value);
    }

    /// <summary>Publica directo en el LWOServer del mismo proceso (modo original).</summary>
    public class ServerStatePublisher : IStatePublisher
    {
        private readonly LWOServer _server;
        public ServerStatePublisher(LWOServer server) { _server = server; }

        public Task PublishAsync(string path, object? value)
            => _server.Broadcast(_server.State.UpdatePath(path, value), "");
    }
}
