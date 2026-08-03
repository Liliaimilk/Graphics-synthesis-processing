using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WindowsFormsApp1
{
    /// <summary>
    /// 应用程序启动前的账号登录窗口。
    /// </summary>
    internal sealed class LoginDialog : Form
    {
        private TextBox txtUsername;
        private TextBox txtPassword;
        private CheckBox chkRememberPassword;
        private Button btnLogin;
        private Button btnCancel;
        private Label lblStatus;
        private bool isLoggingIn;

        public LoginDialog()
        {
            InitializeDialog();
            LoadSavedCredentials();
        }

        /// <summary>
        /// 初始化符合主界面深蓝灰风格的登录界面。
        /// </summary>
        private void InitializeDialog()
        {
            Text = "登录 - 图片处理工具";
            ClientSize = new Size(430, 350);
            MinimumSize = Size;
            MaximumSize = Size;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(25, 35, 55);
            ForeColor = Color.FromArgb(220, 225, 235);
            Font = new Font("微软雅黑", 9.5F);

            var accentBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 5,
                BackColor = Color.FromArgb(45, 120, 190)
            };
            Controls.Add(accentBar);

            var title = new Label
            {
                Text = "图片处理工具",
                Location = new Point(40, 38),
                Size = new Size(350, 36),
                Font = new Font("微软雅黑", 18F, FontStyle.Bold),
                ForeColor = Color.FromArgb(235, 240, 248),
                TextAlign = ContentAlignment.MiddleLeft
            };
            Controls.Add(title);

            var subtitle = new Label
            {
                Text = "请使用工厂账号登录后继续使用",
                Location = new Point(42, 76),
                Size = new Size(340, 24),
                ForeColor = Color.FromArgb(150, 170, 195),
                TextAlign = ContentAlignment.MiddleLeft
            };
            Controls.Add(subtitle);

            Controls.Add(CreateFieldLabel("账号", 42, 118));
            txtUsername = CreateInputBox(42, 142);
            txtUsername.TabIndex = 0;
            Controls.Add(txtUsername);

            Controls.Add(CreateFieldLabel("密码", 42, 184));
            txtPassword = CreateInputBox(42, 208);
            txtPassword.PasswordChar = '*';
            txtPassword.TabIndex = 1;
            Controls.Add(txtPassword);

            chkRememberPassword = new CheckBox
            {
                Text = "记住密码",
                Location = new Point(42, 252),
                Size = new Size(110, 24),
                ForeColor = Color.FromArgb(200, 210, 225),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand,
                TabIndex = 2
            };
            chkRememberPassword.CheckedChanged += ChkRememberPassword_CheckedChanged;
            Controls.Add(chkRememberPassword);

            lblStatus = new Label
            {
                Location = new Point(155, 252),
                Size = new Size(232, 24),
                ForeColor = Color.FromArgb(150, 170, 195),
                TextAlign = ContentAlignment.MiddleRight
            };
            Controls.Add(lblStatus);

            btnLogin = CreateButton("登录", new Point(42, 292), Color.FromArgb(45, 100, 160), Color.FromArgb(55, 120, 180));
            btnLogin.Size = new Size(240, 34);
            btnLogin.TabIndex = 3;
            btnLogin.Click += BtnLogin_Click;
            Controls.Add(btnLogin);

            btnCancel = CreateButton("退出", new Point(292, 292), Color.FromArgb(45, 60, 85), Color.FromArgb(60, 78, 108));
            btnCancel.Size = new Size(96, 34);
            btnCancel.TabIndex = 4;
            btnCancel.DialogResult = DialogResult.Cancel;
            Controls.Add(btnCancel);

            AcceptButton = btnLogin;
            CancelButton = btnCancel;
        }

        /// <summary>
        /// 读取上次保存的账号和可选加密密码。
        /// </summary>
        private void LoadSavedCredentials()
        {
            StoredLoginCredentials credentials = LoginCredentialStore.Load();
            txtUsername.Text = credentials.Username;
            txtPassword.Text = credentials.Password ?? string.Empty;
            chkRememberPassword.Checked = credentials.RememberPassword;
            ActiveControl = string.IsNullOrWhiteSpace(txtUsername.Text) ? txtUsername : txtPassword;
        }

        /// <summary>
        /// 取消记住密码时立即清除本地加密密码，防止下次自动回填。
        /// </summary>
        private void ChkRememberPassword_CheckedChanged(object sender, EventArgs e)
        {
            if (!chkRememberPassword.Checked)
                LoginCredentialStore.ClearRememberedPassword();
        }

        /// <summary>
        /// 校验输入并发起异步登录请求。
        /// </summary>
        private async void BtnLogin_Click(object sender, EventArgs e)
        {
            if (isLoggingIn)
                return;

            string username = txtUsername.Text.Trim();
            string password = txtPassword.Text;
            if (string.IsNullOrWhiteSpace(username))
            {
                ShowInputError("请输入账号。", txtUsername);
                return;
            }
            if (string.IsNullOrWhiteSpace(password))
            {
                ShowInputError("请输入密码。", txtPassword);
                return;
            }

            SetLoggingState(true, "正在登录...");
            try
            {
                LoginUser user = await LoginService.LoginAsync(username, password);
                LoginCredentialStore.Save(user, username, password, chkRememberPassword.Checked);
                lblStatus.ForeColor = Color.FromArgb(120, 220, 160);
                lblStatus.Text = "登录成功：" + user.Username;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                lblStatus.ForeColor = Color.FromArgb(255, 155, 155);
                lblStatus.Text = "登录失败，请检查账号或网络。";
                MessageBox.Show(ex.Message, "登录失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtPassword.SelectAll();
                txtPassword.Focus();
            }
            finally
            {
                if (!IsDisposed)

                    SetLoggingState(false, lblStatus.Text);
            }
        }

        /// <summary>
        /// 切换登录请求期间的控件可用状态。
        /// </summary>
        private void SetLoggingState(bool loggingIn, string statusText)
        {
            isLoggingIn = loggingIn;
            txtUsername.Enabled = !loggingIn;
            txtPassword.Enabled = !loggingIn;
            chkRememberPassword.Enabled = !loggingIn;
            btnLogin.Enabled = !loggingIn;
            btnCancel.Enabled = !loggingIn;
            lblStatus.ForeColor = Color.FromArgb(150, 170, 195);
            lblStatus.Text = statusText;
        }

        /// <summary>
        /// 显示输入校验提示并将焦点返回到对应输入框。
        /// </summary>
        private void ShowInputError(string message, Control control)
        {
            lblStatus.ForeColor = Color.FromArgb(255, 155, 155);
            lblStatus.Text = message;
            control.Focus();
        }

        /// <summary>
        /// 创建统一样式的表单标签。
        /// </summary>
        private static Label CreateFieldLabel(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(100, 22),
                ForeColor = Color.FromArgb(190, 205, 225),
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        /// <summary>
        /// 创建统一样式的单行输入框。
        /// </summary>
        private static TextBox CreateInputBox(int x, int y)
        {
            return new TextBox
            {
                Location = new Point(x, y),
                Size = new Size(346, 26),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(35, 48, 72),
                ForeColor = Color.FromArgb(235, 240, 248),
                Font = new Font("微软雅黑", 10F)
            };
        }

        /// <summary>
        /// 创建带悬停色的深色主题按钮。
        /// </summary>
        private static Button CreateButton(string text, Point location, Color backColor, Color hoverColor)
        {
            var button = new Button
            {
                Text = text,
                Location = location,
                BackColor = backColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font("微软雅黑", 10F)
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(75, 120, 175);
            button.FlatAppearance.MouseOverBackColor = hoverColor;
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(35, 85, 140);
            return button;
        }
    }
}
