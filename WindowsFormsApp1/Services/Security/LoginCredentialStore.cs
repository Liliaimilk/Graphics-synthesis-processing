using System;
using System.Security.Cryptography;
using System.Text;

namespace WindowsFormsApp1
{
    /// <summary>
    /// 登录窗口使用的本地凭据数据。
    /// </summary>
    internal sealed class StoredLoginCredentials
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public bool RememberPassword { get; set; }

        public int UserId { get; set; }
    }

    /// <summary>
    /// 使用当前 Windows 用户的 DPAPI 安全保存登录账号和可选密码。
    /// </summary>
    internal static class LoginCredentialStore
    {
        /// <summary>
        /// 读取已保存的账号和密码；无法解密的旧密码会自动清除。
        /// </summary>
        public static StoredLoginCredentials Load()
        {
            var settings = Properties.Settings.Default;
            var credentials = new StoredLoginCredentials
            {
                Username = settings.LastLoginUsername ?? string.Empty,
                RememberPassword = settings.RememberPassword,
                UserId = settings.UserId
            };

            if (!credentials.RememberPassword || string.IsNullOrWhiteSpace(settings.EncryptedPassword))
                return credentials;

            try
            {
                byte[] encryptedBytes = Convert.FromBase64String(settings.EncryptedPassword);
                byte[] passwordBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
                credentials.Password = Encoding.UTF8.GetString(passwordBytes);
            }
            catch (Exception ex) when (ex is FormatException || ex is CryptographicException)
            {
                ClearRememberedPassword();
                credentials.RememberPassword = false;
            }

            return credentials;
        }

        /// <summary>
        /// 保存账号；勾选记住密码时加密保存密码，未勾选时清除历史密码。
        /// </summary>
        public static void Save(LoginUser res,string username, string password, bool rememberPassword)
        {
            var settings = Properties.Settings.Default;
            settings.LastLoginUsername = username?.Trim() ?? string.Empty;
            settings.RememberPassword = rememberPassword;
            settings.UserId = res.UserId;
            if (rememberPassword && !string.IsNullOrEmpty(password))
            {
                byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
                byte[] encryptedBytes = ProtectedData.Protect(passwordBytes, null, DataProtectionScope.CurrentUser);
                settings.EncryptedPassword = Convert.ToBase64String(encryptedBytes);
            }
            else
            {
                settings.EncryptedPassword = string.Empty;
            }

            settings.Save();
        }

        /// <summary>
        /// 清除已保存的加密密码，但保留最后使用的账号。
        /// </summary>
        public static void ClearRememberedPassword()
        {
            var settings = Properties.Settings.Default;
            settings.RememberPassword = false;
            settings.EncryptedPassword = string.Empty;
            settings.Save();
        }
    }
}
