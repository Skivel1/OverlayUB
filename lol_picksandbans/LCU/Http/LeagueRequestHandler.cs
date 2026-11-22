using LoLApi;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace LCUSharp.Http
{
    /// <inheritdoc cref="ILeagueRequestHandler" />
    internal class LeagueRequestHandler : RequestHandler, ILeagueRequestHandler
    {
        /// <inheritdoc />
        public int Port { get; set; }

        /// <inheritdoc />
        public string Token { get; set; } = string.Empty;

        /// <summary>
        /// Creates a new instance of the <see cref="LeagueRequestHandler"/> class.
        /// </summary>
        /// <param name="port">The league client's port.</param>
        /// <param name="token">The user's Basic authentication token.</param>
        public LeagueRequestHandler(int port, string token)
        {
            ChangeSettings(port, token);
        }

        /// <inheritdoc />
        public void ChangeSettings(int port, string token)
        {
            Port = port;
            Token = token;
            CreateHttpClient();

            var authTokenBytes = Encoding.ASCII.GetBytes($"riot:{token}");
            HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authTokenBytes));
            HttpClient.BaseAddress = new Uri($"https://127.0.0.1:{port}/");
        }

        /// <inheritdoc />
        public Task<string> GetJsonResponseAsync(HttpMethod httpMethod, string relativeUrl, IEnumerable<string>? queryParameters = null)
        {
            return GetJsonResponseAsync<object>(httpMethod, relativeUrl, queryParameters, null);
        }

        /// <inheritdoc />
        public async Task<string> GetJsonResponseAsync<TRequest>(HttpMethod httpMethod, string relativeUrl, IEnumerable<string>? queryParameters, TRequest? body)
        {
            var normalizedUrl = (relativeUrl ?? string.Empty).TrimStart('/');
            using var request = await PrepareRequestAsync(httpMethod, normalizedUrl, queryParameters, body).ConfigureAwait(false);
            using var response = await HttpClient.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await GetResponseContentAsync(response).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task<TResponse?> GetResponseAsync<TResponse>(HttpMethod httpMethod, string relativeUrl, IEnumerable<string>? queryParameters = null)
        {
            return GetResponseAsync<object, TResponse>(httpMethod, relativeUrl, queryParameters, null);
        }

        /// <inheritdoc />
        public async Task<TResponse?> GetResponseAsync<TRequest, TResponse>(HttpMethod httpMethod, string relativeUrl, IEnumerable<string>? queryParameters, TRequest? body)
        {
            var json = await GetJsonResponseAsync(httpMethod, relativeUrl, queryParameters, body).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                return default;

            if (typeof(TResponse) == typeof(string))
                return (TResponse)(object)json;

            return JsonConvert.DeserializeObject<TResponse>(json, Converter.Settings);
        }
    }
}