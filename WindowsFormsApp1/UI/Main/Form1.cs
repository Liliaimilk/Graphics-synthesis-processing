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
        private Panel rightInfoPanel;
        private Label lblStatus;
        private Label lblZoom;
        private Label lblSelectionTitle;
        private Label lblSelectionHint;
        private Label lblSelectedWidthPx;
        private Label lblSelectedHeightPx;
        private Label lblSelectedWidthMm;
        private Label lblSelectedHeightMm;
        private Label lblSelectedDpi;
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
            this.Size = new Size(1560, 920);
            this.MinimumSize = new Size(1320, 780);
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

            TableLayoutPanel contentLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Color.FromArgb(25, 35, 55),
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52F));
            contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280F));
            contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            workspacePanel.Controls.Add(contentLayout);

            leftToolPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(58, 58, 58),
                Padding = new Padding(4, 8, 4, 4)
            };
            contentLayout.Controls.Add(leftToolPanel, 0, 0);

            Panel canvasHostPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(25, 35, 55),
                Padding = new Padding(0)
            };
            contentLayout.Controls.Add(canvasHostPanel, 1, 0);

            SetupSelectionInfoPanel();
            contentLayout.Controls.Add(rightInfoPanel, 2, 0);

            canvas = new RulerCanvas
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(35, 40, 50),
                ActiveTool = CanvasTool.Move
            };
            canvasHostPanel.Controls.Add(canvas);
            canvas.SelectedImageChanged += Canvas_SelectedImageChanged;

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
            toolbarToolTip.SetToolTip(btnMoveTool, "绉诲姩宸ュ叿");
            leftToolPanel.Controls.Add(btnMoveTool);

            SetCanvasTool(CanvasTool.Move);
            UpdateSelectionInfo(null);
        }

        /// <summary>
        /// 初始化右侧固定信息面板，用于展示当前选中图片的尺寸信息。
        /// </summary>
        private void SetupSelectionInfoPanel()
        {
            rightInfoPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(33, 43, 63),
                Padding = new Padding(16, 18, 16, 18)
            };

            lblSelectionTitle = new Label
            {
                Text = "选中图片信息",
                Dock = DockStyle.Top,
                Height = 30,
                Font = new Font("微软雅黑", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(235, 238, 245),
                TextAlign = ContentAlignment.MiddleLeft
            };
            rightInfoPanel.Controls.Add(lblSelectionTitle);

            lblSelectionHint = new Label
            {
                Text = "点击画布中的图片后，这里会显示当前图片的宽高尺寸。",
                Dock = DockStyle.Top,
                Height = 54,
                Font = new Font("微软雅黑", 9F),
                ForeColor = Color.FromArgb(170, 178, 190),
                Padding = new Padding(0, 6, 0, 10)
            };
            rightInfoPanel.Controls.Add(lblSelectionHint);

            Panel infoCard = new Panel
            {
                Dock = DockStyle.Top,
                Height = 226,
                BackColor = Color.FromArgb(41, 53, 77),
                Padding = new Padding(14)
            };
            rightInfoPanel.Controls.Add(infoCard);

            lblSelectedWidthPx = CreateSelectionInfoLabel("宽度(px): 未选择");
            lblSelectedHeightPx = CreateSelectionInfoLabel("高度(px): 未选择");
            lblSelectedWidthMm = CreateSelectionInfoLabel("宽度(mm): 未选择");
            lblSelectedHeightMm = CreateSelectionInfoLabel("高度(mm): 未选择");
            lblSelectedDpi = CreateSelectionInfoLabel("DPI: 未选择");

            infoCard.Controls.Add(lblSelectedDpi);
            infoCard.Controls.Add(lblSelectedHeightMm);
            infoCard.Controls.Add(lblSelectedWidthMm);
            infoCard.Controls.Add(lblSelectedHeightPx);
            infoCard.Controls.Add(lblSelectedWidthPx);
        }

        /// <summary>
        /// 创建右侧信息面板中的单行尺寸标签。
        /// </summary>
        private static Label CreateSelectionInfoLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Top,
                Height = 36,
                Font = new Font("微软雅黑", 10F),
                ForeColor = Color.FromArgb(225, 230, 238),
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        /// <summary>
        /// 画布选中项变化后，同步刷新右侧信息面板。
        /// </summary>
        private void Canvas_SelectedImageChanged(CanvasSelectionInfo selectionInfo)
        {
            UpdateSelectionInfo(selectionInfo);
        }

        /// <summary>
        /// 将选中图片的像素与毫米尺寸显示到右侧固定面板。
        /// </summary>
        private void UpdateSelectionInfo(CanvasSelectionInfo selectionInfo)
        {
            if (lblSelectedWidthPx == null)
            {
                return;
            }

            if (selectionInfo == null)
            {
                lblSelectedWidthPx.Text = "宽度(px): 未选择";
                lblSelectedHeightPx.Text = "高度(px): 未选择";
                lblSelectedWidthMm.Text = "宽度(mm): 未选择";
                lblSelectedHeightMm.Text = "高度(mm): 未选择";
                lblSelectedDpi.Text = "DPI: 未选择";
                return;
            }

            lblSelectedWidthPx.Text = $"宽度(px): {selectionInfo.WidthPx}";
            lblSelectedHeightPx.Text = $"高度(px): {selectionInfo.HeightPx}";
            lblSelectedWidthMm.Text = $"宽度(mm): {selectionInfo.WidthMm:0.##}";
            lblSelectedHeightMm.Text = $"高度(mm): {selectionInfo.HeightMm:0.##}";
            lblSelectedDpi.Text = Math.Abs(selectionInfo.HorizontalDpi - selectionInfo.VerticalDpi) < 0.01f
                ? $"DPI: {selectionInfo.HorizontalDpi:0.##}"
                : $"DPI: {selectionInfo.HorizontalDpi:0.##} x {selectionInfo.VerticalDpi:0.##}";
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
            await OnMainFormShownAsync();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            HandleFormClosingCleanup();
        }

        private async Task StartRemoteWebSocketServerAsync()
        {
            await StartRemoteWebSocketServerCoreAsync();
        }

        private void StopRemoteWebSocketServer()
        {
            StopRemoteWebSocketServerCore();
        }

        private async Task RunRemoteWebSocketClientLoopAsync(CancellationToken cancellationToken)
        {
            await RunRemoteWebSocketClientLoopCoreAsync(cancellationToken);
        }

        private async Task ReceiveRemoteMessagesAsync(WebSocket webSocket, CancellationToken cancellationToken)
        {
            await ReceiveRemoteMessagesCoreAsync(webSocket, cancellationToken);
        }

        private RemoteMergeRequest CreateRemoteMergeRequest(string rawJson)
        {
            return BuildRemoteMergeRequest(rawJson);
        }

        private RemoteMergeMessage DeserializeRemoteMergeMessage(string rawJson)
        {
            return DeserializeRemoteMessage(rawJson);
        }

        private void EnqueueRemoteMergeRequest(RemoteMergeRequest request)
        {
            QueueRemoteMergeRequest(request);
        }

        private async Task ProcessRemoteMergeQueueAsync()
        {
            await ProcessRemoteMergeQueueCoreAsync();
        }

        private void SafeUpdateStatus(string message)
        {
            UpdateStatusSafely(message);
        }

        private void BtnMergeTool_Click(object sender, EventArgs e)
        {
            HandleMergeToolClick();
        }

        private void BtnLayoutOutputTool_Click(object sender, EventArgs e)
        {
            HandleLayoutOutputToolClick();
        }

        private void LoadResultToCanvas(string path)
        {
            LoadResultToCanvasCore(path);
        }

        private void LoadResultsToCanvas(IEnumerable<string> paths)
        {
            LoadResultsToCanvasCore(paths);
        }

        private void ZoomTrackBar_ValueChanged(object sender, EventArgs e)
        {
            ApplyZoomTrackBarValue();
        }

        private void SetCanvasTool(CanvasTool tool)
        {
            ApplyCanvasToolState(tool);
        }
    }
}

