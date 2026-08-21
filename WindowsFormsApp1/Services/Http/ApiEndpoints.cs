namespace WindowsFormsApp1
{
    /// <summary>
    /// 集中管理业务服务地址和接口路径，业务代码只引用路径常量。
    /// </summary>
    internal static class ApiEndpoints
    {
        public const string ServiceBaseUrl = "http://192.168.0.115:8080";
        public const string Login = "/api/factory-client/login";
        public const string OrderScanResult = "/open-api/orders/scan-result";
        public const string ScanBoard = "/open-api/fulfillment/scan-board";
    }
}
