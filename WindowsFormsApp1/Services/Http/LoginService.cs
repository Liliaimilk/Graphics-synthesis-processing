using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsFormsApp1
{
    /// <summary>
    /// 已通过登录接口验证的用户信息。
    /// </summary>
    [DataContract]
    internal sealed class LoginUser
    {
        [DataMember(Name = "userId")]
        public int UserId { get; set; }

        [DataMember(Name = "username")]
        public string Username { get; set; }
    }

    /// <summary>
    /// 负责调用工厂客户端登录接口，不承担界面提示和凭据保存职责。
    /// </summary>
    internal static class LoginService
    {
        private const string LoginEndpoint = "http://192.168.0.222:8080/api/factory-client/login";
        private const int TimeoutSeconds = 30;
        private static readonly HttpClient HttpClient = CreateHttpClient();

        [DataContract]
        private sealed class LoginRequest
        {
            [DataMember(Name = "username")]
            public string Username { get; set; }

            [DataMember(Name = "password")]
            public string Password { get; set; }
        }

        [DataContract]
        private sealed class LoginResponse
        {
            [DataMember(Name = "msg")]
            public string Message { get; set; }

            [DataMember(Name = "code")]
            public int Code { get; set; }

            [DataMember(Name = "data")]
            public LoginUser Data { get; set; }
        }

        /// <summary>
        /// 使用账号密码请求服务端登录，并验证 HTTP 与业务状态码。
        /// </summary>
        public static async Task<LoginUser> LoginAsync(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("请输入账号。", nameof(username));
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("请输入密码。", nameof(password));

            string requestJson = Serialize(new LoginRequest
            {
                Username = username.Trim(),
                Password = password
            });

            using (var request = new HttpRequestMessage(HttpMethod.Post, LoginEndpoint))
            using (var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds)))
            {
                request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                try
                {
                    using (HttpResponseMessage response = await HttpClient
                        .SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutSource.Token)
                        .ConfigureAwait(false))
                    {
                        string responseJson = response.Content == null
                            ? string.Empty
                            : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode)
                            throw new HttpRequestException($"登录请求失败：HTTP {(int)response.StatusCode} ({response.ReasonPhrase})。");

                        LoginResponse loginResponse = Deserialize<LoginResponse>(responseJson);
                        if (loginResponse == null)
                            throw new InvalidOperationException("登录接口未返回有效数据。");
                        if (loginResponse.Code != 200)
                            throw new InvalidOperationException(string.IsNullOrWhiteSpace(loginResponse.Message) ? "登录失败。" : loginResponse.Message);
                        if (loginResponse.Data == null || string.IsNullOrWhiteSpace(loginResponse.Data.Username))
                            throw new InvalidOperationException("登录接口未返回有效用户信息。");

                        return loginResponse.Data;
                    }
                }
                catch (OperationCanceledException ex) when (timeoutSource.IsCancellationRequested)
                {
                    throw new TimeoutException($"登录请求超时（{TimeoutSeconds} 秒）。", ex);
                }
            }
        }

        /// <summary>
        /// 创建复用的 HTTP 客户端，避免频繁建立连接。
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
        /// 将登录请求序列化为接口需要的 JSON 文本。
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
        /// 解析接口 JSON 响应。
        /// </summary>
        private static T Deserialize<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException("登录接口返回内容为空。");

            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return (T)serializer.ReadObject(stream);
            }
        }
    }
}
