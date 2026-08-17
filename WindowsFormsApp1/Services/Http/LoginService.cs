using System;
using System.Runtime.Serialization;
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

            LoginResponse loginResponse = await ApiClient.PostAsync<LoginRequest, LoginResponse>(
                ApiEndpoints.Login,
                new LoginRequest
                {
                    Username = username.Trim(),
                    Password = password
                });
            if (loginResponse == null)
                throw new InvalidOperationException("登录接口未返回有效数据。");
            if (loginResponse.Code != 200)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(loginResponse.Message) ? "登录失败。" : loginResponse.Message);
            if (loginResponse.Data == null || string.IsNullOrWhiteSpace(loginResponse.Data.Username))
                throw new InvalidOperationException("登录接口未返回有效用户信息。");

            return loginResponse.Data;
        }
    }
}
