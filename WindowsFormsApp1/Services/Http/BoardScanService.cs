using System;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace WindowsFormsApp1
{
    /// <summary>
    /// 排版扫码接口返回的业务结果。
    /// </summary>
    internal sealed class BoardScanResult
    {
        public int Code { get; set; }
        public string Message { get; set; }
        public string SkuName { get; set; }
        public string DataMessage { get; set; }
    }

    /// <summary>
    /// 调用标签码校验接口，并返回可供排版窗口判断的业务结果。
    /// </summary>
    internal static class BoardScanService
    {
        [DataContract]
        private sealed class BoardScanRequest
        {
            [DataMember(Name = "labelCode")]
            public string LabelCode { get; set; }
        }

        [DataContract]
        private sealed class BoardScanResponse
        {
            [DataMember(Name = "msg")]
            public string Message { get; set; }

            [DataMember(Name = "code")]
            public int Code { get; set; }

            [DataMember(Name = "data")]
            public BoardScanData Data { get; set; }
        }

        [DataContract]
        private sealed class BoardScanData
        {
            [DataMember(Name = "message")]
            public string Message { get; set; }

            [DataMember(Name = "skuName")]
            public string SkuName { get; set; }
        }

        /// <summary>
        /// 使用标签码查询对应 SKU；业务失败不抛异常，由界面显示服务端提示。
        /// </summary>
        public static async Task<BoardScanResult> ScanAsync(string labelCode)
        {
            if (string.IsNullOrWhiteSpace(labelCode))
                throw new ArgumentException("请输入标签码。", nameof(labelCode));

            BoardScanResponse response = await ApiClient.PostAsync<BoardScanRequest, BoardScanResponse>(
                ApiEndpoints.ScanBoard,
                new BoardScanRequest { LabelCode = labelCode.Trim() });
            if (response == null)
                throw new InvalidOperationException("标签码接口未返回有效数据。");

            return new BoardScanResult
            {
                Code = response.Code,
                Message = response.Message,
                SkuName = response.Data?.SkuName,
                DataMessage = response.Data?.Message
            };
        }
    }
}
