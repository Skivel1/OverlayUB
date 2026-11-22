using LCUSharp.Http;
using LCUSharp.Http.Endpoints;
using LCUSharp.Utility;
using LCUSharp.Websocket;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace LCUSharp
{
    /// <inheritdoc />
    public class LeagueClientApi : ILeagueClientApi
    {
        private static readonly LeagueProcessHandler _processHandler;
        private static readonly LockFileHandler _lockFileHandler;

        /// <inheritdoc />
        public event EventHandler? Disconnected;

        /// <inheritdoc />
        public ILeagueRequestHandler RequestHandler { get; }

        /// <inheritdoc />
        public ILeagueEventHandler EventHandler { get; }

        /// <inheritdoc />
        public IRiotClientEndpoint RiotClientEndpoint { get; }

        /// <inheritdoc />
        public IProcessControlEndpoint ProcessControlEndpoint { get; }

        static LeagueClientApi()
        {
            _processHandler = new LeagueProcessHandler();
            _lockFileHandler = new LockFileHandler();
        }

        private LeagueClientApi(int port, string token, ILeagueEventHandler eventHandler)
        {
            EventHandler = eventHandler;
            RequestHandler = new LeagueRequestHandler(port, token);
            RiotClientEndpoint = new RiotClientEndpoint(RequestHandler);
            ProcessControlEndpoint = new ProcessControlEndpoint(RequestHandler);

            _processHandler.Exited -= OnDisconnected;
            _processHandler.Exited += OnDisconnected;
        }

        /// <summary>
        /// Connects to the league client api.
        /// </summary>
        public static async Task<LeagueClientApi> ConnectAsync()
        {
            var (port, token) = await GetAuthCredentialsAsync().ConfigureAwait(false);
            var eventHandler = new LeagueEventHandler(port, token);
            var api = new LeagueClientApi(port, token, eventHandler);
            return await EnsureConnectionAsync(api).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task ReconnectAsync()
        {
            var (port, token) = await GetAuthCredentialsAsync().ConfigureAwait(false);
            await Task.Run(() =>
            {
                RequestHandler.ChangeSettings(port, token);
                EventHandler.ChangeSettings(port, token);
            }).ConfigureAwait(false);
            await EnsureConnectionAsync(this).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public void Disconnect()
        {
            OnDisconnected(this, EventArgs.Empty);
        }

        private static async Task<(int port, string token)> GetAuthCredentialsAsync()
        {
            await Task.Run(() => _processHandler.WaitForProcess()).ConfigureAwait(false);
            return await _lockFileHandler.ParseLockFileAsync(_processHandler.ExecutablePath).ConfigureAwait(false);
        }

        private static async Task<LeagueClientApi> EnsureConnectionAsync(LeagueClientApi api)
        {
            while (true)
            {
                try
                {
                    await api.RequestHandler.GetResponseAsync<object>(HttpMethod.Get, "/riotclient/app-name").ConfigureAwait(false);
                    await Task.Run(() => api.EventHandler.Connect()).ConfigureAwait(false);
                    return api;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[LeagueClientApi] Waiting for LCU connection: {e.Message}");
                    await Task.Delay(3000).ConfigureAwait(false);
                }
            }
        }

        private async void OnDisconnected(object? sender, EventArgs e)
        {
            try
            {
                await Task.Run(() => EventHandler.Disconnect()).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LeagueClientApi] Error on disconnect: {ex.Message}");
            }
            finally
            {
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}