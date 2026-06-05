using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using BitMiracle.LibTiff;
using BitMiracle.LibTiff.Classic;

namespace WindowsFormsApp1
{
    public partial class Form1 : Form
    {
        private TabControl tabControl;
        private TabPage tabTemplateMerge;
        private TabPage tabLayout;

        private Label lblTemplateFolder;
        private TextBox txtTemplateFolder;
        private Button btnBrowseTemplateFolder;

        private Label lblMaterialFolder;
        private TextBox txtMaterialFolder;
        private Button btnBrowseMaterialFolder;

        private Label lblSavePath;
        private TextBox txtSavePath;
        private Button btnBrowseSavePath;

        private Label lblSeparator;
        private TextBox txtSeparator;
        private ComboBox cmbFormat;
        private ComboBox cmbRotation;
        private ComboBox cmbMirror;

        private CheckBox chkWhiteInk;
        private CheckBox chkVarnish;
        private TextBox txtWhiteInkName;
        private TextBox txtVarnishName;

        private CheckBox chkUseAspose;
        private CheckBox chkTifMode;

        private Button btnMerge;
        private PictureBox picResultPreview;
        private Label lblPreview;
        private Label lblStatus;

        private Label lblLayoutInfo;

        public Form1()
        {
            SetupDarkTheme();
            SetupTabControl();
            SetupTemplateMergeTab();
            SetupLayoutTab();
            LoadSavedPaths();
            //previewImage();
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

        private void SavePath(TextBox textBox, string settingName)
        {
            try
            {
                var settings = Properties.Settings.Default;
                switch (settingName)
                {
                    case "TemplateFolder":
                        settings.TemplateFolder = textBox.Text;
                        break;
                    case "MaterialFolder":
                        settings.MaterialFolder = textBox.Text;
                        break;
                    case "SavePath":
                        settings.SavePath = textBox.Text;
                        break;
                }
                settings.Save();
            }
            catch { }
        }

        private void SetupDarkTheme()
        {
            this.Text = "图片处理工具";
            this.Size = new Size(1100, 850);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(25, 35, 55);
            this.ForeColor = Color.FromArgb(220, 225, 235);

            typeof(Control).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, this, new object[] { true });
        }

        private void SetupTabControl()
        {
            tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                Location = new Point(0, 0),
                Size = new Size(1100, 850),
                BackColor = Color.FromArgb(30, 40, 60),
                ForeColor = Color.FromArgb(220, 225, 235),
                ItemSize = new Size(120, 40),
                DrawMode = TabDrawMode.OwnerDrawFixed
            };

            tabControl.DrawItem += TabControl_DrawItem;

            tabTemplateMerge = new TabPage("套图");
            tabTemplateMerge.BackColor = Color.FromArgb(25, 35, 55);
            tabTemplateMerge.ForeColor = Color.FromArgb(220, 225, 235);

            tabLayout = new TabPage("排版");
            tabLayout.BackColor = Color.FromArgb(25, 35, 55);
            tabLayout.ForeColor = Color.FromArgb(220, 225, 235);

            tabControl.TabPages.Add(tabTemplateMerge);
            tabControl.TabPages.Add(tabLayout);

            this.Controls.Add(tabControl);
        }

        private void TabControl_DrawItem(object sender, DrawItemEventArgs e)
        {
            Graphics g = e.Graphics;
            TabControl tc = (TabControl)sender;
            TabPage tp = tc.TabPages[e.Index];
            Rectangle bounds = tc.GetTabRect(e.Index);

            using (SolidBrush bgBrush = new SolidBrush(
                tc.SelectedIndex == e.Index ? Color.FromArgb(45, 65, 95) : Color.FromArgb(30, 40, 60)))
            {
                g.FillRectangle(bgBrush, bounds);
            }
            
            using (SolidBrush textBrush = new SolidBrush(
                tc.SelectedIndex == e.Index ? Color.FromArgb(100, 180, 255) : Color.FromArgb(180, 185, 195)))
            {
                StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(tp.Text, new Font("微软雅黑", 12F), textBrush, bounds, sf);
            }
        }

        private void SetupTemplateMergeTab()
        {
            int startX = 30;
            int startY = 30;
            int rowHeight = 45;
            int labelWidth = 100;
            int textBoxWidth = 500;
            int btnWidth = 80;

            lblTemplateFolder = CreateLabel("模版文件夹:", startX, startY, labelWidth);
            txtTemplateFolder = CreateTextBox(startX + labelWidth + 10, startY - 3, textBoxWidth);
            btnBrowseTemplateFolder = CreateButton("浏览...", startX + labelWidth + textBoxWidth + 20, startY - 5, btnWidth);
            btnBrowseTemplateFolder.Click += (s, e) => BrowseFolder(txtTemplateFolder);

            startY += rowHeight + 10;
            lblMaterialFolder = CreateLabel("素材文件夹:", startX, startY, labelWidth);
            txtMaterialFolder = CreateTextBox(startX + labelWidth + 10, startY - 3, textBoxWidth);
            btnBrowseMaterialFolder = CreateButton("浏览...", startX + labelWidth + textBoxWidth + 20, startY - 5, btnWidth);
            btnBrowseMaterialFolder.Click += (s, e) => BrowseFolder(txtMaterialFolder);

            startY += rowHeight + 10;
            lblSavePath = CreateLabel("保存路径:", startX, startY, labelWidth);
            txtSavePath = CreateTextBox(startX + labelWidth + 10, startY - 3, textBoxWidth);
            btnBrowseSavePath = CreateButton("浏览...", startX + labelWidth + textBoxWidth + 20, startY - 5, btnWidth);
            btnBrowseSavePath.Click += (s, e) => BrowseFolder(txtSavePath);

            startY += rowHeight + 10;
            lblSeparator = CreateLabel("文件名分隔符:", startX, startY, labelWidth);
            txtSeparator = CreateTextBox(startX + labelWidth + 10, startY - 3, 100);
            txtSeparator.Text = "-";

            cmbFormat = CreateComboBox(startX + labelWidth + 120, startY - 3, 100);
            cmbFormat.Items.AddRange(new object[] { "TIF", "PSD", "JPEG", "PNG" });
            cmbFormat.SelectedIndex = 0;

            cmbRotation = CreateComboBox(startX + labelWidth + 240, startY - 3, 80);
            cmbRotation.Items.AddRange(new object[] { "0°", "90°", "180°", "270°" });
            cmbRotation.SelectedIndex = 0;

            cmbMirror = CreateComboBox(startX + labelWidth + 340, startY - 3, 100);
            cmbMirror.Items.AddRange(new object[] { "无", "水平镜像", "垂直镜像" });
            cmbMirror.SelectedIndex = 0;

            startY += rowHeight + 20;

            Label lblSpotTitle = CreateLabel("专色通道设置", startX, startY, 120);
            lblSpotTitle.Font = new Font("微软雅黑", 10F, FontStyle.Bold);

            startY += rowHeight - 5;

            chkWhiteInk = new CheckBox
            {
                Text = "白墨通道",
                Location = new Point(startX + labelWidth + 10, startY - 2),
                Size = new Size(100, 25),
                ForeColor = Color.FromArgb(200, 205, 215),
                BackColor = Color.Transparent,
                Checked = true
            };

            txtWhiteInkName = CreateTextBox(startX + labelWidth + 110, startY - 3, 150);
            txtWhiteInkName.Text = "White";

            chkVarnish = new CheckBox
            {
                Text = "光油通道",
                Location = new Point(startX + labelWidth + 270, startY - 2),
                Size = new Size(100, 25),
                ForeColor = Color.FromArgb(200, 205, 215),
                BackColor = Color.Transparent,
                Checked = true
            };

            txtVarnishName = CreateTextBox(startX + labelWidth + 370, startY - 3, 150);
            txtVarnishName.Text = "Varnish";

            startY += rowHeight + 15;

            chkUseAspose = new CheckBox
            {
                Text = "使用 Aspose.PSD（图层模式）",
                Location = new Point(startX, startY),
                Size = new Size(220, 25),
                ForeColor = Color.FromArgb(200, 205, 215),
                BackColor = Color.Transparent,
                Checked = true
            };
            chkUseAspose.CheckedChanged += (s, e) => { if (chkUseAspose.Checked) chkTifMode.Checked = false; };

            startY += rowHeight - 5;

            chkTifMode = new CheckBox
            {
                Text = "tif模式",
                Location = new Point(startX, startY),
                Size = new Size(100, 25),
                ForeColor = Color.FromArgb(200, 205, 215),
                BackColor = Color.Transparent,
                Checked = false
            };
            chkTifMode.CheckedChanged += (s, e) => { if (chkTifMode.Checked) chkUseAspose.Checked = false; };

            startY += rowHeight + 10;
            btnMerge = new Button
            {
                Text = "开始套图",
                Location = new Point(startX, startY),
                Size = new Size(200, 45),
                Font = new Font("微软雅黑", 12F),
                BackColor = Color.FromArgb(45, 100, 160),
                ForeColor = Color.FromArgb(220, 225, 235),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnMerge.FlatAppearance.BorderColor = Color.FromArgb(70, 140, 200);
            btnMerge.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 120, 180);
            btnMerge.Click += BtnMerge_Click;

            lblStatus = new Label
            {
                Text = "就绪",
                Location = new Point(startX + 220, startY + 10),
                Size = new Size(400, 25),
                Font = new Font("微软雅黑", 10F),
                ForeColor = Color.FromArgb(150, 155, 165)
            };

            startY += rowHeight + 30;
            lblPreview = CreateLabel("结果预览:", startX, startY, 100);

            picResultPreview = new PictureBox
            {
                Location = new Point(startX, startY + 30),
                Size = new Size(500, 350),
                BackColor = Color.FromArgb(20, 28, 45),
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle
            };

            tabTemplateMerge.Controls.Add(lblTemplateFolder);
            tabTemplateMerge.Controls.Add(txtTemplateFolder);
            tabTemplateMerge.Controls.Add(btnBrowseTemplateFolder);
            tabTemplateMerge.Controls.Add(lblMaterialFolder);
            tabTemplateMerge.Controls.Add(txtMaterialFolder);
            tabTemplateMerge.Controls.Add(btnBrowseMaterialFolder);
            tabTemplateMerge.Controls.Add(lblSavePath);
            tabTemplateMerge.Controls.Add(txtSavePath);
            tabTemplateMerge.Controls.Add(btnBrowseSavePath);
            tabTemplateMerge.Controls.Add(lblSeparator);
            tabTemplateMerge.Controls.Add(txtSeparator);
            tabTemplateMerge.Controls.Add(cmbFormat);
            tabTemplateMerge.Controls.Add(cmbRotation);
            tabTemplateMerge.Controls.Add(cmbMirror);
            tabTemplateMerge.Controls.Add(lblSpotTitle);
            tabTemplateMerge.Controls.Add(chkWhiteInk);
            tabTemplateMerge.Controls.Add(txtWhiteInkName);
            tabTemplateMerge.Controls.Add(chkVarnish);
            tabTemplateMerge.Controls.Add(txtVarnishName);
            tabTemplateMerge.Controls.Add(chkUseAspose);
            tabTemplateMerge.Controls.Add(chkTifMode);
            tabTemplateMerge.Controls.Add(btnMerge);
            tabTemplateMerge.Controls.Add(lblStatus);
            tabTemplateMerge.Controls.Add(lblPreview);
            tabTemplateMerge.Controls.Add(picResultPreview);
        }

        private void SetupLayoutTab()
        {
            lblLayoutInfo = new Label
            {
                Text = "排版功能开发中...",
                Location = new Point(400, 350),
                AutoSize = true,
                Font = new Font("微软雅黑", 16F),
                ForeColor = Color.FromArgb(150, 155, 165)
            };
            tabLayout.Controls.Add(lblLayoutInfo);
        }

        private Label CreateLabel(string text, int x, int y, int width)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 25),
                Font = new Font("微软雅黑", 10F),
                ForeColor = Color.FromArgb(200, 205, 215),
                BackColor = Color.Transparent
            };
        }

        private TextBox CreateTextBox(int x, int y, int width)
        {
            return new TextBox
            {
                Location = new Point(x, y),
                Size = new Size(width, 30),
                Font = new Font("微软雅黑", 10F),
                BackColor = Color.FromArgb(35, 45, 65),
                ForeColor = Color.FromArgb(220, 225, 235),
                BorderStyle = BorderStyle.FixedSingle
            };
        }

        private ComboBox CreateComboBox(int x, int y, int width)
        {
            return new ComboBox
            {
                Location = new Point(x, y),
                Size = new Size(width, 30),
                Font = new Font("微软雅黑", 10F),
                BackColor = Color.FromArgb(35, 45, 65),
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
                Size = new Size(width, 30),
                Font = new Font("微软雅黑", 9F),
                BackColor = Color.FromArgb(40, 55, 80),
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
                    // 保存路径
                    string settingName = textBox == txtTemplateFolder ? "TemplateFolder" :
                                         textBox == txtMaterialFolder ? "MaterialFolder" :
                                         textBox == txtSavePath ? "SavePath" : "";
                    if (!string.IsNullOrEmpty(settingName))
                        SavePath(textBox, settingName);
                }
            }
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


        // 预览tif
        private void previewImage()
        {
            using(var preview = AsposePSDHelper.GeneratePreview("D:\\matrials\\2-picture\\粉色海浪玫瑰.tif"))
            {
                if (preview != null)
                {
                    DisplayPreview(preview);
                    preview.Dispose();
                }
            }
           
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
                string ext = format.ToLower() == "jpeg" || format.ToLower() == "jpg" ? ".jpg" :
                             format.ToLower() == "png" ? ".png" :
                             format.ToLower() == "psd" ? ".psd" : ".tif";

                string baseName = GetBaseName(templateFile) + separator + GetBaseName(materialFile);
                string outputFile = NextOutputFile(txtSavePath.Text, baseName, ext);

                //PSDAnalyzer.AnalyzePSD(materialFile);

                Console.WriteLine($"模版路径名称：{templateFile}");
                Console.WriteLine($"素材路径名称：{materialFile}");

                if (chkTifMode.Checked)
                {
                    AsposePSDHelper.ProcessTifMode(
                        templateFile,
                        materialFile,
                        outputFile,
                        format,
                        msg => { lblStatus.Text = msg; Application.DoEvents(); });

                    lblStatus.Text = "正在生成预览...";
                    Application.DoEvents();
                    Console.WriteLine($"输出路径名称：{outputFile}");

                    lblStatus.Text = "完成！";
                    MessageBox.Show($"TIF模式套图完成！\n保存路径: {outputFile}", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else if (chkUseAspose.Checked)
                {
                    PSDAnalyzer.AnalyzeAndMatchLayer(templateFile, materialFile, outputFile,
                        chkWhiteInk.Checked, chkVarnish.Checked,
                        chkWhiteInk.Checked ? txtWhiteInkName.Text : null,
                        chkVarnish.Checked ? txtVarnishName.Text : null);

                    lblStatus.Text = "正在生成预览...";
                    Application.DoEvents();
                    Console.WriteLine($"输出路径名称：{outputFile}");

                    lblStatus.Text = "完成！";
                    MessageBox.Show($"套图完成！\n保存路径: {outputFile}", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("请选择一种处理模式", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
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

        private void DisplayPreview(Bitmap preview)
        {
            Bitmap scaled = new Bitmap(picResultPreview.Width, picResultPreview.Height);
            using (Graphics g = Graphics.FromImage(scaled))
            {
                g.Clear(Color.FromArgb(20, 28, 45));

                int imgW = preview.Width;
                int imgH = preview.Height;
                float ratio = Math.Min((float)picResultPreview.Width / imgW, (float)picResultPreview.Height / imgH);
                int newW = (int)(imgW * ratio);
                int newH = (int)(imgH * ratio);
                int px = (picResultPreview.Width - newW) / 2;
                int py = (picResultPreview.Height - newH) / 2;

                g.DrawImage(preview, px, py, newW, newH);
            }
            picResultPreview.Image = scaled;
        }

      

       
            
        
    }
}