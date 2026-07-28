using System;
using System.Collections.Generic;
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
    public partial class Form1
    {
        private const int DefaultHttpTimeoutSeconds = 30;
        private const string OrderScanResultEndpoint = "http://192.168.0.222:8080/open-api/orders/scan-result";

        // HttpClient 应在窗体生命周期内复用，避免每次请求重复创建连接和耗尽端口。
        private static readonly HttpClient SharedHttpClient = CreateHttpClient();
        private List<OrderScanResult> latestOrderScanResults = new List<OrderScanResult>();

        [DataContract]
        private sealed class OrderScanResponse
        {
            [DataMember(Name = "msg")]
            public string Message { get; set; }

            [DataMember(Name = "code")]
            public int Code { get; set; }

            [DataMember(Name = "data")]
            public List<OrderScanResult> Data { get; set; }
        }

        [DataContract]
        private sealed class OrderScanResult
        {
            [DataMember(Name = "orderNum")]
            public string OrderNumber { get; set; }

            [DataMember(Name = "skuName")]
            public string SkuName { get; set; }
        }

        /// <summary>
        /// 按订单号查询扫码订单结果，并校验接口业务状态码。
        /// </summary>
        private async Task<List<OrderScanResult>> GetOrderScanResultsAsync(string orderNumber)
        {
            string requestUrl = OrderScanResultEndpoint + "?orderNum=" + Uri.EscapeDataString(orderNumber);
            string responseJson = await SendHttpGetAsync(requestUrl);
            OrderScanResponse response = DeserializeHttpJson<OrderScanResponse>(responseJson);

            if (response == null)
                throw new InvalidOperationException("订单接口未返回有效数据。");

            if (response.Code != 200)
            {
                // Console.WriteLine($"订单查询接口返回非成功状态码: {response.Code}, 消息: {response.Message}");
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(response.Message) ? "订单查询未成功。" : response.Message);
            }
                

            if (response.Data == null || response.Data.Count == 0)
                throw new InvalidOperationException("未查询到订单信息。");

            return response.Data;
        }

        /// <summary>
        /// 将订单信息转换为适合状态栏显示的简短文本。
        /// </summary>
        private static string BuildOrderStatusText(IReadOnlyList<OrderScanResult> orderResults)
        {
            if (orderResults == null || orderResults.Count == 0)
                return "未查询到订单信息";

            OrderScanResult firstResult = orderResults[0];
            string orderNumber = string.IsNullOrWhiteSpace(firstResult.OrderNumber) ? "未知订单" : firstResult.OrderNumber;
            string skuName = string.IsNullOrWhiteSpace(firstResult.SkuName) ? "未返回 SKU" : firstResult.SkuName;
            return $"订单查询成功: {orderNumber} | SKU {orderResults.Count} 条 | {skuName}";
        }

        /// <summary>
        /// 使用项目现有的 DataContract JSON 机制反序列化 HTTP 响应。
        /// </summary>
        private static T DeserializeHttpJson<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException("订单接口返回内容为空。");

            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return (T)serializer.ReadObject(stream);
            }
        }

        /// <summary>
        /// 发送 GET 请求并返回服务端响应文本。
        /// </summary>
        private Task<string> SendHttpGetAsync(
            string requestUrl,
            IDictionary<string, string> headers = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return SendHttpRequestAsync(HttpMethod.Get, requestUrl, null, headers, cancellationToken);
        }

        /// <summary>
        /// 发送 JSON POST 请求并返回服务端响应文本。
        /// </summary>
        private Task<string> SendHttpJsonPostAsync(
            string requestUrl,
            string jsonBody,
            IDictionary<string, string> headers = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return SendHttpRequestAsync(HttpMethod.Post, requestUrl, jsonBody, headers, cancellationToken);
        }

        /// <summary>
        /// 发送 HTTP 请求。请求体非空时会以 application/json; charset=utf-8 提交。
        /// 非 2xx 响应、超时和主动取消都会转换为包含上下文的异常，便于界面直接提示。
        /// </summary>
        private async Task<string> SendHttpRequestAsync(
            HttpMethod method,
            string requestUrl,
            string jsonBody = null,
            IDictionary<string, string> headers = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Console.WriteLine($"发送 HTTP 请求: {method} {requestUrl}");
            if (method == null)
                throw new ArgumentNullException(nameof(method));

            Uri requestUri = ValidateHttpUri(requestUrl);
            using (var request = new HttpRequestMessage(method, requestUri))
            using (var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(DefaultHttpTimeoutSeconds)))
            using (var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token))
            {
                if (!string.IsNullOrWhiteSpace(jsonBody))
                    request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                AddRequestHeaders(request, headers);

                try
                {
                    using (HttpResponseMessage response = await SharedHttpClient
                        .SendAsync(request, HttpCompletionOption.ResponseContentRead, linkedSource.Token)
                        .ConfigureAwait(false))
                    {
                        string responseBody = response.Content == null
                            ? string.Empty
                            : await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                        if (!response.IsSuccessStatusCode)
                        {
                            string detail = string.IsNullOrWhiteSpace(responseBody)
                                ? "服务端未返回错误详情。"
                                : responseBody;
                            throw new HttpRequestException(
                                $"HTTP {(int)response.StatusCode} ({response.ReasonPhrase}): {detail}");
                        }

                        return responseBody;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException ex) when (timeoutSource.IsCancellationRequested)
                {
                    throw new TimeoutException($"HTTP 请求超时（{DefaultHttpTimeoutSeconds} 秒）: {requestUri}", ex);
                }
            }
        }

        /// <summary>
        /// 创建共享 HTTP 客户端，启用常用压缩协议以减少 JSON 响应传输量。
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
        /// 验证请求地址仅使用 HTTP 或 HTTPS，避免错误地址在底层抛出难以理解的异常。
        /// </summary>
        private static Uri ValidateHttpUri(string requestUrl)
        {
            if (!Uri.TryCreate(requestUrl, UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException("请求地址必须是有效的 HTTP 或 HTTPS 绝对地址。", nameof(requestUrl));
            }

            return uri;
        }

        /// <summary>
        /// 写入调用方提供的请求头；无效头会立即报错，避免出现静默丢失鉴权信息的情况。
        /// </summary>
        private static void AddRequestHeaders(HttpRequestMessage request, IDictionary<string, string> headers)
        {
            if (headers == null)
                return;

            foreach (KeyValuePair<string, string> header in headers)
            {
                if (string.IsNullOrWhiteSpace(header.Key) || string.IsNullOrWhiteSpace(header.Value))
                    continue;

                if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
                    throw new ArgumentException($"无效的 HTTP 请求头: {header.Key}", nameof(headers));
            }
        }
    }
}
