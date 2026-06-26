using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bitmap = System.Drawing.Bitmap;
using Graphics = System.Drawing.Graphics;
using Rectangle = System.Drawing.Rectangle;
using RectangleF = System.Drawing.RectangleF;

namespace WindowsFormsApp1
{
    public sealed class LayoutOutputDialog : Form
    {
        private const int LeftPanelWidth = 510;
        private const int RightPanelWidth = 620;
        private const int DialogWidth = 1180;
        private const int DialogHeight = 640;

        // Left panel controls
        private TextBox txtSourceFolder;
        private TextBox txtOutputFolder;
        private TextBox txtOutputFileName;
        private TextBox txtSheetWidth;
        private TextBox txtSheetHeight;
        private TextBox txtDpi;
        private TextBox txtStartX;
        private TextBox txtStartY;
        private TextBox txtSlotWidth;
        private TextBox txtSlotHeight;
        private TextBox txtHorizontalGap;
        private TextBox txtVerticalGap;
        private TextBox txtRows;
        private TextBox txtColumns;
        private Button btnBrowseSource;
        private Button btnBrowseOutput;
        private Button btnRefreshPreview;

        private Button btnPreviewAll;
        private Button btnRun;

        private Button loadTiffButton;
        private Button btnClose;
        private Label lblStatus;

        // Right panel controls
        private Panel previewHost;
        private PictureBox picPreview;
        private Label lblPreviewTitle;
        private Label lblPreviewSummary;
        private Label lblPreviewHint;

        private bool isRunning;
        private Dictionary<string, Image> thumbnailCache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private List<string> currentImageFiles = new List<string>();

        public string ResultPath { get; private set; }

        public LayoutOutputDialog()
        {
            SetupDarkTheme();
            SetupControls();
            LoadSavedPaths();
            // 预览加载
            // RefreshPreview();
        }

        private void SetupDarkTheme()
        {
            Text = "排版输出";
            Size = new Size(DialogWidth, DialogHeight);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(25, 35, 55);
            ForeColor = Color.FromArgb(220, 225, 235);
        }

        private void SetupControls()
        {
            // ========== Left Panel ==========
            Panel leftPanel = new Panel
            {
                Location = new Point(10, 10),
                Size = new Size(LeftPanelWidth, DialogHeight - 20),
                BackColor = Color.Transparent,
                AutoScroll = false
            };
            Controls.Add(leftPanel);

            int startX = 0;
            int startY = 0;
            int rowHeight = 36;
            int labelWidth = 85;
            int textBoxWidth = 275;
            int btnWidth = 56;

            // Source folder row
            leftPanel.Controls.Add(CreateLabel("源目录:", startX, startY, labelWidth));
            txtSourceFolder = CreateTextBox(startX + labelWidth + 5, startY, textBoxWidth);
            txtSourceFolder.TextChanged += (s, e) => OnSourceFolderChanged();
            leftPanel.Controls.Add(txtSourceFolder);
            btnBrowseSource = CreateButton("浏览", startX + labelWidth + textBoxWidth + 10, startY, btnWidth);
            btnBrowseSource.Click += (s, e) => BrowseFolder(txtSourceFolder, false);
            leftPanel.Controls.Add(btnBrowseSource);

            // Output folder row
            startY += rowHeight;
            leftPanel.Controls.Add(CreateLabel("输出目录:", startX, startY, labelWidth));
            txtOutputFolder = CreateTextBox(startX + labelWidth + 5, startY, textBoxWidth);
            leftPanel.Controls.Add(txtOutputFolder);
            btnBrowseOutput = CreateButton("浏览", startX + labelWidth + textBoxWidth + 10, startY, btnWidth);
            btnBrowseOutput.Click += (s, e) => BrowseFolder(txtOutputFolder, true);
            leftPanel.Controls.Add(btnBrowseOutput);

            // Output filename row
            startY += rowHeight;
            leftPanel.Controls.Add(CreateLabel("输出文件名:", startX, startY, labelWidth));
            txtOutputFileName = CreateTextBox(startX + labelWidth + 5, startY, textBoxWidth);
            txtOutputFileName.Text = "layout-output";
            leftPanel.Controls.Add(txtOutputFileName);

            // Sheet size row
            startY += rowHeight + 5;
            leftPanel.Controls.Add(CreateLabel("大图宽(mm):", startX, startY, labelWidth));
            txtSheetWidth = CreateTextBox(startX + labelWidth + 5, startY, 70);
            txtSheetWidth.Text = "600";
            txtSheetWidth.TextChanged += (s, e) => MarkPreviewDirty();
            leftPanel.Controls.Add(txtSheetWidth);
            leftPanel.Controls.Add(CreateLabel("大图高(mm):", startX + 160, startY, 80));
            txtSheetHeight = CreateTextBox(startX + 245, startY, 70);
            txtSheetHeight.Text = "900";
            txtSheetHeight.TextChanged += (s, e) => MarkPreviewDirty();
            leftPanel.Controls.Add(txtSheetHeight);
            leftPanel.Controls.Add(CreateLabel("DPI:", startX + 320, startY, 35));
            txtDpi = CreateTextBox(startX + 358, startY, 55);
            txtDpi.Text = "300";
            txtDpi.TextChanged += (s, e) => MarkPreviewDirty();
            leftPanel.Controls.Add(txtDpi);

            // Start position row
            startY += rowHeight;
            leftPanel.Controls.Add(CreateLabel("首格X(mm):", startX, startY, labelWidth));
            txtStartX = CreateTextBox(startX + labelWidth + 5, startY, 70);
            txtStartX.Text = "10";
            txtStartX.TextChanged += (s, e) => MarkPreviewDirty();
            leftPanel.Controls.Add(txtStartX);
            leftPanel.Controls.Add(CreateLabel("首格Y(mm):", startX + 160, startY, 80));
            txtStartY = CreateTextBox(startX + 245, startY, 70);
            txtStartY.Text = "10";
            txtStartY.TextChanged += (s, e) => MarkPreviewDirty();
            leftPanel.Controls.Add(txtStartY);
            leftPanel.Controls.Add(CreateLabel("列数:", startX + 320, startY, 35));
            txtColumns = CreateTextBox(startX + 358, startY, 55);
            txtColumns.Text = "3";
            txtColumns.TextChanged += (s, e) => MarkPreviewDirty();
            leftPanel.Controls.Add(txtColumns);

            // Slot size row
            startY += rowHeight;
            leftPanel.Controls.Add(CreateLabel("格宽(mm):", startX, startY, labelWidth));
            txtSlotWidth = CreateTextBox(startX + labelWidth + 5, startY, 70);
            txtSlotWidth.Text = "180";
            txtSlotWidth.TextChanged += (s, e) => MarkPreviewDirty();
            leftPanel.Controls.Add(txtSlotWidth);
            leftPanel.Controls.Add(CreateLabel("格高(mm):", startX + 160, startY, 80));
            txtSlotHeight = CreateTextBox(startX + 245, startY, 70);
            txtSlotHeight.Text = "240";
            txtSlotHeight.TextChanged += (s, e) => MarkPreviewDirty();
            leftPanel.Controls.Add(txtSlotHeight);
            leftPanel.Controls.Add(CreateLabel("行数:", startX + 320, startY, 35));
            txtRows = CreateTextBox(startX + 358, startY, 55);
            txtRows.Text = "3";
            txtRows.TextChanged += (s, e) => MarkPreviewDirty();
            leftPanel.Controls.Add(txtRows);

            // Gap row
            startY += rowHeight;
            leftPanel.Controls.Add(CreateLabel("横间距(mm):", startX, startY, labelWidth));
            txtHorizontalGap = CreateTextBox(startX + labelWidth + 5, startY, 70);
            txtHorizontalGap.Text = "10";
            txtHorizontalGap.TextChanged += (s, e) => MarkPreviewDirty();
            leftPanel.Controls.Add(txtHorizontalGap);
            leftPanel.Controls.Add(CreateLabel("纵间距(mm):", startX + 160, startY, 80));
            txtVerticalGap = CreateTextBox(startX + 245, startY, 70);
            txtVerticalGap.Text = "10";
            txtVerticalGap.TextChanged += (s, e) => MarkPreviewDirty();
            leftPanel.Controls.Add(txtVerticalGap);

            // Buttons row
            startY += rowHeight + 10;

            btnPreviewAll = new Button
            {
                Text = "一键排版",
                Location = new Point(startX, startY),
                Size = new Size(115, 32),
                Font = new Font("微软雅黑", 9F),
                BackColor = Color.FromArgb(55, 85, 120),
                ForeColor = Color.FromArgb(220, 225, 235),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnPreviewAll.Click += (s, e) => RefreshPreview("all");
            leftPanel.Controls.Add(btnPreviewAll);

            btnRefreshPreview = new Button
            {
                Text = "刷新预览",
                Location = new Point(startX + 125, startY),
                Size = new Size(120, 32),
                Font = new Font("微软雅黑", 9F),
                BackColor = Color.FromArgb(55, 85, 120),
                ForeColor = Color.FromArgb(220, 225, 235),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnRefreshPreview.Click += (s, e) => RefreshPreview();
            leftPanel.Controls.Add(btnRefreshPreview);

            btnRun = new Button
            {
                Text = "开始排版输出",
                Location = new Point(startX + 250, startY),
                Size = new Size(120, 32),
                Font = new Font("微软雅黑", 9F),
                BackColor = Color.FromArgb(45, 100, 160),
                ForeColor = Color.FromArgb(220, 225, 235),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnRun.Click += BtnRun_Click;
            leftPanel.Controls.Add(btnRun);

            btnClose = new Button
            {
                Text = "关闭",
                Location = new Point(startX + 375, startY),
                Size = new Size(90, 32),
                Font = new Font("微软雅黑", 9F),
                BackColor = Color.FromArgb(45, 60, 85),
                ForeColor = Color.FromArgb(220, 225, 235),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnClose.Click += BtnClose_Click;
            leftPanel.Controls.Add(btnClose);

            startY += rowHeight + 10;
            loadTiffButton = new Button
            {
                Text = "载入TIFF",
                Location = new Point(startX, startY),
                Size = new Size(120, 32),
                Font = new Font("微软雅黑", 9F),
                BackColor = Color.FromArgb(55, 85, 120),
                ForeColor = Color.FromArgb(220, 225, 235),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            loadTiffButton.Click+= (s, e) => LoadTiffButton_Click(s, e);
            leftPanel.Controls.Add(loadTiffButton);


            // Status label
            startY += rowHeight + 12;
            lblStatus = new Label
            {
                Location = new Point(startX, startY),
                Size = new Size(LeftPanelWidth - 12, 28),
                Font = new Font("微软雅黑", 8.5F),
                ForeColor = Color.FromArgb(200, 205, 215),
                BackColor = Color.Transparent,
                Text = "点击\"刷新预览\"查看版面布局"
            };
            leftPanel.Controls.Add(lblStatus);

            // ========== Right Panel (Preview) ==========
            int rightPanelX = LeftPanelWidth + 30;

            // Preview title
            lblPreviewTitle = new Label
            {
                Location = new Point(rightPanelX, 10),
                Size = new Size(RightPanelWidth, 25),
                Font = new Font("微软雅黑", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(220, 225, 235),
                BackColor = Color.Transparent,
                Text = "版面预览"
            };
            Controls.Add(lblPreviewTitle);

            // Preview summary
            lblPreviewSummary = new Label
            {
                Location = new Point(rightPanelX, 38),
                Size = new Size(RightPanelWidth, 20),
                Font = new Font("微软雅黑", 9F),
                ForeColor = Color.FromArgb(180, 185, 200),
                BackColor = Color.Transparent,
                Text = "容量: 0 | 图片: 0 | 状态: 未刷新"
            };
            Controls.Add(lblPreviewSummary);

            // Preview host panel with border
            previewHost = new Panel
            {
                Location = new Point(rightPanelX, 62),
                Size = new Size(RightPanelWidth, DialogHeight - 160),
                BackColor = Color.FromArgb(20, 28, 45),
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(5)
            };
            Controls.Add(previewHost);

            // Preview PictureBox
            picPreview = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.CenterImage,
                BackColor = Color.FromArgb(35, 45, 65)
            };
            previewHost.Controls.Add(picPreview);

            // Preview hint
            lblPreviewHint = new Label
            {
                Location = new Point(rightPanelX, DialogHeight - 85),
                Size = new Size(RightPanelWidth, 30),
                Font = new Font("微软雅黑", 8.5F),
                ForeColor = Color.FromArgb(140, 145, 160),
                BackColor = Color.Transparent,
                Text = "灰框为空位，彩色为缩略示意，导出以原图为准"
            };
            Controls.Add(lblPreviewHint);
        }

        private void OnSourceFolderChanged()
        {
            // Clear thumbnail cache when source folder changes
            ClearThumbnailCache();
            MarkPreviewDirty();
        }

        private void ClearThumbnailCache()
        {
            foreach (var img in thumbnailCache.Values)
            {
                if (img != null) img.Dispose();
            }
            thumbnailCache.Clear();
            currentImageFiles.Clear();
        }

        private void MarkPreviewDirty()
        {
            // Optional: could disable auto-refresh for now
        }

        private void LoadTiffButton_Click(object sender, EventArgs e)
        {
            
        }

        // 刷新预览
        private void RefreshPreview(string mode = null)
        {
            if (isRunning)
                return;

            try
            {
                lblStatus.Text = "正在刷新预览...";
                Application.DoEvents();

                // 构建请求，预览不要求提供源文件夹
                SheetLayoutSettings settings = TryBuildSettings();
                if (settings == null)
                {
                    // 检查校验
                    RenderEmptyPreview("请检查参数是否有效");
                    lblStatus.Text = "参数无效，请检查输入";
                    return;
                }

                // 一键排版,获取图片资源
                if(mode == "all")
                {
                    string sourceFolder = txtSourceFolder.Text.Trim();
                    List<string> imageFiles = new List<string>();
                    if (!string.IsNullOrWhiteSpace(sourceFolder) && Directory.Exists(sourceFolder))
                    {
                        imageFiles = LayoutOutputHelper.GetImageFiles(sourceFolder);
                    }
                    currentImageFiles = imageFiles;
                }
               

                // 准备版面布局
                LayoutOutputHelper.PreparedLayout prepared;
                try
                {
                    prepared = LayoutOutputHelper.PrepareLayout(settings);
                }
                catch (ArgumentException ex)
                {
                    RenderEmptyPreview(ex.Message);
                    lblStatus.Text = "版面超出边界，请调整参数";
                    return;
                }

                // 更新预览信息和状态
                if(mode == "all")
                {
                    UpdatePreviewSummary(prepared, currentImageFiles);
                    // 渲染预览图像
                    RenderPreview(prepared,currentImageFiles);
                }
                else
                {
                    
                    RenderPreview(prepared);
                }

               
                

                lblStatus.Text = "预览已刷新";
            }
            catch (Exception ex)
            {
                RenderEmptyPreview($"预览失败: {ex.Message}");
                lblStatus.Text = "预览失败";
            }
        }

        private SheetLayoutSettings TryBuildSettings()
        {
            decimal sheetWidth, sheetHeight, dpi, startX, startY, slotWidth, slotHeight, hGap, vGap;
            int rows, columns;

            string w = txtSheetWidth.Text?.Trim() ?? "";
            string h = txtSheetHeight.Text?.Trim() ?? "";
            string d = txtDpi.Text?.Trim() ?? "";
            string sx = txtStartX.Text?.Trim() ?? "";
            string sy = txtStartY.Text?.Trim() ?? "";
            string sw = txtSlotWidth.Text?.Trim() ?? "";
            string sh = txtSlotHeight.Text?.Trim() ?? "";
            string hg = txtHorizontalGap.Text?.Trim() ?? "";
            string vg = txtVerticalGap.Text?.Trim() ?? "";
            string r = txtRows.Text?.Trim() ?? "";
            string c = txtColumns.Text?.Trim() ?? "";

            if (!decimal.TryParse(w, NumberStyles.Number, CultureInfo.InvariantCulture, out sheetWidth) || sheetWidth <= 0)
                return null;
            if (!decimal.TryParse(h, NumberStyles.Number, CultureInfo.InvariantCulture, out sheetHeight) || sheetHeight <= 0)
                return null;
            if (!decimal.TryParse(d, NumberStyles.Number, CultureInfo.InvariantCulture, out dpi) || dpi <= 0)
                return null;
            if (!decimal.TryParse(sx, NumberStyles.Number, CultureInfo.InvariantCulture, out startX) || startX < 0)
                return null;
            if (!decimal.TryParse(sy, NumberStyles.Number, CultureInfo.InvariantCulture, out startY) || startY < 0)
                return null;
            if (!decimal.TryParse(sw, NumberStyles.Number, CultureInfo.InvariantCulture, out slotWidth) || slotWidth <= 0)
                return null;
            if (!decimal.TryParse(sh, NumberStyles.Number, CultureInfo.InvariantCulture, out slotHeight) || slotHeight <= 0)
                return null;
            if (!decimal.TryParse(hg, NumberStyles.Number, CultureInfo.InvariantCulture, out hGap) || hGap < 0)
                return null;
            if (!decimal.TryParse(vg, NumberStyles.Number, CultureInfo.InvariantCulture, out vGap) || vGap < 0)
                return null;
            if (!int.TryParse(r, out rows) || rows <= 0)
                return null;
            if (!int.TryParse(c, out columns) || columns <= 0)
                return null;

            return new SheetLayoutSettings
            {
                SheetWidthMm = sheetWidth,
                SheetHeightMm = sheetHeight,
                Dpi = dpi,
                StartXmm = startX,
                StartYmm = startY,
                SlotWidthMm = slotWidth,
                SlotHeightMm = slotHeight,
                HorizontalGapMm = hGap,
                VerticalGapMm = vGap,
                Rows = rows,
                Columns = columns
            };
        }
        // 更新预览摘要信息及状态
        private void UpdatePreviewSummary(LayoutOutputHelper.PreparedLayout prepared, List<string> imageFiles)
        {
            int capacity = prepared.Capacity;
            int imageCount = imageFiles.Count;
            string statusText;

            if (imageCount > capacity)
            {
                statusText = $"容量不足 (需要 {imageCount} 张，仅能放 {capacity} 张)";
                lblPreviewSummary.ForeColor = Color.FromArgb(255, 100, 100);
            }
            else if (imageCount == 0)
            {
                statusText = "未找到图片";
                lblPreviewSummary.ForeColor = Color.FromArgb(180, 185, 200);
            }
            else
            {
                statusText = "正常";
                lblPreviewSummary.ForeColor = Color.FromArgb(100, 200, 120);
            }

            lblPreviewSummary.Text = $"容量: {capacity} | 图片: {imageCount} | 状态: {statusText}";
        }

        private void RenderEmptyPreview(string message)
        {
            int w = RightPanelWidth - 12;
            int h = previewHost.Height - 12;

            using (Bitmap bmp = new Bitmap(w, h))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(35, 45, 65));

                using (Font f = new Font("微软雅黑", 10F))
                using (Brush br = new SolidBrush(Color.FromArgb(150, 155, 170)))
                {
                    StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(message, f, br, new RectangleF(0, 0, w, h), sf);
                }

                if (picPreview.Image != null)
                {
                    var old = picPreview.Image;
                    picPreview.Image = null;
                    old.Dispose();
                }
                picPreview.Image = new Bitmap(bmp);
            }
        }
        // 渲染预览图像
        private void RenderPreview(LayoutOutputHelper.PreparedLayout prepared,List<string> imageFiles = null)
        {
            int availW = RightPanelWidth - 12;
            int availH = previewHost.Height - 12;

            // 计算缩放比例使画布在预览区域中显示合适大小
            float scaleX = (float)availW / prepared.CanvasWidthPx;
            float scaleY = (float)availH / prepared.CanvasHeightPx;
            float scale = Math.Min(scaleX, scaleY);

            // 在画布周围添加内边距
            int canvasW = (int)(prepared.CanvasWidthPx * scale);
            int canvasH = (int)(prepared.CanvasHeightPx * scale);
            int offsetX = (availW - canvasW) / 2;
            int offsetY = (availH - canvasH) / 2;

            using (Bitmap bmp = new Bitmap(availW, availH))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(Color.FromArgb(35, 45, 65));

                // 绘制画布背景
                Rectangle canvasRect = new Rectangle(offsetX, offsetY, canvasW, canvasH);
                using (Brush canvasBrush = new SolidBrush(Color.FromArgb(245, 245, 250)))
                {
                    g.FillRectangle(canvasBrush, canvasRect);
                }

                // 绘制画布边框
                using (Pen canvasPen = new Pen(Color.FromArgb(120, 130, 150), 1))
                {
                    g.DrawRectangle(canvasPen, canvasRect);
                }

                // 绘制每个插槽
                for (int i = 0; i < prepared.Slots.Count; i++)
                {
                    Rectangle slot = prepared.Slots[i];
                    Rectangle scaledSlot = new Rectangle(
                        offsetX + (int)(slot.X * scale),
                        offsetY + (int)(slot.Y * scale),
                        Math.Max(1, (int)(slot.Width * scale)),
                        Math.Max(1, (int)(slot.Height * scale))
                    );

                    // 粘贴至画布区域
                    Rectangle clipSlot = Rectangle.Intersect(canvasRect, scaledSlot);
                    if (clipSlot.Width <= 0 || clipSlot.Height <= 0)
                        continue;
                    
                    // 绘制缩略图或占位
                    if (imageFiles != null && i < imageFiles.Count)
                    {
                        // 有图像 - 尝试绘制缩略图
                        Image thumb = GetThumbnail(imageFiles[i], clipSlot.Width, clipSlot.Height);
                        if (thumb != null)
                        {
                            // 缩略图居中并且适配显示
                            RectangleF thumbRect = CalcContainRectF(new SizeF(clipSlot.Width, clipSlot.Height), new SizeF(thumb.Width, thumb.Height));
                            thumbRect.X += clipSlot.X;
                            thumbRect.Y += clipSlot.Y;
                            g.DrawImage(thumb, thumbRect);
                        }
                        else
                        {
                            // 备用彩色背景
                            using (Brush br = new SolidBrush(Color.FromArgb(180, 100, 150)))
                            {
                                g.FillRectangle(br, clipSlot);
                            }
                        }
                    }
                    else
                    {
                        // 灰色虚线边框
                        using (Pen dashPen = new Pen(Color.FromArgb(140, 145, 160), 1) { DashStyle = DashStyle.Dash })
                        {
                            g.DrawRectangle(dashPen, clipSlot);
                        }
                        using (Brush br = new SolidBrush(Color.FromArgb(80, 85, 100)))
                        {
                            StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                            g.DrawString("空位", new Font("微软雅黑", Math.Max(7, clipSlot.Width / 8)), br, clipSlot, sf);
                        }
                    }

                    // 绘制插槽边框
                    using (Pen borderPen = new Pen(Color.FromArgb(80, 90, 110), 1))
                    {
                        g.DrawRectangle(borderPen, clipSlot);
                    }

                    // 固定格编号
                    string numText = (i + 1).ToString();
                    int numSize = Math.Max(9, Math.Min(14, clipSlot.Width / 4));
                    using (Font numFont = new Font("微软雅黑", numSize, FontStyle.Bold))
                    using (Brush numBrush = new SolidBrush(Color.FromArgb(60, 70, 90)))
                    {
                        PointF numPos = new PointF(clipSlot.X + 2, clipSlot.Y + 1);
                        g.DrawString(numText, numFont, numBrush, numPos);
                    }
                }

                // 交换图片
                if (picPreview.Image != null)
                {
                    var old = picPreview.Image;
                    picPreview.Image = null;
                    old.Dispose();
                }
                picPreview.Image = new Bitmap(bmp);
            }
        }

        private RectangleF CalcContainRectF(SizeF container, SizeF source)
        {
            if (source.Width <= 0 || source.Height <= 0)
                return new RectangleF(0, 0, container.Width, container.Height);

            float scale = Math.Min(container.Width / source.Width, container.Height / source.Height);
            float w = source.Width * scale;
            float h = source.Height * scale;
            float x = (container.Width - w) / 2;
            float y = (container.Height - h) / 2;
            return new RectangleF(x, y, w, h);
        }

        private Image GetThumbnail(string imagePath, int maxWidth, int maxHeight)
        {
            if (string.IsNullOrEmpty(imagePath))
                return null;

            string cacheKey = $"{imagePath}|{maxWidth}x{maxHeight}";
            if (thumbnailCache.TryGetValue(cacheKey, out Image cached))
                return cached;

            try
            {
                // 生成预览图
                using (Bitmap preview = AsposePSDHelper.GeneratePreview(imagePath))
                {
                    if (preview == null)
                        return null;

                    // Scale to fit
                    float scale = Math.Min((float)maxWidth / preview.Width, (float)maxHeight / preview.Height);
                    int thumbW = Math.Max(1, (int)(preview.Width * scale));
                    int thumbH = Math.Max(1, (int)(preview.Height * scale));

                    using (Bitmap thumb = new Bitmap(thumbW, thumbH))
                    using (Graphics g = Graphics.FromImage(thumb))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(preview, 0, 0, thumbW, thumbH);

                        Image result = new Bitmap(thumb);
                        thumbnailCache[cacheKey] = result;
                        return result;
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private async void BtnRun_Click(object sender, EventArgs e)
        {
            if (isRunning)
                return;

            try
            {
                ResultPath = null;
                LayoutOutputRequest request = BuildRequest();
                if (request == null)
                    return;

                SetBusyState(true);
                LayoutOutputResult result = await Task.Run(() => LayoutOutputHelper.Execute(request, UpdateStatusSafe));
                ResultPath = result.OutputPath;
                lblStatus.Text = $"排版完成，已输出 {Path.GetFileName(ResultPath)}";
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                lblStatus.Text = "排版失败";
                MessageBox.Show($"排版输出失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusyState(false);
            }
        }

        private LayoutOutputRequest BuildRequest()
        {
            if (string.IsNullOrWhiteSpace(txtSourceFolder.Text) || !Directory.Exists(txtSourceFolder.Text))
            {
                ShowWarning("请选择有效的套图结果源目录");
                return null;
            }

            if (string.IsNullOrWhiteSpace(txtOutputFolder.Text) || !Directory.Exists(txtOutputFolder.Text))
            {
                ShowWarning("请选择有效的大图输出目录");
                return null;
            }

            decimal sheetWidth, sheetHeight, dpi, startX, startY, slotWidth, slotHeight, hGap, vGap;
            int rows, columns;

            if (!TryReadDecimal(txtSheetWidth, out sheetWidth, "请输入有效的大图宽度(mm)")) return null;
            if (!TryReadDecimal(txtSheetHeight, out sheetHeight, "请输入有效的大图高度(mm)")) return null;
            if (!TryReadDecimal(txtDpi, out dpi, "请输入有效的 DPI")) return null;
            if (!TryReadDecimal(txtStartX, out startX, "请输入有效的首格 X 坐标(mm)")) return null;
            if (!TryReadDecimal(txtStartY, out startY, "请输入有效的首格 Y 坐标(mm)")) return null;
            if (!TryReadDecimal(txtSlotWidth, out slotWidth, "请输入有效的格位宽度(mm)")) return null;
            if (!TryReadDecimal(txtSlotHeight, out slotHeight, "请输入有效的格位高度(mm)")) return null;
            if (!TryReadDecimal(txtHorizontalGap, out hGap, "请输入有效的横向间距(mm)")) return null;
            if (!TryReadDecimal(txtVerticalGap, out vGap, "请输入有效的纵向间距(mm)")) return null;
            if (!TryReadInt(txtRows, out rows, "请输入有效的行数")) return null;
            if (!TryReadInt(txtColumns, out columns, "请输入有效的列数")) return null;

            return new LayoutOutputRequest
            {
                SourceFolder = txtSourceFolder.Text.Trim(),
                OutputFolder = txtOutputFolder.Text.Trim(),
                OutputFileName = txtOutputFileName.Text.Trim(),
                Settings = new SheetLayoutSettings
                {
                    SheetWidthMm = sheetWidth,
                    SheetHeightMm = sheetHeight,
                    Dpi = dpi,
                    StartXmm = startX,
                    StartYmm = startY,
                    SlotWidthMm = slotWidth,
                    SlotHeightMm = slotHeight,
                    HorizontalGapMm = hGap,
                    VerticalGapMm = vGap,
                    Rows = rows,
                    Columns = columns
                }
            };
        }

        private void BrowseFolder(TextBox textBox, bool saveOutputPath)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择文件夹";
                if (!string.IsNullOrWhiteSpace(textBox.Text) && Directory.Exists(textBox.Text))
                    dialog.SelectedPath = textBox.Text;

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    textBox.Text = dialog.SelectedPath;
                    if (saveOutputPath)
                    {
                        SaveOutputPath(dialog.SelectedPath);
                    }
                    // Auto refresh preview when folder changes
                    // RefreshPreview();
                }
            }
        }

        private void LoadSavedPaths()
        {
            try
            {
                var settings = Properties.Settings.Default;
                if (!string.IsNullOrEmpty(settings.SavePath) && Directory.Exists(settings.SavePath))
                {
                    txtSourceFolder.Text = settings.SavePath;
                    txtOutputFolder.Text = settings.SavePath;
                }
            }
            catch
            {
            }
        }

        private void SaveOutputPath(string path)
        {
            try
            {
                var settings = Properties.Settings.Default;
                settings.SavePath = path;
                settings.Save();
            }
            catch
            {
            }
        }

        private void SetBusyState(bool busy)
        {
            isRunning = busy;
            txtSourceFolder.Enabled = !busy;
            txtOutputFolder.Enabled = !busy;
            txtOutputFileName.Enabled = !busy;
            txtSheetWidth.Enabled = !busy;
            txtSheetHeight.Enabled = !busy;
            txtDpi.Enabled = !busy;
            txtStartX.Enabled = !busy;
            txtStartY.Enabled = !busy;
            txtSlotWidth.Enabled = !busy;
            txtSlotHeight.Enabled = !busy;
            txtHorizontalGap.Enabled = !busy;
            txtVerticalGap.Enabled = !busy;
            txtRows.Enabled = !busy;
            txtColumns.Enabled = !busy;
            btnBrowseSource.Enabled = !busy;
            btnBrowseOutput.Enabled = !busy;
            btnRefreshPreview.Enabled = !busy;
            btnRun.Enabled = !busy;
            btnClose.Enabled = !busy;
            btnRun.Text = busy ? "处理中..." : "开始排版输出";
        }

        private void BtnClose_Click(object sender, EventArgs e)
        {
            if (isRunning)
            {
                MessageBox.Show("当前任务正在执行，请等待完成后再关闭。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Close();
        }

        private void UpdateStatusSafe(string message)
        {
            if (IsDisposed || Disposing)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(UpdateStatusSafe), message);
                return;
            }

            lblStatus.Text = message;
        }

        private bool TryReadDecimal(TextBox textBox, out decimal value, string errorMessage)
        {
            string text = (textBox.Text ?? string.Empty).Trim();
            if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value) ||
                decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            ShowWarning(errorMessage);
            return false;
        }

        private bool TryReadInt(TextBox textBox, out int value, string errorMessage)
        {
            if (int.TryParse((textBox.Text ?? string.Empty).Trim(), out value))
                return true;

            ShowWarning(errorMessage);
            return false;
        }

        private void ShowWarning(string message)
        {
            lblStatus.Text = message;
            MessageBox.Show(message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            ClearThumbnailCache();
            base.OnFormClosed(e);
        }
    }
}
