using RulerGridApp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
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

            // 套图工具按钮
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

            // 缩放标签
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

            // 缩放滑块 (10% - 300%)
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

            // 重置按钮
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

            // 同步 canvas 缩放事件到工具栏
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
                if (dialog.ShowDialog() == DialogResult.OK && !string.IsNullOrEmpty(dialog.ResultPath))
                {
                    LoadResultToCanvas(dialog.ResultPath);
                }
            }
        }

        private void LoadResultToCanvas(string path)
        {
            try
            {
                lblStatus.Text = "加载图片...";
                Application.DoEvents();

                using (var resultBitmap = new Bitmap(path))
                {
                    canvas.LoadImage(new Bitmap(resultBitmap));
                }

                lblStatus.Text = "完成！";
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

    // ========== 套图弹窗 ==========
    public class MergeDialog : Form
    {
        private TextBox txtTemplateFolder;
        private TextBox txtMaterialFolder;
        private TextBox txtSavePath;
        private TextBox txtSeparator;
        private ComboBox cmbFormat;
        private ComboBox cmbCompositeMode;
        private CheckBox chkWhiteInk;
        private CheckBox chkVarnish;
        private Button btnMerge;
        private Label lblStatus;
        private Button btnClose;

        public string ResultPath { get; private set; }

        public MergeDialog()
        {
            SetupDarkTheme();
            SetupControls();
            LoadSavedPaths();
        }

        private void SetupDarkTheme()
        {
            this.Text = "套图";
            this.Size = new Size(520, 380);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(25, 35, 55);
            this.ForeColor = Color.FromArgb(220, 225, 235);

            // 居中显示
            this.StartPosition = FormStartPosition.CenterParent;
        }

        private void SetupControls()
        {
            int startX = 20;
            int startY = 20;
            int rowHeight = 40;
            int labelWidth = 80;
            int textBoxWidth = 320;
            int btnWidth = 60;

            // 模版文件夹
            var lblTemplate = CreateLabel("模版:", startX, startY, labelWidth);
            txtTemplateFolder = CreateTextBox(startX + labelWidth + 5, startY, textBoxWidth);
            var btnBrowseTemplate = CreateButton("浏览", startX + labelWidth + textBoxWidth + 10, startY, btnWidth);
            btnBrowseTemplate.Click += (s, e) => BrowseFolder(txtTemplateFolder);

            // 素材文件夹
            startY += rowHeight;
            var lblMaterial = CreateLabel("素材:", startX, startY, labelWidth);
            txtMaterialFolder = CreateTextBox(startX + labelWidth + 5, startY, textBoxWidth);
            var btnBrowseMaterial = CreateButton("浏览", startX + labelWidth + textBoxWidth + 10, startY, btnWidth);
            btnBrowseMaterial.Click += (s, e) => BrowseFolder(txtMaterialFolder);

            // 保存路径
            startY += rowHeight;
            var lblSave = CreateLabel("保存:", startX, startY, labelWidth);
            txtSavePath = CreateTextBox(startX + labelWidth + 5, startY, textBoxWidth);
            var btnBrowseSave = CreateButton("浏览", startX + labelWidth + textBoxWidth + 10, startY, btnWidth);
            btnBrowseSave.Click += (s, e) => BrowseFolder(txtSavePath);

            // 分隔符 + 格式 + 模式
            startY += rowHeight;
            var lblSeparator = CreateLabel("分隔符:", startX, startY, labelWidth);
            txtSeparator = CreateTextBox(startX + labelWidth + 5, startY, 50);
            txtSeparator.Text = "-";

            var lblFormat = CreateLabel("格式:", startX + 130, startY, 40);
            cmbFormat = CreateComboBox(startX + 175, startY, 70);
            cmbFormat.Items.AddRange(new object[] { "TIF", "PSD", "JPEG", "PNG" });
            cmbFormat.SelectedIndex = 0;

            var lblMode = CreateLabel("模式:", startX + 260, startY, 40);
            cmbCompositeMode = CreateComboBox(startX + 305, startY, 100);
            cmbCompositeMode.Items.AddRange(new object[] { "套图标准模式", "满版模式" });
            cmbCompositeMode.SelectedIndex = 0;

            // 通道设置
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

            // 状态标签
            lblStatus = new Label
            {
                Text = "就绪",
                Location = new Point(startX + 250, startY + 8),
                Size = new Size(150, 22),
                Font = new Font("微软雅黑", 9F),
                ForeColor = Color.FromArgb(150, 155, 165),
                BackColor = Color.Transparent
            };

            // 按钮
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
            btnClose.Click += (s, e) => this.Close();

            // 添加所有控件
            this.Controls.AddRange(new Control[] {
                lblTemplate, txtTemplateFolder, btnBrowseTemplate,
                lblMaterial, txtMaterialFolder, btnBrowseMaterial,
                lblSave, txtSavePath, btnBrowseSave,
                lblSeparator, txtSeparator,
                lblFormat, cmbFormat,
                lblMode, cmbCompositeMode,
                chkWhiteInk, chkVarnish, lblStatus,
                btnMerge, btnClose
            });
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

        private string[] imageExtensions = { ".psd", ".psb", ".tif", ".tiff", ".jpg", ".jpeg", ".png", ".bmp" };

        private string FindFirstImage(string folderPath)
        {
            if (!Directory.Exists(folderPath)) return null;
            var files = Directory.GetFiles(folderPath)
                .Where(f => imageExtensions.Contains(Path.GetExtension(f).ToLower()))
                .OrderBy(f => f)
                .ToArray();
            return files.Length > 0 ? files[0] : null;
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

        private void BtnMerge_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(txtTemplateFolder.Text) || !Directory.Exists(txtTemplateFolder.Text))
            {
                MessageBox.Show("请选择有效的模版文件夹", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrEmpty(txtMaterialFolder.Text) || !Directory.Exists(txtMaterialFolder.Text))
            {
                MessageBox.Show("请选择有效的素材文件夹", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrEmpty(txtSavePath.Text) || !Directory.Exists(txtSavePath.Text))
            {
                MessageBox.Show("请选择有效的保存路径", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                btnMerge.Enabled = false;
                btnMerge.Text = "处理中...";
                lblStatus.Text = "正在查找图片...";
                Application.DoEvents();

                string templateFile = FindFirstImage(txtTemplateFolder.Text);
                string materialFile = FindFirstImage(txtMaterialFolder.Text);

                if (templateFile == null)
                {
                    MessageBox.Show("模版文件夹未找到图片文件", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                if (materialFile == null)
                {
                    MessageBox.Show("素材文件夹未找到图片文件", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                string separator = txtSeparator.Text;
                if (string.IsNullOrEmpty(separator)) separator = "-";
                string format = cmbFormat.SelectedItem?.ToString() ?? "TIF";
                TemplateCompositeMode compositeMode = cmbCompositeMode.SelectedIndex == 1
                    ? TemplateCompositeMode.FullBleed
                    : TemplateCompositeMode.Standard;
                string compositeModeName = cmbCompositeMode.SelectedItem?.ToString() ?? "套图标准模式";
                string exclusionMaskPath = compositeMode == TemplateCompositeMode.FullBleed
                    ? @"D:\matrials\3-save\mask.png"
                    : null;
                string ext = format.ToLower() == "jpeg" || format.ToLower() == "jpg" ? ".jpg" :
                             format.ToLower() == "png" ? ".png" :
                             format.ToLower() == "psd" ? ".psd" : ".tif";

                string baseName = GetBaseName(templateFile) + separator + GetBaseName(materialFile);
                string outputFile = NextOutputFile(txtSavePath.Text, baseName, ext);

                Console.WriteLine($"模版路径名称：{templateFile}");
                Console.WriteLine($"素材路径名称：{materialFile}");
                Console.WriteLine($"套图模式：{compositeModeName}");

                AsposePSDHelper.ProcessTifMode(
                    templateFile,
                    materialFile,
                    outputFile,
                    format,
                    msg => { lblStatus.Text = msg; Application.DoEvents(); },
                    chkWhiteInk.Checked ? "White" : null,
                    chkVarnish.Checked ? "Varnish" : null,
                    0,
                    0,
                    compositeMode,
                    exclusionMaskPath);

                ResultPath = outputFile;
                lblStatus.Text = "完成！";
                Console.WriteLine($"输出路径名称：{outputFile}");
                MessageBox.Show($"{compositeModeName}完成！\n保存路径: {outputFile}", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                lblStatus.Text = "处理失败";
                MessageBox.Show($"套图失败: {ex.Message}\n{ex.StackTrace}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnMerge.Enabled = true;
                btnMerge.Text = "开始套图";
            }
        }
    }
        public class GuideLine
    {
        public bool IsHorizontal { get; set; }
        public float Position { get; set; }
        public Color Color { get; set; }
    }
}