using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace WindowsFormsApp1
{
    public partial class Form1
    {
        /// <summary>
        /// 打开套图对话框，并将成功结果回载到画布。
        /// </summary>
        private void HandleMergeToolClick()
        {
            using (MergeDialog dialog = new MergeDialog())
            {
                dialog.Owner = this;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    LoadDialogResultsToCanvasCore(dialog);
                }
            }
        }

        /// <summary>
        /// 打开排版输出对话框，并将导出图片载入画布。
        /// </summary>
        private void HandleLayoutOutputToolClick()
        {
            using (LayoutOutputDialog dialog = new LayoutOutputDialog())
            {
                dialog.Owner = this;
                if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dialog.ResultPath))
                {
                    LoadResultToCanvasCore(dialog.ResultPath);
                }
            }
        }

        /// <summary>
        /// 将对话框返回的结果集统一载入画布。
        /// </summary>
        private void LoadDialogResultsToCanvasCore(MergeDialog dialog)
        {
            if (dialog == null)
            {
                return;
            }

            if (dialog.ResultPaths != null && dialog.ResultPaths.Count > 1)
            {
                LoadResultsToCanvasCore(dialog.ResultPaths);
                return;
            }

            if (!string.IsNullOrEmpty(dialog.ResultPath))
            {
                LoadResultToCanvasCore(dialog.ResultPath);
            }
        }

        /// <summary>
        /// 将单张结果图片载入画布。
        /// </summary>
        private void LoadResultToCanvasCore(string path)
        {
            LoadResultsToCanvasCore(new[] { path });
        }

        /// <summary>
        /// 将一个或多个有效图片文件载入画布，并同步更新状态栏。
        /// </summary>
        private void LoadResultsToCanvasCore(IEnumerable<string> paths)
        {
            try
            {
                List<string> validPaths = (paths ?? Enumerable.Empty<string>())
                    .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    .ToList();

                if (validPaths.Count == 0)
                {
                    lblStatus.Text = "没有可加载的图片";
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

        /// <summary>
        /// 将工具栏缩放值应用到画布视口。
        /// </summary>
        private void ApplyZoomTrackBarValue()
        {
            if (canvas == null)
            {
                return;
            }

            float newZoom = zoomTrackBar.Value / 100f;
            canvas.SetZoom(newZoom);
            zoomLabel.Text = $"缩放: {zoomTrackBar.Value}%";
        }

        /// <summary>
        /// 更新当前画布工具，并刷新对应按钮的选中状态。
        /// </summary>
        private void ApplyCanvasToolState(CanvasTool tool)
        {
            currentTool = tool;

            if (canvas != null)
            {
                canvas.ActiveTool = tool;
            }

            if (btnMoveTool == null)
            {
                return;
            }

            bool isActive = tool == CanvasTool.Move;
            btnMoveTool.BackColor = isActive ? System.Drawing.Color.FromArgb(20, 20, 20) : System.Drawing.Color.FromArgb(90, 90, 90);
            btnMoveTool.FlatAppearance.BorderColor = isActive ? System.Drawing.Color.FromArgb(20, 20, 20) : System.Drawing.Color.FromArgb(90, 90, 90);
            btnMoveTool.FlatAppearance.MouseOverBackColor = isActive ? System.Drawing.Color.FromArgb(32, 32, 32) : System.Drawing.Color.FromArgb(110, 110, 110);
        }
    }
}
