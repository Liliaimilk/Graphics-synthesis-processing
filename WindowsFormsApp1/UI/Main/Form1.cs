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
        private const string RemoteWebSocketEndpoint = "ws://localhost:8080/websocket/2";

        private RulerCanvas canvas;
        private Panel workspacePanel;
        private Panel leftToolPanel;
        private Panel rightInfoPanel;
        private Label lblStatus;
        private Label lblZoom;
        private Label lblSelectionTitle;
        private Label lblSelectionHint;
        private Label lblSelectionSummary;
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
            contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320F));
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
            toolbarToolTip.SetToolTip(btnMoveTool, "移动/选择");
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
                Padding = new Padding(12, 14, 12, 14)
            };

            lblSelectionTitle = new Label
            {
                Text = "选中图片信息",
                Dock = DockStyle.Top,
                Height = 28,
                Font = new Font("微软雅黑", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(235, 238, 245),
                TextAlign = ContentAlignment.MiddleLeft
            };
            lblSelectionHint = new Label
            {
                Text = "点击画布中的图片后，这里会显示当前图片的宽高尺寸。",
                Dock = DockStyle.Top,
                Height = 42,
                Font = new Font("微软雅黑", 9F),
                ForeColor = Color.FromArgb(170, 178, 190),
                Padding = new Padding(0, 4, 0, 8)
            };

            Panel summaryCard = new Panel
            {
                Dock = DockStyle.Top,
                Height = 90,
                BackColor = Color.FromArgb(41, 53, 77),
                Padding = new Padding(14, 12, 14, 12),
                Margin = new Padding(0, 0, 0, 12)
            };

            Label summaryTitle = new Label
            {
                Text = "当前选中",
                Dock = DockStyle.Top,
                Height = 24,
                Font = new Font("微软雅黑", 10.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 243, 248)
            };
            summaryCard.Controls.Add(summaryTitle);

            lblSelectionSummary = new Label
            {
                Text = "未选择图片",
                Dock = DockStyle.Fill,
                Font = new Font("微软雅黑", 9.5F),
                ForeColor = Color.FromArgb(190, 198, 210),
                TextAlign = ContentAlignment.MiddleLeft
            };
            summaryCard.Controls.Add(lblSelectionSummary);

            Label sectionTitle = new Label
            {
                Text = "图片尺寸",
                Dock = DockStyle.Top,
                Height = 40,
                Font = new Font("微软雅黑", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(228, 233, 240),
                Padding = new Padding(0, 10, 0, 8)
            };
            TableLayoutPanel metricGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 210,
                ColumnCount = 2,
                RowCount = 3,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            metricGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            metricGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            metricGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
            metricGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
            metricGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
            metricGrid.Controls.Add(CreateMetricCard("W", "px", out lblSelectedWidthPx), 0, 0);
            metricGrid.Controls.Add(CreateMetricCard("H", "px", out lblSelectedHeightPx), 1, 0);
            metricGrid.Controls.Add(CreateMetricCard("宽度", "mm", out lblSelectedWidthMm), 0, 1);
            metricGrid.Controls.Add(CreateMetricCard("高度", "mm", out lblSelectedHeightMm), 1, 1);
            metricGrid.Controls.Add(CreateWideMetricCard("DPI", out lblSelectedDpi), 0, 2);
            metricGrid.SetColumnSpan(metricGrid.GetControlFromPosition(0, 2), 2);

            // DockStyle.Top 的控件按从下往上的顺序添加，最终显示顺序才稳定。
            rightInfoPanel.Controls.Add(metricGrid);
            rightInfoPanel.Controls.Add(lblSelectionTitle);
            rightInfoPanel.Controls.Add(lblSelectionHint);
            rightInfoPanel.Controls.Add(summaryCard);
            rightInfoPanel.Controls.Add(sectionTitle);
        }

        /// <summary>
        /// 创建右侧信息面板中的单个指标卡片。
        /// </summary>
        private static Panel CreateMetricCard(string title, string unit, out Label valueLabel)
        {
            Panel card = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(41, 53, 77),
                Margin = new Padding(6)
            };

            Label titleLabel = new Label
            {
                Text = title,
                AutoSize = false,
                Location = new Point(12, 10),
                Size = new Size(60, 18),
                Font = new Font("微软雅黑", 8.5F),
                ForeColor = Color.FromArgb(176, 186, 198)
            };
            card.Controls.Add(titleLabel);

            valueLabel = new Label
            {
                Text = unit == "px" ? "-- px" : $"-- {unit}",
                AutoSize = false,
                Location = new Point(12, 31),
                Size = new Size(126, 24),
                Font = new Font("微软雅黑", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(239, 242, 247)
            };
            card.Controls.Add(valueLabel);
            return card;
        }

        /// <summary>
        /// 创建横向铺满的一行指标卡片。
        /// </summary>
        private static Panel CreateWideMetricCard(string title, out Label valueLabel)
        {
            Panel card = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(41, 53, 77),
                Margin = new Padding(6)
            };

            Label titleLabel = new Label
            {
                Text = title,
                AutoSize = false,
                Location = new Point(12, 10),
                Size = new Size(80, 18),
                Font = new Font("微软雅黑", 8.5F),
                ForeColor = Color.FromArgb(176, 186, 198)
            };
            card.Controls.Add(titleLabel);

            valueLabel = new Label
            {
                Text = "--",
                AutoSize = false,
                Location = new Point(12, 31),
                Size = new Size(240, 24),
                Font = new Font("微软雅黑", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(239, 242, 247)
            };
            card.Controls.Add(valueLabel);
            return card;
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
                lblSelectionSummary.Text = "未选择图片";
                lblSelectedWidthPx.Text = "-- px";
                lblSelectedHeightPx.Text = "-- px";
                lblSelectedWidthMm.Text = "-- mm";
                lblSelectedHeightMm.Text = "-- mm";
                lblSelectedDpi.Text = "--";
                return;
            }

            lblSelectionSummary.Text = string.IsNullOrWhiteSpace(selectionInfo.ImageName)
                ? "未命名图片"
                : selectionInfo.ImageName;
            lblSelectedWidthPx.Text = $"{selectionInfo.WidthPx} px";
            lblSelectedHeightPx.Text = $"{selectionInfo.HeightPx} px";
            lblSelectedWidthMm.Text = $"{selectionInfo.WidthMm:0.##} mm";
            lblSelectedHeightMm.Text = $"{selectionInfo.HeightMm:0.##} mm";
            lblSelectedDpi.Text = Math.Abs(selectionInfo.HorizontalDpi - selectionInfo.VerticalDpi) < 0.01f
                ? $"{selectionInfo.HorizontalDpi:0.##}"
                : $"{selectionInfo.HorizontalDpi:0.##} x {selectionInfo.VerticalDpi:0.##}";
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

