using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsFormsApp1
{
    /// <summary>
    /// 统一处理业务服务的地址拼接、JSON 请求、超时和响应反序列化。
    /// </summary>
    internal static class ApiClient
    {
        private const int TimeoutSeconds = 30;
        private static readonly HttpClient HttpClient = CreateHttpClient();
        private static string bearerToken;

        /// <summary>
        /// 保存本次登录会话的 Bearer Token，供需要鉴权的接口显式使用。
        /// </summary>
        public static void SetBearerToken(string token)
        {
            bearerToken = string.IsNullOrWhiteSpace(token) ? null : token.Trim();
        }

        /// <summary>
        /// 发起 GET 请求，并将 JSON 响应反序列化为指定类型。
        /// </summary>
        public static async Task<TResponse> GetAsync<TResponse>(string path, string query = null)
        {
            string responseJson = await SendAsync(HttpMethod.Get, BuildUrl(path, query), null);
            return Deserialize<TResponse>(responseJson);
        }

        /// <summary>
        /// 发起 JSON POST 请求，并将 JSON 响应反序列化为指定类型。
        /// </summary>
        public static async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest body, bool includeBearerToken = false)
        {
            string requestJson = Serialize(body);
            string responseJson = await SendAsync(HttpMethod.Post, BuildUrl(path, null), requestJson, includeBearerToken);
            return Deserialize<TResponse>(responseJson);
        }

        /// <summary>
        /// 将业务路径拼接到统一服务基础地址。
        /// </summary>
        private static string BuildUrl(string path, string query)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("接口路径不能为空。", nameof(path));

            string url = ApiEndpoints.ServiceBaseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
            return string.IsNullOrWhiteSpace(query) ? url : url + "?" + query.TrimStart('?');
        }

        /// <summary>
        /// 发送 HTTP 请求并将网络、超时和非成功状态转换为可展示异常。
        /// </summary>
        private static async Task<string> SendAsync(HttpMethod method, string requestUrl, string jsonBody, bool includeBearerToken = false)
        {
            using (var request = new HttpRequestMessage(method, requestUrl))
            using (var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds)))
            {
                if (!string.IsNullOrWhiteSpace(jsonBody))
                    request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                if (includeBearerToken)
                {
                    if (string.IsNullOrWhiteSpace(bearerToken))
                        throw new InvalidOperationException("当前登录会话缺少访问令牌，请重新登录。");

                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                }

                try
                {
                    using (HttpResponseMessage response = await HttpClient
                        .SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutSource.Token)
                        .ConfigureAwait(false))
                    {
                        string responseBody = response.Content == null
                            ? string.Empty
                            : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode)
                            throw new HttpRequestException($"服务请求失败：HTTP {(int)response.StatusCode} ({response.ReasonPhrase})。");

                        return responseBody;
                    }
                }
                catch (OperationCanceledException ex) when (timeoutSource.IsCancellationRequested)
                {
                    throw new TimeoutException($"服务请求超时（{TimeoutSeconds} 秒）。", ex);
                }
            }
        }

        /// <summary>
        /// 创建整个应用共享的 HTTP 客户端，避免重复建立连接。
        /// </summary>
        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            var client = new HttpClient(handler);
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate");
            return client;
        }

        /// <summary>
        /// 将请求模型序列化为 UTF-8 JSON。
        /// </summary>
        private static string Serialize<T>(T value)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        /// <summary>
        /// 将服务响应反序列化为指定模型。
        /// </summary>
        private static T Deserialize<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException("服务未返回有效数据。");

            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return (T)serializer.ReadObject(stream);
            }
        }
    }
}
