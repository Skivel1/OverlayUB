using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text;
using System.Threading.Tasks;

namespace LCUSharp.Http
{
    /// <summary>
    /// A request handler that supports authentication.
    /// </summary>
    internal abstract class RequestHandler : IDisposable
    {
        private readonly HttpClientHandler _httpClientHandler;
        private bool _disposed;

        /// <summary>
        /// The HttpClient used to make requests.
        /// </summary>
        protected HttpClient HttpClient { get; private set; } = null!;

        /// <summary>
        /// Creates a new instance of the <see cref="RequestHandler"/> class.
        /// </summary>
        public RequestHandler()
        {
            _httpClientHandler = new HttpClientHandler
            {
                ClientCertificateOptions = ClientCertificateOption.Manual,
                SslProtocols = SslProtocols.Tls12,
                ServerCertificateCustomValidationCallback = (response, cert, chain, errors) => true
            };

            CreateHttpClient();
        }

        /// <summary>
        /// Creates a new <see cref="HttpClient"/>.
        /// </summary>
        protected void CreateHttpClient()
        {
            HttpClient?.Dispose();
            HttpClient = new HttpClient(_httpClientHandler, disposeHandler: false);
            HttpClient.DefaultRequestHeaders.Accept.Clear();
            HttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        /// <summary>
        /// Prepares a request by calculating the proper url.
        /// </summary>
        protected Task<HttpRequestMessage> PrepareRequestAsync<TRequest>(
            HttpMethod httpMethod,
            string relativeUrl,
            IEnumerable<string>? queryParameters,
            TRequest? body)
        {
            var queryString = BuildQueryParameterString(queryParameters);
            var url = string.IsNullOrEmpty(queryString)
                ? relativeUrl
                : relativeUrl + queryString;

            var request = new HttpRequestMessage(httpMethod, url);

            if (body != null)
            {
                var json = JsonConvert.SerializeObject(body);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            return Task.FromResult(request);
        }

        /// <summary>
        /// Builds the query parameter string that's appended to the request url.
        /// </summary>
        protected string BuildQueryParameterString(IEnumerable<string>? queryParameters)
        {
            if (queryParameters == null)
                return string.Empty;

            var validParams = queryParameters.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
            return validParams.Count == 0
                ? string.Empty
                : "?" + string.Join("&", validParams);
        }

        /// <summary>
        /// Gets a <see cref="HttpResponseMessage"/>'s content.
        /// </summary>
        protected async Task<string> GetResponseContentAsync(HttpResponseMessage response)
        {
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            HttpClient?.Dispose();
            _httpClientHandler.Dispose();
            _disposed = true;
        }
    }
}