using LCUSharp;
using LCUSharp.Websocket;
using LightWeightOverlay;
using LoLApi;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace lol_picksandbans
{
    public class LCUHook
    {
        private readonly IStatePublisher _publisher;
        private readonly SemaphoreSlim _reconnectLock = new SemaphoreSlim(1, 1);
        private readonly Dictionary<long, Summoner> _summoners = new Dictionary<long, Summoner>();
        private Dictionary<DateTime, Session>? _sessionLog;

        public event EventHandler<LeagueEvent>? GameFlowChanged;
        public LeagueClientApi? currentAPI { get; private set; }

        public LCUHook(LWOServer s) : this(new ServerStatePublisher(s))
        {
        }

        public LCUHook(IStatePublisher publisher)
        {
            _publisher = publisher;
        }

        public async Task PlaybackSessionlogAsync()
        {
            var raw = await File.ReadAllTextAsync("sessionLog.json").ConfigureAwait(false);
            _sessionLog = JsonConvert.DeserializeObject<Dictionary<DateTime, Session>>(raw);
            if (_sessionLog == null || _sessionLog.Count == 0)
                return;

            while (true)
            {
                foreach (var kvp in _sessionLog)
                {
                    await _publisher.PublishAsync("lolChampSelect/session", kvp.Value).ConfigureAwait(false);
                    Console.WriteLine($"{kvp.Key} - Press Enter to continue...");
                    Console.ReadLine();
                }
            }
        }

        public async Task ConnectToLeague()
        {
            await _reconnectLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (currentAPI != null)
                {
                    currentAPI.Disconnected -= CurrentAPI_Disconnected;
                    currentAPI.EventHandler.UnsubscribeAll();
                }

                var api = await LeagueClientApi.ConnectAsync().ConfigureAwait(false);
                currentAPI = api;

                Console.WriteLine("Connected to League Client!");

                api.EventHandler.Subscribe("/lol-gameflow/v1/gameflow-phase", OnGameFlowChanged);
                api.EventHandler.Subscribe("/lol-champ-select/v1/session", OnSessionChanged);
                api.EventHandler.Subscribe("/lol-gameflow/v1/session", OnGameflowSessionChanged);
                api.Disconnected += CurrentAPI_Disconnected;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LCUHook] ConnectToLeague error: {ex.Message}");
            }
            finally
            {
                _reconnectLock.Release();
            }
        }

        private void CurrentAPI_Disconnected(object? sender, EventArgs e)
        {
            Console.WriteLine("Disconnected from League Client! Reconnecting...");

            if (sender is LeagueClientApi api)
            {
                api.Disconnected -= CurrentAPI_Disconnected;
                api.EventHandler.UnsubscribeAll();
            }

            _ = Task.Run(async () =>
            {
                await Task.Delay(1500).ConfigureAwait(false);
                await ConnectToLeague().ConfigureAwait(false);
            });
        }

        private void OnGameflowSessionChanged(object? sender, LeagueEvent e)
        {
            try
            {
                if (e?.Data == null)
                    return;

                var result = e.Data.ToString();
                if (string.IsNullOrWhiteSpace(result))
                    return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LCUHook] OnGameflowSessionChanged error: {ex.Message}");
            }
        }

        private void OnGameFlowChanged(object? sender, LeagueEvent e)
        {
            try
            {
                if (e == null)
                    return;

                GameFlowChanged?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LCUHook] OnGameFlowChanged error: {ex.Message}");
            }
        }

        private void OnSessionChanged(object? sender, LeagueEvent e)
        {
            _ = HandleSessionChangedAsync(e);
        }

        private async Task HandleSessionChangedAsync(LeagueEvent? e)
        {
            try
            {
                if (e == null)
                    return;

                if (string.Equals(e.EventType, "Delete", StringComparison.OrdinalIgnoreCase) || e.Data == null)
                {
                    await _publisher.PublishAsync("lolChampSelect/session", null).ConfigureAwait(false);
                    return;
                }

                var rawJson = e.Data.ToString();
                if (string.IsNullOrWhiteSpace(rawJson))
                    return;

                var session = Session.FromJson(rawJson);
                var newSummoner = false;
                var api = currentAPI;

                var allPlayers = (session.MyTeam ?? Array.Empty<Player>())
                    .Concat(session.TheirTeam ?? Array.Empty<Player>());

                foreach (var player in allPlayers)
                {
                    if (player.SummonerId <= 0)
                        continue;

                    if (_summoners.ContainsKey(player.SummonerId))
                        continue;

                    // Si el payload ya trae nombre o Riot ID, guardamos un registro base inmediato
                    if (!string.IsNullOrWhiteSpace(player.GameName) || !string.IsNullOrWhiteSpace(player.SummonerName))
                    {
                        _summoners[player.SummonerId] = new Summoner
                        {
                            SummonerId = player.SummonerId,
                            GameName = player.GameName ?? string.Empty,
                            TagLine = player.TagLine ?? string.Empty,
                            DisplayName = !string.IsNullOrWhiteSpace(player.GameName)
                                ? player.GameName
                                : (player.SummonerName ?? string.Empty)
                        };
                        newSummoner = true;
                    }

                    if (api != null)
                    {
                        try
                        {
                            var summonerJson = await api.RequestHandler
                                .GetJsonResponseAsync(HttpMethod.Get, $"/lol-summoner/v1/summoners/{player.SummonerId}")
                                .ConfigureAwait(false);

                            var fetched = Summoner.FromJson(summonerJson);
                            if (fetched.SummonerId > 0)
                            {
                                _summoners[player.SummonerId] = fetched;
                                newSummoner = true;
                            }
                        }
                        catch
                        {
                            // Ignora bots o invocadores no consultables en /lol-summoner/v1/summoners/{id}
                        }
                    }
                }

                if (newSummoner)
                {
                    await _publisher.PublishAsync("lolChampSelect/summoners", _summoners).ConfigureAwait(false);
                }

                var deserializedState = JsonHelper.Deserialize(rawJson);
                await _publisher.PublishAsync("lolChampSelect/session", deserializedState).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LCUHook] OnSessionChanged error: {ex.Message}");
            }
        }
    }
}