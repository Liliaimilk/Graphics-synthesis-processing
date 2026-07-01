using RulerGridApp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WindowsFormsApp1
{
    [DataContract]
    public sealed class RemoteMergeMessage
    {
        [DataMember(Name = "materialName")]
        public string MaterialName { get; set; }

        [DataMember(Name = "materialNames")]
        public string[] MaterialNames { get; set; }

        [DataMember(Name = "templateName")]
        public string TemplateName { get; set; }

        [DataMember(Name = "format")]
        public string Format { get; set; }

        [DataMember(Name = "compositeMode")]
        public string CompositeMode { get; set; }

        [DataMember(Name = "whiteInk")]
        public bool? WhiteInk { get; set; }

        [DataMember(Name = "varnish")]
        public bool? Varnish { get; set; }

        [DataMember(Name = "separator")]
        public string Separator { get; set; }
    }

    public sealed class RemoteMergeRequest
    {
        public string TemplateFolder { get; set; }
        public string MaterialFolder { get; set; }
        public string SavePath { get; set; }
        public List<string> MaterialNames { get; set; } = new List<string>();
        public string TemplateName { get; set; }
        public string Format { get; set; }
        public string CompositeMode { get; set; }
        public bool? WhiteInk { get; set; }
        public bool? Varnish { get; set; }
        public string Separator { get; set; }
        public string RawJson { get; set; }

        public string DisplayName
        {
            get
            {
                if (MaterialNames == null || MaterialNames.Count == 0)
                {
                    return "未命名请求";
                }

                return string.Join(", ", MaterialNames);
            }
        }
    }

    public partial class Form1 : Form
    {
        //"ws://192.168.0.222:8080/websocket/2"
        private const string RemoteWebSocketEndpoint = null;

        private RulerCanvas canvas;
        private Panel workspacePanel;
        private Panel leftToolPanel;
        private Label lblStatus;
        private Label lblZoom;
        private Button btnMergeTool;
        private Button btnLayoutOutputTool;
        private Button btnMoveTool;
        private ToolTip toolbarToolTip;

        private TrackBar zoomTrackBar;
        private Label zoomLabel;
        private Button resetZoomButton;
        private CanvasTool currentTool = CanvasTool.Move;
        private readonly Queue<RemoteMergeRequest> remoteMergeQueue = new Queue<RemoteMergeRequest>();
        private ClientWebSocket remoteWebSocketClient;
        private CancellationTokenSource remoteWebSocketCts;
        private Task remoteWebSocketReceiveTask;
        private bool isProcessingRemoteQueue;
        private MergeDialog activeRemoteDialog;

        public Form1()
        {
            SetupDarkTheme();
            SetupCanvas();
            SetupToolbar();
            SetupStatusBar();
            SetupDragDrop();
            this.Shown += Form1_Shown;
            this.FormClosing += Form1_FormClosing;
        }

        private void SetupDragDrop()
        {
            // RulerCanvas 已经内置拖放支持，这里不需要额外处理
        }

        private void SetupDarkTheme()
        {
            this.Text = "图片处理工具";
            this.Size = new Size(1280, 800);
            this.MinimumSize = new Size(1024, 700);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(25, 35, 55);
            this.ForeColor = Color.FromArgb(220, 225, 235);

            typeof(Control).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, this, new object[] { true });
        }

        private void SetupToolbar()
        {
            Panel toolbarPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 48,
                BackColor = Color.FromArgb(30, 40, 60)
            };
            this.Controls.Add(toolbarPanel);

            btnMergeTool = new Button
            {
                Text = "套图",
                Location = new Point(10, 10),
                Size = new Size(80, 28),
                Font = new Font("微软雅黑", 10F),
                BackColor = Color.FromArgb(45, 100, 160),
                ForeColor = Color.FromArgb(220, 225, 235),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Tag = "套图"
            };
            btnMergeTool.FlatAppearance.BorderColor = Color.FromArgb(70, 140, 200);
            btnMergeTool.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 120, 180);
            btnMergeTool.Click += BtnMergeTool_Click;

            btnLayoutOutputTool = new Button
            {
                Text = "排版输出",
                Location = new Point(100, 10),
                Size = new Size(96, 28),
                Font = new Font("微软雅黑", 10F),
                BackColor = Color.FromArgb(45, 100, 160),
                ForeColor = Color.FromArgb(220, 225, 235),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Tag = "排版输出"
            };
            btnLayoutOutputTool.FlatAppearance.BorderColor = Color.FromArgb(70, 140, 200);
            btnLayoutOutputTool.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 120, 180);
            btnLayoutOutputTool.Click += BtnLayoutOutputTool_Click;

            zoomLabel = new Label
            {
                Text = "缩放: 100%",
                Location = new Point(220, 10),
                Size = new Size(60, 28),
                Font = new Font("微软雅黑", 9F),
                ForeColor = Color.FromArgb(200, 205, 215),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };

            zoomTrackBar = new TrackBar
            {
                Location = new Point(285, 10),
                Minimum = 10,
                Maximum = 300,
                Value = 10,
                TickFrequency = 25,
                Width = 200,
                Height = 30
            };
            zoomTrackBar.ValueChanged += ZoomTrackBar_ValueChanged;

            resetZoomButton = new Button
            {
                Text = "重置",
                Location = new Point(495, 10),
                Size = new Size(50, 28),
                Font = new Font("微软雅黑", 9F),
                BackColor = Color.FromArgb(40, 55, 80),
                ForeColor = Color.FromArgb(220, 225, 235),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            resetZoomButton.Click += (s, e) => canvas?.ResetView();

            toolbarPanel.Controls.Add(btnMergeTool);
            toolbarPanel.Controls.Add(btnLayoutOutputTool);
            toolbarPanel.Controls.Add(zoomLabel);
            toolbarPanel.Controls.Add(zoomTrackBar);
            toolbarPanel.Controls.Add(resetZoomButton);

            if (canvas != null)
            {
                canvas.ZoomChanged += (zoom) =>
                {
                    zoomTrackBar.Value = (int)Math.Min(300, Math.Max(10, zoom * 100));
                    zoomLabel.Text = $"缩放: {(int)(zoom * 100)}%";
                };
            }
        }

        private void SetupCanvas()
        {
            workspacePanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(25, 35, 55)
            };
            this.Controls.Add(workspacePanel);

            toolbarToolTip = new ToolTip();

            canvas = new RulerCanvas
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(35, 40, 50),
                ActiveTool = CanvasTool.Move
            };
            workspacePanel.Controls.Add(canvas);

            leftToolPanel = new Panel
            {
                Dock = DockStyle.Left,
                Width = 52,
                BackColor = Color.FromArgb(58, 58, 58),
                Padding = new Padding(4, 8, 4, 4)
            };
            workspacePanel.Controls.Add(leftToolPanel);

            btnMoveTool = new Button
            {
                Text = "✥",
                Dock = DockStyle.Top,
                Height = 44,
                Font = new Font("Segoe UI Symbol", 13F, FontStyle.Regular),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter,
                UseCompatibleTextRendering = true,
                Margin = new Padding(0)
            };
            btnMoveTool.FlatAppearance.BorderSize = 0;
            btnMoveTool.Click += (s, e) => SetCanvasTool(CanvasTool.Move);
            toolbarToolTip.SetToolTip(btnMoveTool, "移动工具");
            leftToolPanel.Controls.Add(btnMoveTool);

            SetCanvasTool(CanvasTool.Move);
        }

        private void SetupStatusBar()
        {
            Panel statusPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 28,
                BackColor = Color.FromArgb(25, 35, 55)
            };
            this.Controls.Add(statusPanel);

            lblStatus = new Label
            {
                Text = "就绪",
                Location = new Point(10, 5),
                Size = new Size(300, 18),
                Font = new Font("微软雅黑", 9F),
                ForeColor = Color.FromArgb(150, 155, 165),
                BackColor = Color.Transparent
            };

            lblZoom = new Label
            {
                Text = "100%",
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(statusPanel.Width - 80, 5),
                Size = new Size(70, 18),
                Font = new Font("微软雅黑", 9F),
                ForeColor = Color.FromArgb(150, 155, 165),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleRight
            };

            statusPanel.Controls.Add(lblStatus);
            statusPanel.Controls.Add(lblZoom);
            canvas.ZoomChanged += (zoom) => lblZoom.Text = $"{Math.Round(zoom * 100)}%";
        }

        private async void Form1_Shown(object sender, EventArgs e)
        {
            await StartRemoteWebSocketServerAsync();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            StopRemoteWebSocketServer();
            activeRemoteDialog?.Close();
        }

        private async Task StartRemoteWebSocketServerAsync()
        {
            if (remoteWebSocketReceiveTask != null && !remoteWebSocketReceiveTask.IsCompleted)
            {
                return;
            }

            try
            {
                remoteWebSocketCts?.Dispose();
                remoteWebSocketCts = new CancellationTokenSource();
                remoteWebSocketReceiveTask = Task.Run(() => RunRemoteWebSocketClientLoopAsync(remoteWebSocketCts.Token));
                lblStatus.Text = "正在连接远程WebSocket...";
            }
            catch (Exception ex)
            {
                lblStatus.Text = "WebSocket启动失败";
                MessageBox.Show($"无法启动 WebSocket 客户端: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                StopRemoteWebSocketServer();
            }

            await Task.CompletedTask;
        }

        private void StopRemoteWebSocketServer()
        {
            try
            {
                remoteWebSocketCts?.Cancel();
            }
            catch { }

            try
            {
                if (remoteWebSocketClient != null)
                {
                    if (remoteWebSocketClient.State == WebSocketState.Open || remoteWebSocketClient.State == WebSocketState.CloseReceived)
                    {
                        remoteWebSocketClient.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None).GetAwaiter().GetResult();
                    }

                    remoteWebSocketClient.Dispose();
                }
            }
            catch { }

            remoteWebSocketClient = null;
            remoteWebSocketReceiveTask = null;
            remoteWebSocketCts?.Dispose();
            remoteWebSocketCts = null;
        }

        private async Task RunRemoteWebSocketClientLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ClientWebSocket client = null;
                try
                {
                    client = new ClientWebSocket();
                    remoteWebSocketClient = client;
                    SafeUpdateStatus($"正在连接: {RemoteWebSocketEndpoint}");
                    await client.ConnectAsync(new Uri(RemoteWebSocketEndpoint), cancellationToken);
                    SafeUpdateStatus("远程WebSocket已连接");
                    await ReceiveRemoteMessagesAsync(client, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (WebSocketException ex)
                {
                    SafeUpdateStatus($"WebSocket连接失败，稍后重试: {ex.Message}");
                }
                catch (Exception ex)
                {
                    SafeUpdateStatus($"WebSocket运行异常，稍后重试: {ex.Message}");
                }
                finally
                {
                    if (remoteWebSocketClient == client)
                    {
                        remoteWebSocketClient = null;
                    }

                    try
                    {
                        client?.Dispose();
                    }
                    catch { }
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task ReceiveRemoteMessagesAsync(WebSocket webSocket, CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];
            var builder = new StringBuilder();

            while (webSocket != null && webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                builder.Clear();
                WebSocketReceiveResult result;
                do
                {
                    result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived)
                        {
                            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
                        }
                        return;
                    }

                    if (result.MessageType != WebSocketMessageType.Text)
                    {
                        continue;
                    }

                    builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }
                while (!result.EndOfMessage);

                if (builder.Length == 0)
                {
                    continue;
                }

                try
                {
                    RemoteMergeRequest request = CreateRemoteMergeRequest(builder.ToString());
                    Console.WriteLine($"result:{request.ToString()}" );

                    BeginInvoke(new Action(() => EnqueueRemoteMergeRequest(request)));
                }
                catch (Exception ex)
                {
                    SafeUpdateStatus($"远程消息解析失败: {ex.Message}");
                }
            }
        }

        private RemoteMergeRequest CreateRemoteMergeRequest(string rawJson)
        {
            RemoteMergeMessage message = DeserializeRemoteMergeMessage(rawJson);
            var settings = Properties.Settings.Default;
            List<string> materialNames = new List<string>();

            if (!string.IsNullOrWhiteSpace(message?.MaterialName))
            {
                materialNames.Add(message.MaterialName.Trim());
            }

            if (message?.MaterialNames != null)
            {
                materialNames.AddRange(message.MaterialNames
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name.Trim()));
            }

            materialNames = materialNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (materialNames.Count == 0)
            {
                throw new InvalidOperationException("消息中未提供素材名");
            }

            return new RemoteMergeRequest
            {
                TemplateFolder = settings.TemplateFolder,
                MaterialFolder = settings.MaterialFolder,
                SavePath = settings.SavePath,
                MaterialNames = materialNames,
                TemplateName = message?.TemplateName,
                Format = message?.Format,
                CompositeMode = message?.CompositeMode,
                WhiteInk = message?.WhiteInk,
                Varnish = message?.Varnish,
                Separator = message?.Separator,
                RawJson = rawJson
            };
        }

        private RemoteMergeMessage DeserializeRemoteMergeMessage(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                throw new InvalidOperationException("消息内容为空");
            }

            var serializer = new DataContractJsonSerializer(typeof(RemoteMergeMessage));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(rawJson)))
            {
                return serializer.ReadObject(stream) as RemoteMergeMessage;
            }
        }

        private void EnqueueRemoteMergeRequest(RemoteMergeRequest request)
        {
            if (request == null)
            {
                return;
            }

            remoteMergeQueue.Enqueue(request);
            lblStatus.Text = $"远程请求排队: {request.DisplayName}";

            if (!isProcessingRemoteQueue)
            {
                _ = ProcessRemoteMergeQueueAsync();
            }
        }

        private async Task ProcessRemoteMergeQueueAsync()
        {
            if (isProcessingRemoteQueue)
            {
                return;
            }

            isProcessingRemoteQueue = true;
            try
            {
                while (remoteMergeQueue.Count > 0 && !IsDisposed)
                {
                    RemoteMergeRequest request = remoteMergeQueue.Dequeue();
                    lblStatus.Text = $"处理远程请求: {request.DisplayName}";

                    if (activeRemoteDialog == null || activeRemoteDialog.IsDisposed)
                    {
                        Console.WriteLine($"{nameof(activeRemoteDialog)} is null or disposed, creating a new instance.");
                        activeRemoteDialog = new MergeDialog();
                        activeRemoteDialog.Owner = this;
                    }

                    try
                    {
                        Console.WriteLine($"Applying remote request: {request.DisplayName}");
                        activeRemoteDialog.ApplyRemoteRequest(request);
                        if (!activeRemoteDialog.Visible)
                        {
                            Console.WriteLine("Showing active remote dialog.");
                            activeRemoteDialog.Show(this);
                        }

                        activeRemoteDialog.BringToFront();
                        Console.WriteLine("Starting remote run...");
                        bool completed = await activeRemoteDialog.StartRemoteRunAsync();
                        Console.WriteLine($"Remote run completed: {completed}");
                        if (!completed)
                        {
                            lblStatus.Text = $"远程请求失败: {activeRemoteDialog.GetStatusTextSnapshot()}";
                        }
                        if (completed)
                        {
                            if (activeRemoteDialog.ResultPaths != null && activeRemoteDialog.ResultPaths.Count > 1)
                            {
                                LoadResultsToCanvas(activeRemoteDialog.ResultPaths);
                            }
                            else if (!string.IsNullOrEmpty(activeRemoteDialog.ResultPath))
                            {
                                LoadResultToCanvas(activeRemoteDialog.ResultPath);
                            }
                        }
                    }
                    finally
                    {
                        if (activeRemoteDialog != null && !activeRemoteDialog.IsDisposed && activeRemoteDialog.Visible)
                        {
                            //activeRemoteDialog.Hide();
                        }
                    }
                }
            }
            finally
            {
                isProcessingRemoteQueue = false;
                if (activeRemoteDialog != null && !activeRemoteDialog.IsDisposed)
                {
                    activeRemoteDialog.Dispose();
                    activeRemoteDialog = null;
                }
            }
        }

        private void SafeUpdateStatus(string message)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(SafeUpdateStatus), message);
                return;
            }

            lblStatus.Text = message;
        }

        private void BtnMergeTool_Click(object sender, EventArgs e)
        {
            using (var dialog = new MergeDialog())
            {
                dialog.Owner = this;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    if (dialog.ResultPaths != null && dialog.ResultPaths.Count > 1)
                    {
                        LoadResultsToCanvas(dialog.ResultPaths);
                    }
                    else if (!string.IsNullOrEmpty(dialog.ResultPath))
                    {
                        LoadResultToCanvas(dialog.ResultPath);
                    }
                }
            }
        }

        private void BtnLayoutOutputTool_Click(object sender, EventArgs e)
        {
            using (var dialog = new LayoutOutputDialog())
            {
                dialog.Owner = this;
                if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dialog.ResultPath))
                {
                    LoadResultToCanvas(dialog.ResultPath);
                }
            }
        }

        private void LoadResultToCanvas(string path)
        {
            LoadResultsToCanvas(new[] { path });
        }

        private void LoadResultsToCanvas(IEnumerable<string> paths)
        {
            try
            {
                var validPaths = (paths ?? Enumerable.Empty<string>())
                    .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    .ToList();

                if (validPaths.Count == 0)
                {
                    return;
                }

                lblStatus.Text = "加载图片...";
                Application.DoEvents();

                if (validPaths.Count == 1)
                {
                    canvas.LoadImageFromFile(validPaths[0]);
                }
                else
                {
                    canvas.LoadImagesHorizontally(validPaths);
                }

                lblStatus.Text = validPaths.Count == 1 ? "完成！" : $"已加载 {validPaths.Count} 张图片";
            }
            catch (Exception ex)
            {
                lblStatus.Text = "加载失败";
                MessageBox.Show($"加载图片失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ZoomTrackBar_ValueChanged(object sender, EventArgs e)
        {
            if (canvas != null)
            {
                float newZoom = zoomTrackBar.Value / 100f;
                canvas.SetZoom(newZoom);
                zoomLabel.Text = $"缩放: {zoomTrackBar.Value}%";
            }
        }

        private void SetCanvasTool(CanvasTool tool)
        {
            currentTool = tool;

            if (canvas != null)
            {
                canvas.ActiveTool = tool;
            }

            if (btnMoveTool != null)
            {
                bool isActive = tool == CanvasTool.Move;
                btnMoveTool.BackColor = isActive ? Color.FromArgb(20, 20, 20) : Color.FromArgb(90, 90, 90);
                btnMoveTool.FlatAppearance.BorderColor = isActive ? Color.FromArgb(20, 20, 20) : Color.FromArgb(90, 90, 90);
                btnMoveTool.FlatAppearance.MouseOverBackColor = isActive ? Color.FromArgb(32, 32, 32) : Color.FromArgb(110, 110, 110);
            }
        }
    }

    public class MergeDialog : Form
    {
        private const int DialogWidth = 790;
        private const int SingleDialogHeight = 560;
        private const int BatchDialogHeight = 930;
        private const int ChannelCardHeight = 44;
        private const int ChannelCardGap = 8;
        private const int SingleDialogBottomPadding = 144;
        private const int BatchDialogBottomPadding = 95;

        List<string> channelNames = new List<string>();

        private TextBox txtTemplateFolder;
        private TextBox txtMaterialFolder;
        private TextBox txtSavePath;
        private TextBox txtSeparator;
        private ComboBox cmbFormat;
        private ComboBox cmbCompositeMode;

        private ComboBox cmbRotation;

        private ComboBox cmbMirror;
        private CheckBox chkWhiteInk;
        private CheckBox chkVarnish;

        private Button addChannels;

        private CheckBox chkBatchMode;
        private Button btnMerge;
        private Label lblStatus;
        private Button btnClose;
        private Button btnBrowseTemplate;
        private Button btnBrowseMaterial;
        private Button btnBrowseSave;
        private Panel pnlBatch;
        private ProgressBar prgBatch;
        private Label lblBatchSummary;
        private ListView lvResults;
        private Button btnPauseResume;
        private Button btnCancel;

        private Panel channelListPanel;
        private Label channelSectionLabel;

        private bool isRunning;
        private bool canReturnResults;
        private bool isRemoteMode;
        private BatchRunState batchState;
        private RemoteMergeRequest pendingRemoteRequest;

        private readonly List<ChannelControl> channelControls = new List<ChannelControl>();
        private int nextChannelNumber = 1;
        private readonly string[] imageExtensions = { ".psd", ".psb", ".tif", ".tiff", ".jpg", ".jpeg", ".png", ".bmp" };

        public string ResultPath { get; private set; }
        public IReadOnlyList<string> ResultPaths { get; private set; } = Array.Empty<string>();

        private enum MergeJobStatus
        {
            Pending,
            Validating,
            Running,
            Completed,
            Failed,
            Canceled,
            Skipped
        }

        private sealed class MergeJobItem
        {
            public int Index { get; set; }
            public string TemplatePath { get; set; }
            public string MaterialPath { get; set; }
            public string OutputPath { get; set; }
            public MergeJobStatus Status { get; set; }
            public string Message { get; set; }
            public ListViewItem ListItem { get; set; }
        }

        private sealed class BatchRunState : IDisposable
        {
            public BatchRunState(List<MergeJobItem> jobs)
            {
                Jobs = jobs ?? new List<MergeJobItem>();
                SuccessOutputPaths = new List<string>();
                CancellationSource = new CancellationTokenSource();
                PauseGate = new ManualResetEventSlim(true);
            }

            public List<MergeJobItem> Jobs { get; }
            public List<string> SuccessOutputPaths { get; }
            public CancellationTokenSource CancellationSource { get; }
            public ManualResetEventSlim PauseGate { get; }
            public bool IsPaused { get; set; }

            public void Dispose()
            {
                PauseGate.Dispose();
                CancellationSource.Dispose();
            }
        }

        private sealed class BuildJobsResult
        {
            public bool IsBatchMode { get; set; }
            public string TemplateFile { get; set; }
            public List<MergeJobItem> Jobs { get; set; }
            public string Format { get; set; }
            public string CompositeModeName { get; set; }
            public TemplateCompositeMode CompositeMode { get; set; }
            public string ExclusionMaskPath { get; set; }

            public string Rotation { get; set; }
            public string Mirror { get; set; }
        }

        private sealed class ValidationResult
        {
            public bool IsValid { get; set; }
            public string ErrorMessage { get; set; }
        }

        public MergeDialog()
        {
            SetupDarkTheme();
            SetupControls();
            LoadSavedPaths();
            this.FormClosing += MergeDialog_FormClosing;
        }

        // 获取远程消息后，请求处理
        public void ApplyRemoteRequest(RemoteMergeRequest request)
        {
            pendingRemoteRequest = request;
            isRemoteMode = request != null;
            ResetDialogResultState();

            if (request == null)
            {
                lblStatus.Text = "远程请求为空";
                return;
            }
            Console.WriteLine($"{request},request");
            // 文件路径以及选项接收赋值

            txtTemplateFolder.Text = request.TemplateFolder ?? string.Empty;
            txtMaterialFolder.Text = request.MaterialFolder ?? string.Empty;
            txtSavePath.Text = request.SavePath ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(request.Separator))
            {
                txtSeparator.Text = request.Separator;
            }

            if (!string.IsNullOrWhiteSpace(request.Format))
            {
                SelectFormat(request.Format);
            }

            if (!string.IsNullOrWhiteSpace(request.CompositeMode))
            {
                SelectCompositeMode(request.CompositeMode);
            }

            // if (request.WhiteInk.HasValue)
            // {
            //     chkWhiteInk.Checked = request.WhiteInk.Value;
            // }

            // if (request.Varnish.HasValue)
            // {
            //     chkVarnish.Checked = request.Varnish.Value;
            // }

            chkBatchMode.Checked = request.MaterialNames != null && request.MaterialNames.Count > 1;
            lblStatus.Text = $"远程请求已加载: {request.DisplayName}";
        }

        public async Task<bool> StartRemoteRunAsync()
        {
            if (isRunning)
            {
                lblStatus.Text = "当前任务正在执行";
                return false;
            }

            if (pendingRemoteRequest == null)
            {
                lblStatus.Text = "没有待执行的远程请求";
                return false;
            }

            ResetDialogResultState();
            BuildJobsResult buildResult = BuildJobsFromRemoteRequest(pendingRemoteRequest);
            if (buildResult == null)
            {
                pendingRemoteRequest = null;
                return false;
            }

            await ExecuteJobsAsync(buildResult);
            pendingRemoteRequest = null;
            return canReturnResults;
        }

        public string GetStatusTextSnapshot()
        {
            return lblStatus?.Text ?? string.Empty;
        }

        private void SelectFormat(string format)
        {
            string normalized = (format ?? string.Empty).Trim().ToUpperInvariant();
            for (int i = 0; i < cmbFormat.Items.Count; i++)
            {
                string item = cmbFormat.Items[i]?.ToString() ?? string.Empty;
                if (string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    cmbFormat.SelectedIndex = i;
                    return;
                }
            }
        }

        private void SelectCompositeMode(string compositeMode)
        {
            if (cmbCompositeMode != null && cmbCompositeMode.Items.Count > 1)
                cmbCompositeMode.SelectedIndex = 1;
        }

        private void ShowStatusMessage(string statusText, string dialogMessage, string title, MessageBoxIcon icon)
        {
            lblStatus.Text = statusText;
            if (!isRemoteMode)
            {
                MessageBox.Show(dialogMessage, title, MessageBoxButtons.OK, icon);
            }
        }

        private bool ValidateSelectedFolders()
        {
            if (string.IsNullOrEmpty(txtTemplateFolder.Text) || !Directory.Exists(txtTemplateFolder.Text))
            {
                ShowStatusMessage("模版目录无效", "请选择有效的模版文件夹", "提示", MessageBoxIcon.Warning);
                return false;
            }
            if (string.IsNullOrEmpty(txtMaterialFolder.Text) || !Directory.Exists(txtMaterialFolder.Text))
            {
                ShowStatusMessage("素材目录无效", "请选择有效的素材文件夹", "提示", MessageBoxIcon.Warning);
                return false;
            }
            if (string.IsNullOrEmpty(txtSavePath.Text) || !Directory.Exists(txtSavePath.Text))
            {
                ShowStatusMessage("保存目录无效", "请选择有效的保存路径", "提示", MessageBoxIcon.Warning);
                return false;
            }

            return true;
        }

    // 远程请求处理，构建任务列表
        private BuildJobsResult BuildJobsFromRemoteRequest(RemoteMergeRequest request)
        {
            if (!ValidateSelectedFolders())
            {
                return null;
            }

            string format = cmbFormat.SelectedItem?.ToString() ?? "TIF";
            
            if (string.Equals(format, "PSD", StringComparison.OrdinalIgnoreCase))
            {
                ShowStatusMessage("PSD 导出不支持", "当前版本暂不支持真实 PSD 导出，请改用 TIF、PNG 或 JPEG。", "提示", MessageBoxIcon.Warning);
                return null;
            }

            string templateError;
            string templateFile = ResolveRemoteTemplateFile(request, out templateError);
            if (templateFile == null)
            {
                ShowStatusMessage("远程模版匹配失败,未找到素材", templateError, "错误", MessageBoxIcon.Error);
                return null;
            }

            string materialError;
            Console.WriteLine($"{string.Join(", ", request.MaterialNames ?? new List<string>())}, 材料名称");
            List<string> materialFiles = ResolveMaterialsByNames(txtMaterialFolder.Text, request.MaterialNames, out materialError);
            if (materialFiles == null)
            {
                ShowStatusMessage("远程素材匹配失败,未找到素材", materialError, "错误", MessageBoxIcon.Error);
                return null;
            }

            bool batchMode = materialFiles.Count > 1;
            chkBatchMode.Checked = batchMode;
            return BuildJobsCore(templateFile, materialFiles, batchMode, format);
        }

        private string ResolveRemoteTemplateFile(RemoteMergeRequest request, out string errorMessage)
        {
            errorMessage = null;
            List<string> templateFiles = GetImageFiles(txtTemplateFolder.Text);
            if (templateFiles.Count == 0)
            {
                errorMessage = "模版文件夹未找到图片文件";
                return null;
            }

            if (!string.IsNullOrWhiteSpace(request?.TemplateName))
            {
                return ResolveSingleFileByBaseName(templateFiles, request.TemplateName, "模版", out errorMessage);
            }

            if (templateFiles.Count != 1)
            {
                errorMessage = "远程模式要求模版文件夹中只有一张图片，或在消息中提供 templateName";
                return null;
            }

            return templateFiles[0];
        }

        private List<string> ResolveMaterialsByNames(string folderPath, IEnumerable<string> materialNames, out string errorMessage)
        {
            errorMessage = null;
            List<string> materialFiles = GetImageFiles(folderPath);
            if (materialFiles.Count == 0)
            {
                errorMessage = "素材文件夹未找到图片文件";
                return null;
            }

            var resolvedFiles = new List<string>();
            foreach (string materialName in materialNames ?? Enumerable.Empty<string>())
            {
                string currentError;
                string matchedFile = ResolveSingleFileByBaseName(materialFiles, materialName, "素材", out currentError);
                if (matchedFile == null)
                {
                    errorMessage = currentError;
                    return null;
                }

                resolvedFiles.Add(matchedFile);
            }

            return resolvedFiles;
        }

        private string ResolveSingleFileByBaseName(IEnumerable<string> files, string targetName, string fileRole, out string errorMessage)
        {
            errorMessage = null;
            string normalizedName = (targetName ?? string.Empty).Trim();
            List<string> matches = (files ?? Enumerable.Empty<string>())
                .Where(path => string.Equals(GetBaseName(path), normalizedName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0)
            {
                errorMessage = $"未找到同名{fileRole}: {normalizedName}";
                return null;
            }

            if (matches.Count > 1)
            {
                errorMessage = $"同名{fileRole}存在多个不同扩展文件，无法自动选择: {normalizedName}";
                return null;
            }

            return matches[0];
        }

        private BuildJobsResult BuildJobsCore(string templateFile, List<string> materialFiles, bool isBatchMode, string format, string rotation = null, string mirror = null)
        {
            string separator = string.IsNullOrWhiteSpace(txtSeparator.Text) ? "-" : txtSeparator.Text;
            TemplateCompositeMode compositeMode = TemplateCompositeMode.FullBleed;
            string compositeModeName = "满版模式";
            string exclusionMaskPath = null;
            string ext = GetOutputExtension(format);

            var jobs = new List<MergeJobItem>();
            int index = 1;
            foreach (string materialFile in materialFiles)
            {
                string baseName = GetBaseName(templateFile) + separator + GetBaseName(materialFile);
                string outputFile = NextOutputFile(txtSavePath.Text, baseName, ext);
                jobs.Add(new MergeJobItem
                {
                    Index = index++,
                    TemplatePath = templateFile,
                    MaterialPath = materialFile,
                    OutputPath = outputFile,
                    Status = MergeJobStatus.Pending,
                    Message = "等待处理"
                });
            }

            return new BuildJobsResult
            {
                IsBatchMode = isBatchMode,
                TemplateFile = templateFile,
                Jobs = jobs,
                Format = format,
                CompositeMode = compositeMode,
                CompositeModeName = compositeModeName,
                ExclusionMaskPath = exclusionMaskPath,
                Rotation = rotation,
                Mirror = mirror
            };
        }

        private void SetupDarkTheme()
        {
            this.Text = "套图";
            this.Size = new Size(DialogWidth, SingleDialogHeight);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(25, 35, 55);
            this.ForeColor = Color.FromArgb(220, 225, 235);
        }

        private void SetupControls()
        {
            int startX = 20;
            int startY = 20;
            int rowHeight = 40;
            int labelWidth = 80;
            int textBoxWidth = 520;
            int btnWidth = 60;

            var lblTemplate = CreateLabel("模版:", startX, startY, labelWidth);
            txtTemplateFolder = CreateTextBox(startX + labelWidth + 5, startY, textBoxWidth);
            btnBrowseTemplate = CreateButton("浏览", startX + labelWidth + textBoxWidth + 10, startY, btnWidth);
            btnBrowseTemplate.Click += (s, e) => BrowseFolder(txtTemplateFolder);

            startY += rowHeight;
            var lblMaterial = CreateLabel("素材:", startX, startY, labelWidth);
            txtMaterialFolder = CreateTextBox(startX + labelWidth + 5, startY, textBoxWidth);
            btnBrowseMaterial = CreateButton("浏览", startX + labelWidth + textBoxWidth + 10, startY, btnWidth);
            btnBrowseMaterial.Click += (s, e) => BrowseFolder(txtMaterialFolder);

            startY += rowHeight;
            var lblSave = CreateLabel("保存:", startX, startY, labelWidth);
            txtSavePath = CreateTextBox(startX + labelWidth + 5, startY, textBoxWidth);
            btnBrowseSave = CreateButton("浏览", startX + labelWidth + textBoxWidth + 10, startY, btnWidth);
            btnBrowseSave.Click += (s, e) => BrowseFolder(txtSavePath);

            startY += rowHeight;
            var lblSeparator = CreateLabel("分隔符:", startX, startY, labelWidth);
            txtSeparator = CreateTextBox(startX + labelWidth, startY, 50);
            txtSeparator.Text = "-";

            var lblFormat = CreateLabel("格式:", startX + 145, startY, 40);
            cmbFormat = CreateComboBox(startX + 190, startY, 80);
            cmbFormat.Items.AddRange(new object[] { "TIF", "PSD", "JPEG", "PNG" });
            cmbFormat.SelectedIndex = 0;

            var lblMode = CreateLabel("模式:", startX + 590, startY, 40);
            cmbCompositeMode = CreateComboBox(startX + 635, startY, 80);
            cmbCompositeMode.Items.AddRange(new object[] { "套图标准模式", "满版模式" });
            cmbCompositeMode.SelectedIndex = 1;

            var lblRotation = CreateLabel("旋转:", startX + 460, startY, 40);
            cmbRotation = CreateComboBox(startX + 505, startY, 80);
            cmbRotation.Items.AddRange(new object[] { "0°", "90°", "180°", "270°" });
            cmbRotation.SelectedIndex = 0;

            var lblMirror = CreateLabel("镜像:", startX + 290, startY, 40);
            cmbMirror = CreateComboBox(startX + 335, startY, 110);
            cmbMirror.Items.AddRange(new object[] { "无", "水平", "垂直", "水平+垂直" });
            cmbMirror.SelectedIndex = 0;

            startY += rowHeight;
            channelSectionLabel = new Label
            {
                Text = "通道设置",
                Location = new Point(startX, startY),
                Size = new Size(120, 24),
                Font = new Font("微软雅黑", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(220, 225, 235),
                BackColor = Color.Transparent
            };

            addChannels = new Button
            {
                Text = "+ 添加通道",
                Location = new Point(startX + 560, startY - 3),
                Size = new Size(110, 30),
                Font = new Font("微软雅黑", 9F),
                BackColor = Color.FromArgb(45, 100, 160),
                ForeColor = Color.FromArgb(220, 225, 235),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            addChannels.FlatAppearance.BorderColor = Color.FromArgb(70, 140, 200);
            addChannels.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 120, 180);
            addChannels.Click += addChannels_Click;

            channelListPanel = new Panel
            {
                Location = new Point(startX, startY + 30),
                Size = new Size(DialogWidth - 56, 0),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                AutoScroll = false
            };

            startY += 30;

            // chkWhiteInk = new CheckBox
            // {
            //     Text = "白墨通道",
            //     Location = new Point(startX + labelWidth + 5, startY + 8),
            //     Size = new Size(80, 22),
            //     ForeColor = Color.FromArgb(200, 205, 215),
            //     BackColor = Color.Transparent,
            //     FlatStyle = FlatStyle.Flat,
            //     Checked = true
            // };

            // chkVarnish = new CheckBox
            // {
            //     Text = "光油通道",
            //     Location = new Point(startX + labelWidth + 100, startY + 8),
            //     Size = new Size(80, 22),
            //     ForeColor = Color.FromArgb(200, 205, 215),
            //     BackColor = Color.Transparent,
            //     FlatStyle = FlatStyle.Flat,
            //     Checked = true
            // };

            chkBatchMode = new CheckBox
            {
                Text = "批量套图",
                Location = new Point(startX + labelWidth + 200, startY + 8),
                Size = new Size(90, 22),
                ForeColor = Color.FromArgb(200, 205, 215),
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Checked = false
            };
            chkBatchMode.CheckedChanged += (s, e) => ToggleBatchModeLayout();

            lblStatus = new Label
            {
                Text = "就绪",
                Location = new Point(startX + 380, startY + 8),
                Size = new Size(300, 22),
                Font = new Font("微软雅黑", 9F),
                ForeColor = Color.FromArgb(150, 155, 165),
                BackColor = Color.Transparent
            };

            startY += rowHeight + 10;
            btnMerge = new Button
            {
                Text = "开始套图",
                Location = new Point(startX, startY),
                Size = new Size(120, 36),
                Font = new Font("微软雅黑", 10F),
                BackColor = Color.FromArgb(45, 100, 160),
                ForeColor = Color.FromArgb(220, 225, 235),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnMerge.FlatAppearance.BorderColor = Color.FromArgb(70, 140, 200);
            btnMerge.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 120, 180);
            btnMerge.Click += BtnMerge_Click;

            btnClose = new Button
            {
                Text = "关闭",
                Location = new Point(startX + 140, startY),
                Size = new Size(80, 36),
                Font = new Font("微软雅黑", 10F),
                BackColor = Color.FromArgb(40, 55, 80),
                ForeColor = Color.FromArgb(220, 225, 235),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnClose.Click += BtnClose_Click;

            pnlBatch = new Panel
            {
                Location = new Point(startX, startY + 55),
                Size = new Size(DialogWidth - 56, 400),
                BackColor = Color.FromArgb(30, 40, 60),
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };

            prgBatch = new ProgressBar
            {
                Location = new Point(12, 12),
                Size = new Size(pnlBatch.Width - 24, 18),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Minimum = 0,
                Maximum = 1,
                Value = 0
            };

            lblBatchSummary = new Label
            {
                Text = "等待批量任务",
                Location = new Point(12, 38),
                Size = new Size(pnlBatch.Width - 24, 22),
                Font = new Font("微软雅黑", 9F),
                ForeColor = Color.FromArgb(200, 205, 215),
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            lvResults = new ListView
            {
                Location = new Point(12, 68),
                Size = new Size(pnlBatch.Width - 24, 290),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false,
                HideSelection = false,
                BackColor = Color.FromArgb(40, 50, 70),
                ForeColor = Color.FromArgb(220, 225, 235),
                BorderStyle = BorderStyle.FixedSingle,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            lvResults.Columns.Add("序号", 50);
            lvResults.Columns.Add("素材", 180);
            lvResults.Columns.Add("状态", 90);
            lvResults.Columns.Add("输出文件", 220);
            lvResults.Columns.Add("消息", 140);

            btnPauseResume = new Button
            {
                Text = "暂停",
                Location = new Point(pnlBatch.Width - 190, pnlBatch.Height - 42),
                Size = new Size(80, 28),
                Font = new Font("微软雅黑", 9F),
                BackColor = Color.FromArgb(45, 60, 85),
                ForeColor = Color.FromArgb(220, 225, 235),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
                Enabled = false
            };
            btnPauseResume.Click += BtnPauseResume_Click;

            btnCancel = new Button
            {
                Text = "取消",
                Location = new Point(pnlBatch.Width - 100, pnlBatch.Height - 42),
                Size = new Size(80, 28),
                Font = new Font("微软雅黑", 9F),
                BackColor = Color.FromArgb(115, 55, 55),
                ForeColor = Color.FromArgb(220, 225, 235),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
                Enabled = false
            };
            btnCancel.Click += BtnCancel_Click;

            pnlBatch.Controls.Add(prgBatch);
            pnlBatch.Controls.Add(lblBatchSummary);
            pnlBatch.Controls.Add(lvResults);
            pnlBatch.Controls.Add(btnPauseResume);
            pnlBatch.Controls.Add(btnCancel);

            this.Controls.AddRange(new Control[] {
                lblTemplate, txtTemplateFolder, btnBrowseTemplate,
                lblMaterial, txtMaterialFolder, btnBrowseMaterial,
                lblSave, txtSavePath, btnBrowseSave,
                lblSeparator, txtSeparator,
                lblFormat, cmbFormat,
                lblRotation, cmbRotation,
                lblMirror, cmbMirror,
                channelSectionLabel, addChannels, channelListPanel,
                chkBatchMode, lblStatus,
                btnMerge, btnClose,
                pnlBatch
            });
            // chkWhiteInk, chkVarnish,

            RearrangeChannels();
            ToggleBatchModeLayout();
        }

        // 动态添加通道的点击事件
        private void addChannels_Click(object sender, EventArgs e)
        {
            AddNewChannel();
        }

        // 动态添加新通道
        private void AddNewChannel()
        {
            int channelNumber = nextChannelNumber++;
            var panel = new Panel
            {
                Size = new Size(GetChannelCardWidth(), ChannelCardHeight),
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                Tag = channelNumber
            };

            var lblChannel = new Label
            {
                Name = "lblChannel",
                Text = $"通道 {channelNumber}",
                Location = new Point(10, 10),
                Size = new Size(48, 22),
                Font = new Font("微软雅黑", 9F),
                ForeColor = Color.FromArgb(220, 225, 235),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var txtChannelName = new TextBox
            {
                Name = "txtChannelName",
                Text = $"通道{channelNumber}",
                Location = new Point(62, 8),
                Size = new Size(92, 26),
                Font = new Font("微软雅黑", 9F),
                BackColor = Color.FromArgb(50, 65, 90),
                ForeColor = Color.FromArgb(220, 225, 235),
                BorderStyle = BorderStyle.FixedSingle,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };

            var btnApply = new Button
            {
                Name = "btnApply",
                Text = "保存",
                Size = new Size(44, 26),
                Location = new Point(162, 8),
                Font = new Font("微软雅黑", 8.5F),
                BackColor = Color.FromArgb(45, 150, 80),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnApply.Click += (s, e) => ApplyChannelName(txtChannelName, btnApply);

            var btnDelete = new Button
            {
                Name = "btnDelete",
                Text = "删除",
                Size = new Size(44, 26),
                Location = new Point(212, 8),
                Font = new Font("微软雅黑", 8.5F),
                BackColor = Color.FromArgb(180, 60, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnDelete.Click += (s, e) => DeleteChannel(panel);

            panel.Controls.Add(lblChannel);
            panel.Controls.Add(txtChannelName);
            panel.Controls.Add(btnApply);
            panel.Controls.Add(btnDelete);
            channelListPanel.Controls.Add(panel);

            channelControls.Add(new ChannelControl
            {
                Panel = panel,
                TextBox = txtChannelName,
                ChannelNumber = channelNumber
            });

            LayoutChannelPanel(panel);
            RearrangeChannels();
            txtChannelName.Focus();
            txtChannelName.SelectAll();
        }

        private void RefreshChannelNames()
        {
            channelNames.Clear();
            channelNames.AddRange(channelControls
                .Select(control => control.TextBox.Text.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name)));
            Console.WriteLine($"当前通道名称列表: {string.Join(", ", channelNames)}");
        }

        // 应用/确认通道名称
        private void ApplyChannelName(TextBox txtBox, Button btnApply)
        {
            string newName = txtBox.Text.Trim();
            if (string.IsNullOrEmpty(newName))
            {
                MessageBox.Show("通道名称不能为空！", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtBox.Focus();
                return;
            }

            foreach (var control in channelControls)
            {
                if (control.TextBox != txtBox &&
                    control.TextBox.Text.Trim().Equals(newName, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show($"通道名称 '{newName}' 已存在，请使用其他名称！",
                        "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    txtBox.Focus();
                    txtBox.SelectAll();
                    return;
                }
            }

            txtBox.Text = newName;
            txtBox.BackColor = Color.FromArgb(35, 70, 50);
            btnApply.BackColor = Color.FromArgb(35, 120, 60);
            btnApply.Text = "已保存";
            RefreshChannelNames();
            lblStatus.Text = $"通道名称已更新: {newName}";
        }

        // 删除通道
        private void DeleteChannel(Panel panel)
        {
            ChannelControl control = channelControls.FirstOrDefault(c => c.Panel == panel);
            string channelName = control?.TextBox.Text.Trim();
            var result = MessageBox.Show($"确定要删除通道 {channelName} 吗？",
                "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                channelListPanel.Controls.Remove(panel);
                panel.Dispose();

                if (control != null)
                {
                    channelControls.Remove(control);
                    // 重置通道计数
                    if(channelControls.Count == 0)
                    {
                        nextChannelNumber = 1;
                    }   
                    
                }

                RefreshChannelNames();
                RearrangeChannels();
                lblStatus.Text = string.IsNullOrWhiteSpace(channelName) ? "已删除通道" : $"已删除通道: {channelName}";
            }
        }

        // 重新排列通道位置
        private void RearrangeChannels()
        {
            int cardWidth = GetChannelCardWidth();
            int columnGap = ChannelCardGap;
            int leftColumn = ChannelCardGap;
            int rightColumn = leftColumn + cardWidth + columnGap;
            List<ChannelControl> orderedControls = channelControls.OrderBy(c => c.ChannelNumber).ToList();

            for (int i = 0; i < orderedControls.Count; i++)
            {
                ChannelControl control = orderedControls[i];
                int columnIndex = i % 2;
                int rowIndex = i / 2;
                int left = columnIndex == 0 ? leftColumn : rightColumn;
                int top = ChannelCardGap + rowIndex * (ChannelCardHeight + ChannelCardGap);

                control.Panel.Location = new Point(left, top);
                control.Panel.Size = new Size(cardWidth, ChannelCardHeight);
                LayoutChannelPanel(control.Panel);
            }

            int rowCount = (orderedControls.Count + 1) / 2;
            int contentHeight = rowCount == 0
                ? 0
                : ChannelCardGap + rowCount * ChannelCardHeight + Math.Max(0, rowCount - 1) * ChannelCardGap;

            channelListPanel.Height = contentHeight;
            channelListPanel.AutoScroll = false;
            channelListPanel.AutoScrollMinSize = Size.Empty;

            UpdateDialogLayout();
        }

        private void UpdateDialogLayout()
        {
            if (channelSectionLabel == null || channelListPanel == null || chkBatchMode == null || lblStatus == null || btnMerge == null || btnClose == null || pnlBatch == null)
            {
                return;
            }

            int optionsTop = channelListPanel.Bottom + 8;
            chkBatchMode.Location = new Point(305, optionsTop + 8);
            lblStatus.Location = new Point(400, optionsTop + 8);

            int buttonTop = optionsTop + 50;
            btnMerge.Location = new Point(20, buttonTop);
            btnClose.Location = new Point(160, buttonTop);
            pnlBatch.Location = new Point(20, buttonTop + 55);

            int baseHeight = (chkBatchMode.Checked ? BatchDialogHeight : SingleDialogHeight) - 120;
            int targetHeight = Math.Max(baseHeight, buttonTop + (chkBatchMode.Checked ? BatchDialogBottomPadding + pnlBatch.Height : SingleDialogBottomPadding));
            this.Size = new Size(DialogWidth, targetHeight);
        }

        private int GetChannelCardWidth()
        {
            int availableWidth = channelListPanel.ClientSize.Width - ChannelCardGap * 3;
            return Math.Max(220, availableWidth / 2);
        }

        private void LayoutChannelPanel(Panel panel)
        {
            if (panel == null)
            {
                return;
            }

            var lblChannel = panel.Controls["lblChannel"] as Label;
            var txtChannelName = panel.Controls["txtChannelName"] as TextBox;
            var btnApply = panel.Controls["btnApply"] as Button;
            var btnDelete = panel.Controls["btnDelete"] as Button;

            if (lblChannel == null || txtChannelName == null || btnApply == null || btnDelete == null)
            {
                return;
            }

            int deleteLeft = panel.Width - btnDelete.Width - 10;
            int applyLeft = deleteLeft - btnApply.Width - 6;
            int textLeft = txtChannelName.Left;
            int textWidth = Math.Max(80, applyLeft - textLeft - 8);

            lblChannel.Location = new Point(10, 10);
            txtChannelName.Location = new Point(textLeft, 8);
            txtChannelName.Size = new Size(textWidth, 26);
            btnApply.Location = new Point(applyLeft, 8);
            btnDelete.Location = new Point(deleteLeft, 8);
        }


        private void MergeDialog_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (isRunning)
            {
                e.Cancel = true;
                if (!isRemoteMode)
                {
                    MessageBox.Show("当前任务正在执行，请先取消任务后再关闭窗口。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    lblStatus.Text = "当前远程任务正在执行，请先等待完成或取消";
                }
                return;
            }

            if (canReturnResults && this.DialogResult != DialogResult.OK && e.CloseReason == CloseReason.UserClosing)
            {
                this.DialogResult = DialogResult.OK;
            }
        }

        private Label CreateLabel(string text, int x, int y, int width)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 22),
                Font = new Font("微软雅黑", 9F),
                ForeColor = Color.FromArgb(200, 205, 215),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleRight
            };
        }

        private TextBox CreateTextBox(int x, int y, int width)
        {
            return new TextBox
            {
                Location = new Point(x, y),
                Size = new Size(width, 24),
                Font = new Font("微软雅黑", 9F),
                BackColor = Color.FromArgb(40, 50, 70),
                ForeColor = Color.FromArgb(220, 225, 235),
                BorderStyle = BorderStyle.FixedSingle
            };
        }

        private ComboBox CreateComboBox(int x, int y, int width)
        {
            return new ComboBox
            {
                Location = new Point(x, y),
                Size = new Size(width, 24),
                Font = new Font("微软雅黑", 9F),
                BackColor = Color.FromArgb(40, 50, 70),
                ForeColor = Color.FromArgb(220, 225, 235),
                FlatStyle = FlatStyle.Flat,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
        }

        private Button CreateButton(string text, int x, int y, int width)
        {
            return new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 24),
                Font = new Font("微软雅黑", 9F),
                BackColor = Color.FromArgb(45, 60, 85),
                ForeColor = Color.FromArgb(220, 225, 235),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
        }

        private void ToggleBatchModeLayout()
        {
            pnlBatch.Visible = chkBatchMode.Checked;
            this.Size = new Size(DialogWidth, chkBatchMode.Checked ? BatchDialogHeight : SingleDialogHeight);
            btnMerge.Text = isRunning ? "处理中..." : (chkBatchMode.Checked ? "开始批量套图" : "开始套图");

            if (chkBatchMode.Checked)
            {
                RearrangeChannels();
            }

            if (!chkBatchMode.Checked && !isRunning)
            {
                lvResults.Items.Clear();
                prgBatch.Maximum = 1;
                prgBatch.Value = 0;
                lblBatchSummary.Text = "等待批量任务";
            }
        }

        private void BrowseFolder(TextBox textBox)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择文件夹";
                if (!string.IsNullOrEmpty(textBox.Text) && Directory.Exists(textBox.Text))
                    dialog.SelectedPath = textBox.Text;

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    textBox.Text = dialog.SelectedPath;
                    SavePath(textBox);
                }
            }
        }

        private void SavePath(TextBox textBox)
        {
            try
            {
                var settings = Properties.Settings.Default;
                if (textBox == txtTemplateFolder)
                    settings.TemplateFolder = textBox.Text;
                else if (textBox == txtMaterialFolder)
                    settings.MaterialFolder = textBox.Text;
                else if (textBox == txtSavePath)
                    settings.SavePath = textBox.Text;
                settings.Save();
            }
            catch { }
        }

        private void LoadSavedPaths()
        {
            try
            {
                var settings = Properties.Settings.Default;
                if (!string.IsNullOrEmpty(settings.TemplateFolder) && Directory.Exists(settings.TemplateFolder))
                    txtTemplateFolder.Text = settings.TemplateFolder;
                if (!string.IsNullOrEmpty(settings.MaterialFolder) && Directory.Exists(settings.MaterialFolder))
                    txtMaterialFolder.Text = settings.MaterialFolder;
                if (!string.IsNullOrEmpty(settings.SavePath) && Directory.Exists(settings.SavePath))
                    txtSavePath.Text = settings.SavePath;
            }
            catch { }
        }

        private List<string> GetImageFiles(string folderPath)
        {
            if (!Directory.Exists(folderPath))
                return new List<string>();

            return Directory.GetFiles(folderPath)
                .Where(f => imageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f)
                .ToList();
        }

        private string FindFirstImage(string folderPath)
        {
            return GetImageFiles(folderPath).FirstOrDefault();
        }

        private string GetBaseName(string filePath)
        {
            string name = Path.GetFileNameWithoutExtension(filePath);
            int dot = name.LastIndexOf('.');
            return dot > 0 ? name.Substring(0, dot) : name;
        }

        private string NextOutputFile(string saveFolder, string baseName, string ext)
        {
            string file = Path.Combine(saveFolder, baseName + ext);
            int i = 1;
            while (File.Exists(file))
            {
                file = Path.Combine(saveFolder, $"{baseName}_{i}{ext}");
                i++;
            }
            return file;
        }

        private string GetOutputExtension(string format)
        {
            switch ((format ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "JPEG":
                case "JPG":
                    return ".jpg";
                case "PNG":
                    return ".png";
                case "PSD":
                    return ".psd";
                default:
                    return ".tif";
            }
        }
        // 本地操作
        private BuildJobsResult BuildJobs()
        {
            if (!ValidateSelectedFolders())
            {
                return null;
            }

            string format = cmbFormat.SelectedItem?.ToString() ?? "TIF";
            string rotation = cmbRotation.SelectedItem?.ToString() ?? "0°";
            string mirror = cmbMirror.SelectedItem?.ToString() ?? "无";
            if (string.Equals(format, "PSD", StringComparison.OrdinalIgnoreCase))
            {
                ShowStatusMessage("PSD 导出不支持", "当前版本暂不支持真实 PSD 导出，请改用 TIF、PNG 或 JPEG。", "提示", MessageBoxIcon.Warning);
                return null;
            }

            bool isBatchMode = chkBatchMode.Checked;
            List<string> templateFiles = GetImageFiles(txtTemplateFolder.Text);
            List<string> materialFiles = GetImageFiles(txtMaterialFolder.Text);

            if (templateFiles.Count == 0)
            {
                ShowStatusMessage("未找到模版图片", "模版文件夹未找到图片文件", "错误", MessageBoxIcon.Error);
                return null;
            }
            if (materialFiles.Count == 0)
            {
                ShowStatusMessage("未找到素材图片", "素材文件夹未找到图片文件", "错误", MessageBoxIcon.Error);
                return null;
            }
            if (isBatchMode && templateFiles.Count != 1)
            {
                ShowStatusMessage("批量模式模版数量无效", "批量套图当前只支持单模版目录，请保证模版文件夹中只有一张图片。", "提示", MessageBoxIcon.Warning);
                return null;
            }

            string templateFile = templateFiles[0];
            if (!isBatchMode)
            {
                materialFiles = new List<string> { materialFiles[0] };
            }

            return BuildJobsCore(templateFile, materialFiles, isBatchMode, format,rotation,mirror);
        }

        //重置对话框初始值
        private void ResetDialogResultState()
        {
            ResultPath = null;
            ResultPaths = Array.Empty<string>();
            canReturnResults = false;
            this.DialogResult = DialogResult.None;
        }

        private void InitializeBatchDisplay(List<MergeJobItem> jobs)
        {
            lvResults.BeginUpdate();
            try
            {
                lvResults.Items.Clear();
                foreach (MergeJobItem job in jobs)
                {
                    var item = new ListViewItem(job.Index.ToString());
                    item.SubItems.Add(Path.GetFileName(job.MaterialPath));
                    item.SubItems.Add(GetStatusText(job.Status));
                    item.SubItems.Add(Path.GetFileName(job.OutputPath));
                    item.SubItems.Add(job.Message ?? string.Empty);
                    item.Tag = job;
                    job.ListItem = item;
                    lvResults.Items.Add(item);
                }
            }
            finally
            {
                lvResults.EndUpdate();
            }

            prgBatch.Maximum = Math.Max(1, jobs.Count);
            prgBatch.Value = 0;
            lblBatchSummary.Text = jobs.Count == 0 ? "没有待处理任务" : $"共 {jobs.Count} 个任务，等待开始";
        }

        private void UpdateJobStatus(MergeJobItem job, MergeJobStatus status, string message)
        {
            job.Status = status;
            job.Message = message;

            if (job.ListItem != null)
            {
                job.ListItem.SubItems[2].Text = GetStatusText(status);
                job.ListItem.SubItems[4].Text = message ?? string.Empty;
            }
        }

        private string GetStatusText(MergeJobStatus status)
        {
            switch (status)
            {
                case MergeJobStatus.Validating:
                    return "预检中";
                case MergeJobStatus.Running:
                    return "处理中";
                case MergeJobStatus.Completed:
                    return "成功";
                case MergeJobStatus.Failed:
                    return "失败";
                case MergeJobStatus.Canceled:
                    return "已取消";
                case MergeJobStatus.Skipped:
                    return "已跳过";
                default:
                    return "等待中";
            }
        }

        private void UpdateBatchSummary()
        {
            if (batchState == null)
                return;

            int total = batchState.Jobs.Count;
            int completed = batchState.Jobs.Count(j => j.Status == MergeJobStatus.Completed);
            int failed = batchState.Jobs.Count(j => j.Status == MergeJobStatus.Failed);
            int skipped = batchState.Jobs.Count(j => j.Status == MergeJobStatus.Skipped);
            int canceled = batchState.Jobs.Count(j => j.Status == MergeJobStatus.Canceled);
            int finished = batchState.Jobs.Count(j =>
                j.Status == MergeJobStatus.Completed ||
                j.Status == MergeJobStatus.Failed ||
                j.Status == MergeJobStatus.Skipped ||
                j.Status == MergeJobStatus.Canceled);

            prgBatch.Value = Math.Min(prgBatch.Maximum, Math.Max(0, finished));

            MergeJobItem runningJob = batchState.Jobs.FirstOrDefault(j => j.Status == MergeJobStatus.Running || j.Status == MergeJobStatus.Validating);
            string runningText = runningJob != null ? $"，当前: {Path.GetFileName(runningJob.MaterialPath)}" : string.Empty;
            lblBatchSummary.Text = $"已完成 {completed}/{total}，失败 {failed}，跳过 {skipped}，取消 {canceled}{runningText}";
        }

        private void SetBusyState(bool busy)
        {
            isRunning = busy;

            txtTemplateFolder.Enabled = !busy;
            txtMaterialFolder.Enabled = !busy;
            txtSavePath.Enabled = !busy;
            txtSeparator.Enabled = !busy;
            cmbFormat.Enabled = !busy;
            // chkWhiteInk.Enabled = !busy;
            // chkVarnish.Enabled = !busy;
            chkBatchMode.Enabled = !busy;
            btnBrowseTemplate.Enabled = !busy;
            btnBrowseMaterial.Enabled = !busy;
            btnBrowseSave.Enabled = !busy;
            btnClose.Enabled = !busy;
            btnMerge.Enabled = !busy;
            btnPauseResume.Enabled = busy && chkBatchMode.Checked;
            btnCancel.Enabled = busy && chkBatchMode.Checked;
            btnPauseResume.Text = "暂停";

            if (busy)
            {
                btnMerge.Text = "处理中...";
            }
            else
            {
                btnMerge.Text = chkBatchMode.Checked ? "开始批量套图" : "开始套图";
            }
        }

        private ValidationResult ValidateImage(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "文件不存在"
                };
            }

            try
            {
                using (var preview = AsposePSDHelper.GeneratePreview(imagePath))
                {
                    if (preview == null)
                    {
                        return new ValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = "无法读取或格式不兼容"
                        };
                    }
                }

                return new ValidationResult
                {
                    IsValid = true,
                    ErrorMessage = null
                };
            }
            catch (Exception ex)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private void RunControlCheckpoint()
        {
            if (batchState == null)
                return;

            batchState.PauseGate.Wait(batchState.CancellationSource.Token);
            batchState.CancellationSource.Token.ThrowIfCancellationRequested();
        }

        private void InvokeOnUi(Action action)
        {
            if (IsDisposed || Disposing || action == null)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(action);
            }
            else
            {
                action();
            }
        }

        private void ReportProgress(MergeJobItem job, string message)
        {
            InvokeOnUi(() =>
            {
                lblStatus.Text = message;
                if (chkBatchMode.Checked && job != null)
                {
                    UpdateJobStatus(job, MergeJobStatus.Running, message);
                    UpdateBatchSummary();
                }
            });
        }

        private async Task<List<MergeJobItem>> PrevalidateJobsAsync(BuildJobsResult buildResult)
        {
            var validJobs = new List<MergeJobItem>();
            var invalidMessages = new List<string>();

            lblStatus.Text = "正在预检模版...";
            var templateValidation = await Task.Run(() => ValidateImage(buildResult.TemplateFile));
            if (!templateValidation.IsValid)
            {
                ShowStatusMessage(
                    "模版预检失败",
                    $"模版预检失败: {templateValidation.ErrorMessage}",
                    "错误",
                    MessageBoxIcon.Error);
                return null;
            }

            foreach (MergeJobItem job in buildResult.Jobs)
            {
                if (buildResult.IsBatchMode)
                {
                    UpdateJobStatus(job, MergeJobStatus.Validating, "预检中...");
                    UpdateBatchSummary();
                }

                lblStatus.Text = $"正在预检素材: {Path.GetFileName(job.MaterialPath)}";
                ValidationResult validation = await Task.Run(() => ValidateImage(job.MaterialPath));
                if (validation.IsValid)
                {
                    validJobs.Add(job);
                    if (buildResult.IsBatchMode)
                    {
                        UpdateJobStatus(job, MergeJobStatus.Pending, "等待处理");
                        UpdateBatchSummary();
                    }
                }
                else
                {
                    string message = validation.ErrorMessage ?? "无法读取或格式不兼容";
                    invalidMessages.Add($"{Path.GetFileName(job.MaterialPath)}: {message}");
                    if (buildResult.IsBatchMode)
                    {
                        UpdateJobStatus(job, MergeJobStatus.Skipped, message);
                        UpdateBatchSummary();
                    }
                }
            }

            if (invalidMessages.Count > 0)
            {
                ShowStatusMessage(
                    $"预检完成，跳过 {invalidMessages.Count} 个素材",
                    "以下素材预检失败，将自动跳过：\n" + string.Join("\n", invalidMessages),
                    "预检提示",
                    MessageBoxIcon.Warning);
            }

            return validJobs;
        }

        private void MarkPendingJobsAsCanceled(List<MergeJobItem> jobs)
        {
            foreach (MergeJobItem job in jobs)
            {
                if (job.Status == MergeJobStatus.Pending || job.Status == MergeJobStatus.Validating || job.Status == MergeJobStatus.Running)
                {
                    UpdateJobStatus(job, MergeJobStatus.Canceled, "已取消");
                }
            }

            if (chkBatchMode.Checked)
            {
                UpdateBatchSummary();
            }
        }

        private async Task ExecuteJobsAsync(BuildJobsResult buildResult)
        {
            batchState = new BatchRunState(buildResult.Jobs);
            SetBusyState(true);

            try
            {
                if (buildResult.IsBatchMode)
                {
                    InitializeBatchDisplay(buildResult.Jobs);
                }

                List<MergeJobItem> validJobs = await PrevalidateJobsAsync(buildResult);
                if (validJobs == null)
                {
                    lblStatus.Text = "预检失败";
                    return;
                }

                if (validJobs.Count == 0)
                {
                    lblStatus.Text = "没有可处理的素材";
                    if (buildResult.IsBatchMode)
                    {
                        UpdateBatchSummary();
                    }
                    return;
                }

                foreach (MergeJobItem job in validJobs)
                {
                    try
                    {
                        RunControlCheckpoint();

                        Console.WriteLine($"模版路径名称：{job.TemplatePath}");
                        Console.WriteLine($"素材路径名称：{job.MaterialPath}");
                        Console.WriteLine($"套图模式：{buildResult.CompositeModeName}");

                        UpdateJobStatus(job, MergeJobStatus.Running, "准备处理...");
                        if (buildResult.IsBatchMode)
                        {
                            UpdateBatchSummary();
                        }

                        await Task.Run(() =>
                        {
                            AsposePSDHelper.ProcessTifMode(
                                job.TemplatePath,
                                job.MaterialPath,
                                job.OutputPath,
                                buildResult.Format,
                                msg => ReportProgress(job, msg),
                                channelNames,
                                buildResult.Rotation,
                                buildResult.Mirror,
                                0,
                                0,
                                buildResult.CompositeMode,
                                buildResult.ExclusionMaskPath,
                                RunControlCheckpoint);
                        }, batchState.CancellationSource.Token);

                        batchState.SuccessOutputPaths.Add(job.OutputPath);
                        UpdateJobStatus(job, MergeJobStatus.Completed, "处理完成");
                        lblStatus.Text = $"已完成: {Path.GetFileName(job.MaterialPath)}";
                        Console.WriteLine($"输出路径名称：{job.OutputPath}");
                    }
                    catch (OperationCanceledException)
                    {
                        UpdateJobStatus(job, MergeJobStatus.Canceled, "已取消");
                        MarkPendingJobsAsCanceled(validJobs);
                        lblStatus.Text = "已取消";
                        break;
                    }
                    catch (Exception ex)
                    {
                        string detail = ex.Message;
                        string log = $"[{DateTime.Now:HH:mm:ss}] 套图失败 job={Path.GetFileName(job.MaterialPath)}{Environment.NewLine}" +
                                     $"{ex.GetType().FullName}: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}";
                        if (ex.InnerException != null)
                        {
                            detail += $" | 内部: {ex.InnerException.Message}";
                            log += $"INNER {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}{Environment.NewLine}{ex.InnerException.StackTrace}{Environment.NewLine}";
                        }
                        Console.WriteLine(log);
                        UpdateJobStatus(job, MergeJobStatus.Failed, detail);
                        lblStatus.Text = $"处理失败: {Path.GetFileName(job.MaterialPath)} - {detail}";
                    }
                    finally
                    {
                        if (buildResult.IsBatchMode)
                        {
                            UpdateBatchSummary();
                        }
                    }
                }

                ResultPaths = batchState.SuccessOutputPaths.ToArray();
                ResultPath = ResultPaths.Count > 0 ? ResultPaths[0] : null;
                canReturnResults = ResultPaths.Count > 0;

                int successCount = buildResult.Jobs.Count(j => j.Status == MergeJobStatus.Completed);
                int failedCount = buildResult.Jobs.Count(j => j.Status == MergeJobStatus.Failed);
                int skippedCount = buildResult.Jobs.Count(j => j.Status == MergeJobStatus.Skipped);
                int canceledCount = buildResult.Jobs.Count(j => j.Status == MergeJobStatus.Canceled);

                if (!buildResult.IsBatchMode)
                {
                    if (successCount > 0)
                    {
                        lblStatus.Text = "完成！";
                        if (!isRemoteMode)
                        {
                            MessageBox.Show($"{buildResult.CompositeModeName}完成！\n保存路径: {ResultPath}", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            this.DialogResult = DialogResult.OK;
                            Close();
                        }
                    }
                    else if (canceledCount > 0)
                    {
                        ShowStatusMessage("任务已取消", "任务已取消。", "提示", MessageBoxIcon.Warning);
                    }
                    else
                    {
                        ShowStatusMessage("套图失败", "套图失败，请检查输入素材或结果消息。", "错误", MessageBoxIcon.Error);
                    }
                }
                else
                {
                    string summary = $"批量套图完成：成功 {successCount}，失败 {failedCount}，跳过 {skippedCount}，取消 {canceledCount}";
                    lblStatus.Text = summary;
                    UpdateBatchSummary();
                    if (!isRemoteMode)
                    {
                        MessageBox.Show(
                            canReturnResults
                                ? summary + "\n关闭窗口后将把成功结果载入画布。"
                                : summary,
                            "批量套图",
                            MessageBoxButtons.OK,
                            canReturnResults ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                    }
                }
            }
            finally
            {
                batchState?.PauseGate.Set();
                batchState?.Dispose();
                batchState = null;
                SetBusyState(false);
            }
        }

        private async void BtnMerge_Click(object sender, EventArgs e)
        {
            if (isRunning)
            {
                return;
            }

            ResetDialogResultState();
            BuildJobsResult buildResult = BuildJobs();
            if (buildResult == null)
            {
                return;
            }

            await ExecuteJobsAsync(buildResult);
        }

        private void BtnPauseResume_Click(object sender, EventArgs e)
        {
            if (!isRunning || batchState == null)
            {
                return;
            }

            if (batchState.IsPaused)
            {
                batchState.IsPaused = false;
                batchState.PauseGate.Set();
                btnPauseResume.Text = "暂停";
                lblStatus.Text = "继续执行...";
            }
            else
            {
                batchState.IsPaused = true;
                batchState.PauseGate.Reset();
                btnPauseResume.Text = "继续";
                lblStatus.Text = "已暂停，等待继续";
            }
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            if (!isRunning || batchState == null)
            {
                return;
            }

            btnCancel.Enabled = false;
            lblStatus.Text = "正在取消...";
            batchState.PauseGate.Set();
            batchState.CancellationSource.Cancel();
        }

        private void BtnClose_Click(object sender, EventArgs e)
        {
            if (isRunning)
            {
                if (!isRemoteMode)
                {
                    MessageBox.Show("当前任务正在执行，请先取消任务后再关闭窗口。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    lblStatus.Text = "当前远程任务正在执行，请先等待完成或取消";
                }
                return;
            }

            if (canReturnResults)
            {
                this.DialogResult = DialogResult.OK;
            }

            if (isRemoteMode)
            {
                Hide();
                return;
            }

            Close();
        }
    }

    public class GuideLine
    {
        public bool IsHorizontal { get; set; }
        public float Position { get; set; }
        public Color Color { get; set; }
    }

    // 通道控制类（用于管理每个通道的数据）
    public class ChannelControl
    {
        public Panel Panel { get; set; }
        public TextBox TextBox { get; set; }
        public int ChannelNumber { get; set; }
    }
}
