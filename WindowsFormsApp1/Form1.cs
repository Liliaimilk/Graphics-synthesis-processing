using RulerGridApp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WindowsFormsApp1
{
    public partial class Form1 : Form
    {
        private RulerCanvas canvas;
        private Panel workspacePanel;
        private Panel leftToolPanel;
        private Label lblStatus;
        private Label lblZoom;
        private Button btnMergeTool;
        private Button btnMoveTool;
        private ToolTip toolbarToolTip;

        private TrackBar zoomTrackBar;
        private Label zoomLabel;
        private Button resetZoomButton;
        private CanvasTool currentTool = CanvasTool.Move;

        public Form1()
        {
            SetupDarkTheme();
            SetupCanvas();
            SetupToolbar();
            SetupStatusBar();
            SetupDragDrop();
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

            zoomLabel = new Label
            {
                Text = "缩放: 100%",
                Location = new Point(110, 10),
                Size = new Size(60, 28),
                Font = new Font("微软雅黑", 9F),
                ForeColor = Color.FromArgb(200, 205, 215),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };

            zoomTrackBar = new TrackBar
            {
                Location = new Point(175, 10),
                Minimum = 10,
                Maximum = 300,
                Value = 100,
                TickFrequency = 25,
                Width = 200,
                Height = 30
            };
            zoomTrackBar.ValueChanged += ZoomTrackBar_ValueChanged;

            resetZoomButton = new Button
            {
                Text = "重置",
                Location = new Point(385, 10),
                Size = new Size(50, 28),
                Font = new Font("微软雅黑", 9F),
                BackColor = Color.FromArgb(40, 55, 80),
                ForeColor = Color.FromArgb(220, 225, 235),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            resetZoomButton.Click += (s, e) => canvas?.ResetView();

            toolbarPanel.Controls.Add(btnMergeTool);
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

                canvas.ClearScene();
                foreach (string path in validPaths)
                {
                    canvas.LoadImageFromFile(path);
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
        private const int DialogWidth = 760;
        private const int SingleDialogHeight = 380;
        private const int BatchDialogHeight = 720;

        private TextBox txtTemplateFolder;
        private TextBox txtMaterialFolder;
        private TextBox txtSavePath;
        private TextBox txtSeparator;
        private ComboBox cmbFormat;
        private ComboBox cmbCompositeMode;
        private CheckBox chkWhiteInk;
        private CheckBox chkVarnish;
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

        private bool isRunning;
        private bool canReturnResults;
        private BatchRunState batchState;

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
            txtSeparator = CreateTextBox(startX + labelWidth + 5, startY, 50);
            txtSeparator.Text = "-";

            var lblFormat = CreateLabel("格式:", startX + 145, startY, 40);
            cmbFormat = CreateComboBox(startX + 190, startY, 80);
            cmbFormat.Items.AddRange(new object[] { "TIF", "PSD", "JPEG", "PNG" });
            cmbFormat.SelectedIndex = 0;

            var lblMode = CreateLabel("模式:", startX + 290, startY, 40);
            cmbCompositeMode = CreateComboBox(startX + 335, startY, 110);
            cmbCompositeMode.Items.AddRange(new object[] { "套图标准模式", "满版模式" });
            cmbCompositeMode.SelectedIndex = 0;

            startY += rowHeight;
            chkWhiteInk = new CheckBox
            {
                Text = "白墨通道",
                Location = new Point(startX + labelWidth + 5, startY + 8),
                Size = new Size(80, 22),
                ForeColor = Color.FromArgb(200, 205, 215),
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Checked = true
            };

            chkVarnish = new CheckBox
            {
                Text = "光油通道",
                Location = new Point(startX + labelWidth + 100, startY + 8),
                Size = new Size(80, 22),
                ForeColor = Color.FromArgb(200, 205, 215),
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Checked = true
            };

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
                Size = new Size(DialogWidth - 56, 300),
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
                Size = new Size(pnlBatch.Width - 24, 190),
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
                lblMode, cmbCompositeMode,
                chkWhiteInk, chkVarnish, chkBatchMode, lblStatus,
                btnMerge, btnClose,
                pnlBatch
            });

            ToggleBatchModeLayout();
        }

        private void MergeDialog_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (isRunning)
            {
                e.Cancel = true;
                MessageBox.Show("当前任务正在执行，请先取消任务后再关闭窗口。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

        private BuildJobsResult BuildJobs()
        {
            if (string.IsNullOrEmpty(txtTemplateFolder.Text) || !Directory.Exists(txtTemplateFolder.Text))
            {
                MessageBox.Show("请选择有效的模版文件夹", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
            if (string.IsNullOrEmpty(txtMaterialFolder.Text) || !Directory.Exists(txtMaterialFolder.Text))
            {
                MessageBox.Show("请选择有效的素材文件夹", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
            if (string.IsNullOrEmpty(txtSavePath.Text) || !Directory.Exists(txtSavePath.Text))
            {
                MessageBox.Show("请选择有效的保存路径", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            string format = cmbFormat.SelectedItem?.ToString() ?? "TIF";
            if (string.Equals(format, "PSD", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("当前版本暂不支持真实 PSD 导出，请改用 TIF、PNG 或 JPEG。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            bool isBatchMode = chkBatchMode.Checked;
            List<string> templateFiles = GetImageFiles(txtTemplateFolder.Text);
            List<string> materialFiles = GetImageFiles(txtMaterialFolder.Text);

            if (templateFiles.Count == 0)
            {
                MessageBox.Show("模版文件夹未找到图片文件", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
            if (materialFiles.Count == 0)
            {
                MessageBox.Show("素材文件夹未找到图片文件", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
            if (isBatchMode && templateFiles.Count != 1)
            {
                MessageBox.Show("批量套图当前只支持单模版目录，请保证模版文件夹中只有一张图片。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            string templateFile = templateFiles[0];
            if (!isBatchMode)
            {
                materialFiles = new List<string> { materialFiles[0] };
            }

            string separator = string.IsNullOrWhiteSpace(txtSeparator.Text) ? "-" : txtSeparator.Text;
            TemplateCompositeMode compositeMode = cmbCompositeMode.SelectedIndex == 1
                ? TemplateCompositeMode.FullBleed
                : TemplateCompositeMode.Standard;
            string compositeModeName = cmbCompositeMode.SelectedItem?.ToString() ?? "套图标准模式";
            string exclusionMaskPath = compositeMode == TemplateCompositeMode.FullBleed
                ? @"D:\matrials\3-save\mask.png"
                : null;
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
                ExclusionMaskPath = exclusionMaskPath
            };
        }

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
            cmbCompositeMode.Enabled = !busy;
            chkWhiteInk.Enabled = !busy;
            chkVarnish.Enabled = !busy;
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
                MessageBox.Show($"模版预检失败: {templateValidation.ErrorMessage}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                MessageBox.Show(
                    "以下素材预检失败，将自动跳过：\n" + string.Join("\n", invalidMessages),
                    "预检提示",
                    MessageBoxButtons.OK,
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
                                chkWhiteInk.Checked ? "White" : null,
                                chkVarnish.Checked ? "Varnish" : null,
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
                        UpdateJobStatus(job, MergeJobStatus.Failed, ex.Message);
                        lblStatus.Text = $"处理失败: {Path.GetFileName(job.MaterialPath)}";
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
                        MessageBox.Show($"{buildResult.CompositeModeName}完成！\n保存路径: {ResultPath}", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        this.DialogResult = DialogResult.OK;
                        Close();
                    }
                    else if (canceledCount > 0)
                    {
                        MessageBox.Show("任务已取消。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    else
                    {
                        MessageBox.Show("套图失败，请检查输入素材或结果消息。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else
                {
                    string summary = $"批量套图完成：成功 {successCount}，失败 {failedCount}，跳过 {skippedCount}，取消 {canceledCount}";
                    lblStatus.Text = summary;
                    UpdateBatchSummary();
                    MessageBox.Show(
                        canReturnResults
                            ? summary + "\n关闭窗口后将把成功结果载入画布。"
                            : summary,
                        "批量套图",
                        MessageBoxButtons.OK,
                        canReturnResults ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
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
                MessageBox.Show("当前任务正在执行，请先取消任务后再关闭窗口。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (canReturnResults)
            {
                this.DialogResult = DialogResult.OK;
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
}
