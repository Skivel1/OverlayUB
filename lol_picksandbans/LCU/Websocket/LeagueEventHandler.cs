using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Security.Authentication;
using WebSocketSharp;

namespace LCUSharp.Websocket
{
    /// <inheritdoc />
    internal class LeagueEventHandler : ILeagueEventHandler
    {
        private const int WampEventMessageType = 8;
        private const int WampEventPayloadIndex = 2;

        private readonly object _lock = new object();
        private readonly Dictionary<string, List<EventHandler<LeagueEvent>>> _subscribers;
        private WebSocket? _webSocket;

        /// <inheritdoc />
        public EventHandler<LeagueEvent>? MessageReceived { get; set; }

        /// <inheritdoc />
        public EventHandler<string>? ErrorReceived { get; set; }

        /// <summary>
        /// Creates a new instance of the <see cref="LeagueEventHandler"/> class.
        /// </summary>
        /// <param name="port">The league client's port.</param>
        /// <param name="token">The user's Basic authentication token.</param>
        public LeagueEventHandler(int port, string token)
        {
            _subscribers = new Dictionary<string, List<EventHandler<LeagueEvent>>>(StringComparer.OrdinalIgnoreCase);
            ChangeSettings(port, token);
        }

        /// <inheritdoc />
        public void Connect()
        {
            if (_webSocket == null)
                return;

            if (!_webSocket.IsAlive)
            {
                _webSocket.Connect();
            }

            if (_webSocket.IsAlive)
            {
                _webSocket.Send("[5, \"OnJsonApiEvent\"]");
            }
        }

        /// <inheritdoc />
        public void Disconnect()
        {
            if (_webSocket == null)
                return;

            try
            {
                if (_webSocket.IsAlive || _webSocket.ReadyState == WebSocketState.Open)
                {
                    _webSocket.Close();
                }
            }
            catch (Exception ex)
            {
                ErrorReceived?.Invoke(this, $"Disconnect error: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public void ChangeSettings(int port, string token)
        {
            DetachWebSocket(_webSocket);

            var ws = new WebSocket($"wss://127.0.0.1:{port}/", "wamp");
            ws.SetCredentials("riot", token, true);
            ws.SslConfiguration.EnabledSslProtocols = SslProtocols.Tls12;
            ws.SslConfiguration.ServerCertificateValidationCallback = (sender, cert, chain, errors) => true;

            ws.OnError += OnError;
            ws.OnMessage += OnMessage;

            _webSocket = ws;
        }

        /// <inheritdoc />
        public void Subscribe(string uri, EventHandler<LeagueEvent> eventHandler)
        {
            if (string.IsNullOrWhiteSpace(uri) || eventHandler == null)
                return;

            lock (_lock)
            {
                if (_subscribers.TryGetValue(uri, out List<EventHandler<LeagueEvent>>? eventHandlers))
                {
                    eventHandlers.Add(eventHandler);
                }
                else
                {
                    _subscribers[uri] = new List<EventHandler<LeagueEvent>> { eventHandler };
                }
            }
        }

        /// <inheritdoc />
        public void Unsubscribe(string uri)
        {
            if (string.IsNullOrWhiteSpace(uri))
                return;

            lock (_lock)
            {
                _subscribers.Remove(uri);
            }
        }

        /// <inheritdoc />
        public void UnsubscribeAll()
        {
            lock (_lock)
            {
                _subscribers.Clear();
            }
        }

        private void DetachWebSocket(WebSocket? ws)
        {
            if (ws == null)
                return;

            ws.OnError -= OnError;
            ws.OnMessage -= OnMessage;

            try
            {
                if (ws.IsAlive || ws.ReadyState == WebSocketState.Open)
                {
                    ws.Close();
                }
            }
            catch
            {
                // Ignore cleanup errors on stale socket
            }
        }

        private void OnError(object? sender, ErrorEventArgs e)
        {
            ErrorReceived?.Invoke(sender, e.Message);
        }

        private void OnMessage(object? sender, MessageEventArgs e)
        {
            if (!e.IsText || string.IsNullOrWhiteSpace(e.Data))
                return;

            try
            {
                var eventArray = JArray.Parse(e.Data);
                if (eventArray.Count <= WampEventPayloadIndex)
                    return;

                var eventNumber = eventArray[0].ToObject<int>();
                if (eventNumber != WampEventMessageType)
                    return;

                var leagueEvent = eventArray[WampEventPayloadIndex].ToObject<LeagueEvent>();
                if (leagueEvent == null)
                    return;

                MessageReceived?.Invoke(sender, leagueEvent);

                if (string.IsNullOrWhiteSpace(leagueEvent.Uri))
                    return;

                List<EventHandler<LeagueEvent>>? handlersSnapshot;
                lock (_lock)
                {
                    _subscribers.TryGetValue(leagueEvent.Uri, out var existing);
                    handlersSnapshot = existing != null ? new List<EventHandler<LeagueEvent>>(existing) : null;
                }

                if (handlersSnapshot != null)
                {
                    foreach (var eventHandler in handlersSnapshot)
                    {
                        try
                        {
                            eventHandler?.Invoke(sender, leagueEvent);
                        }
                        catch (Exception ex)
                        {
                            ErrorReceived?.Invoke(sender, $"Subscriber error ({leagueEvent.Uri}): {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorReceived?.Invoke(sender, $"Failed to parse LCU event: {ex.Message}");
            }
        }
    }
}