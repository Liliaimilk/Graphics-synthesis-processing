using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace WindowsFormsApp1
{
    /// <summary>
    /// 主界面的订单扫码 HTTP 业务逻辑。
    /// 通用请求、地址拼接和 JSON 处理由 ApiClient 统一负责。
    /// </summary>
    public partial class Form1
    {
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
        /// 根据订单号查询 SKU，并校验服务端业务状态码。
        /// </summary>
        private async Task<List<OrderScanResult>> GetOrderScanResultsAsync(string orderNumber)
        {
            string query = "orderNum=" + Uri.EscapeDataString(orderNumber);
            OrderScanResponse response = await ApiClient.GetAsync<OrderScanResponse>(ApiEndpoints.OrderScanResult, query);

            if (response == null)
                throw new InvalidOperationException("订单接口未返回有效数据。");
            if (response.Code != 200)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(response.Message) ? "订单查询未成功。" : response.Message);
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
    }
}
