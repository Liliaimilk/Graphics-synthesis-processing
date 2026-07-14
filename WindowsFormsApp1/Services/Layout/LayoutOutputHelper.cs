using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using Bitmap = System.Drawing.Bitmap;
using Graphics = System.Drawing.Graphics;
using Rectangle = System.Drawing.Rectangle;

namespace WindowsFormsApp1
{
    public sealed class SheetLayoutSettings
    {
        public decimal SheetWidthMm { get; set; }
        public decimal SheetHeightMm { get; set; }
        public decimal Dpi { get; set; }
        public decimal StartXmm { get; set; }
        public decimal StartYmm { get; set; }
        public decimal SlotWidthMm { get; set; }
        public decimal SlotHeightMm { get; set; }
        public decimal HorizontalGapMm { get; set; }
        public decimal VerticalGapMm { get; set; }
        public int Rows { get; set; }
        public int Columns { get; set; }
    }

    public sealed class LayoutOutputRequest
    {
        public string SourceFolder { get; set; }
        public string OutputFolder { get; set; }
        public string OutputFileName { get; set; }
        public SheetLayoutSettings Settings { get; set; }
        public IReadOnlyList<string> ManualImageFiles { get; set; }
        public bool DrawSlotBounds { get; set; }
    }

    public sealed class LayoutOutputResult
    {
        public string OutputPath { get; set; }
        public int PlacedImageCount { get; set; }
        public Size CanvasSize { get; set; }
        public IReadOnlyList<Rectangle> Slots { get; set; }
    }

    public static class LayoutOutputHelper
    {
        public sealed class PreparedLayout
        {
            public int CanvasWidthPx { get; set; }
            public int CanvasHeightPx { get; set; }
            public int Dpi { get; set; }
            public int Rows { get; set; }
            public int Columns { get; set; }
            public int Capacity { get; set; }
            public List<Rectangle> Slots { get; set; } = new List<Rectangle>();
        }

        private const string WhiteInkChannelName = "通道1";
        private const string VarnishChannelName = "通道2";

        private const int MaxBitmapDimensionPx = 65000;
        private const long MaxCanvasBytes = 1024L * 1024L * 1024L;

        public static PreparedLayout PrepareLayout(SheetLayoutSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            if (settings.SheetWidthMm <= 0)
                throw new ArgumentException("大图宽度必须大于 0", nameof(settings));
            if (settings.SheetHeightMm <= 0)
                throw new ArgumentException("大图高度必须大于 0", nameof(settings));
            if (settings.Dpi <= 0)
                throw new ArgumentException("DPI 必须大于 0", nameof(settings));
            if (settings.StartXmm < 0 || settings.StartYmm < 0)
                throw new ArgumentException("首格坐标不能小于 0", nameof(settings));
            if (settings.SlotWidthMm <= 0 || settings.SlotHeightMm <= 0)
                throw new ArgumentException("格位尺寸必须大于 0", nameof(settings));
            if (settings.HorizontalGapMm < 0 || settings.VerticalGapMm < 0)
                throw new ArgumentException("格位间距不能小于 0", nameof(settings));
            if (settings.Rows <= 0 || settings.Columns <= 0)
                throw new ArgumentException("行数和列数必须大于 0", nameof(settings));

            int dpi = Math.Max(1, (int)Math.Round(settings.Dpi, MidpointRounding.AwayFromZero));
            int canvasWidth = MmToPixels(settings.SheetWidthMm, settings.Dpi);
            int canvasHeight = MmToPixels(settings.SheetHeightMm, settings.Dpi);
            int startX = MmToPixels(settings.StartXmm, settings.Dpi);
            int startY = MmToPixels(settings.StartYmm, settings.Dpi);
            int slotWidth = MmToPixels(settings.SlotWidthMm, settings.Dpi);
            int slotHeight = MmToPixels(settings.SlotHeightMm, settings.Dpi);
            int gapX = MmToPixels(settings.HorizontalGapMm, settings.Dpi);
            int gapY = MmToPixels(settings.VerticalGapMm, settings.Dpi);

            ValidateCanvasCapacity(canvasWidth, canvasHeight);

            int lastRight = startX + settings.Columns * slotWidth + Math.Max(0, settings.Columns - 1) * gapX;
            int lastBottom = startY + settings.Rows * slotHeight + Math.Max(0, settings.Rows - 1) * gapY;

            if (lastRight > canvasWidth || lastBottom > canvasHeight)
                throw new ArgumentException("当前版式超出大图边界，请调整首格坐标、格位尺寸、间距或行列数", nameof(settings));

            var result = new PreparedLayout
            {
                CanvasWidthPx = canvasWidth,
                CanvasHeightPx = canvasHeight,
                Dpi = Math.Max(1, dpi),
                Rows = settings.Rows,
                Columns = settings.Columns,
                Capacity = settings.Rows * settings.Columns
            };

            for (int row = 0; row < settings.Rows; row++)
            {
                for (int column = 0; column < settings.Columns; column++)
                {
                    int left = startX + column * (slotWidth + gapX);
                    int top = startY + row * (slotHeight + gapY);
                    result.Slots.Add(new Rectangle(left, top, slotWidth, slotHeight));
                }
            }

            return result;
        }

        private static void ValidateCanvasCapacity(int canvasWidth, int canvasHeight)
        {
            long pixelCount = (long)canvasWidth * canvasHeight;
            long rawBytes = pixelCount * 4L;
            double rawGb = rawBytes / 1024d / 1024d / 1024d;

            if (canvasWidth > MaxBitmapDimensionPx || canvasHeight > MaxBitmapDimensionPx)
            {
                throw new InvalidOperationException(
                    $"当前排版尺寸过大：{canvasWidth}×{canvasHeight}px，单边像素超过 {MaxBitmapDimensionPx}px。" +
                    " 当前 System.Drawing 单画布流程无法稳定输出这种超长图，请降低 DPI、拆分高度，或改成分块 TIFF 输出。");
            }

            if (rawBytes > MaxCanvasBytes)
            {
                throw new InvalidOperationException(
                    $"当前排版尺寸过大：{canvasWidth}×{canvasHeight}px，32 位画布裸内存约 {rawGb:F2}GB。" +
                    " 后续 TIFF 转换和专色通道还会继续增加内存占用，请降低 DPI、拆分版面，或改成分块 TIFF 输出。");
            }
        }

        public static List<string> GetImageFiles(string folderPath)
        {
            string[] imageExtensions = { ".psd", ".psb", ".tif", ".tiff", ".jpg", ".jpeg", ".png", ".bmp" };
            if (!Directory.Exists(folderPath))
                return new List<string>();

            return Directory.GetFiles(folderPath)
                .Where(f => imageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f)
                .ToList();
        }

        public static string NextOutputFile(string saveFolder, string baseName, string ext)
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

        public static string GetOutputExtension(string format)
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

        public static void ValidateSourceImages(IEnumerable<string> imagePaths)
        {
            foreach (string imagePath in imagePaths ?? Enumerable.Empty<string>())
            {
                using (var preview = AsposePSDHelper.GeneratePreview(imagePath))
                {
                    if (preview == null)
                        throw new InvalidOperationException($"图片无法读取或格式不兼容: {Path.GetFileName(imagePath)}");
                }
            }
        }

        public static LayoutOutputResult Execute(LayoutOutputRequest request, Action<string> progressCallback = null)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (request.Settings == null)
                throw new ArgumentNullException(nameof(request.Settings));

            List<string> imageFiles = request.ManualImageFiles != null && request.ManualImageFiles.Count > 0
                ? request.ManualImageFiles.ToList()
                : GetImageFiles(request.SourceFolder);
            if (imageFiles.Count == 0)
                throw new InvalidOperationException("源目录中未找到可用图片");

            if (string.IsNullOrWhiteSpace(request.OutputFolder) || !Directory.Exists(request.OutputFolder))
                throw new DirectoryNotFoundException("大图输出目录无效");

            PreparedLayout prepared = PrepareLayout(request.Settings);
            if (imageFiles.Count > prepared.Capacity) 
                throw new InvalidOperationException($"当前版式容量不足，最多可放 {prepared.Capacity} 张，实际找到 {imageFiles.Count} 张");

            progressCallback?.Invoke("正在预检图片...");
            ValidateSourceImages(imageFiles);

            string outputFileName = SanitizeOutputFileName(request.OutputFileName);
            string outputPath = NextOutputFile(request.OutputFolder, outputFileName, ".tif");

            progressCallback?.Invoke("正在创建大图画布...");
            using (var canvas = new Bitmap(prepared.CanvasWidthPx, prepared.CanvasHeightPx, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                canvas.SetResolution(prepared.Dpi, prepared.Dpi);
                using (Graphics graphics = Graphics.FromImage(canvas))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.CompositingMode = CompositingMode.SourceOver;
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.SmoothingMode = SmoothingMode.HighQuality;

                    for (int i = 0; i < imageFiles.Count; i++)
                    {
                        string imagePath = imageFiles[i];
                        Rectangle slot = prepared.Slots[i];
                        progressCallback?.Invoke($"正在排版 {i + 1}/{imageFiles.Count}: {Path.GetFileName(imagePath)}");

                        using (Bitmap source = AsposePSDHelper.LoadBitmapForLayout(imagePath))
                        {
                            // 先按等比缩放把素材完整塞进格位，再居中绘制。
                            Rectangle targetRect = CalculateContainRect(source.Size, slot);
                            graphics.DrawImage(source, targetRect);
                        }
                    }

                    // 如果勾选绘制边框则输出带格子的大图，便于校验排版坐标及实际打印尺寸。
                    if (request.DrawSlotBounds)
                    {
                        DrawSlotBounds(graphics, prepared.Slots, prepared.Dpi);
                    }
                }

                progressCallback?.Invoke("正在导出 TIF...");

                // 最终输出单张大图 TIFF，并顺便写入额外通道。
                AsposePSDHelper.SaveBitmapAsTiffWithSpotChannels(canvas, outputPath, new List<string> { WhiteInkChannelName, VarnishChannelName });
            }

            progressCallback?.Invoke("排版完成");
            return new LayoutOutputResult
            {
                OutputPath = outputPath,
                PlacedImageCount = imageFiles.Count,
                CanvasSize = new Size(prepared.CanvasWidthPx, prepared.CanvasHeightPx),
                Slots = prepared.Slots.AsReadOnly()
            };
        }

        /// <summary>
        /// 在成品图上绘制格位边框，便于校验排版坐标及实际打印尺寸。
        /// </summary>
        private static void DrawSlotBounds(Graphics graphics, IEnumerable<Rectangle> slots, int dpi)
        {
            float lineWidth = Math.Max(1f, dpi * 0.3f / 25.4f);
            using (var pen = new Pen(Color.Magenta, lineWidth))
            {
                foreach (Rectangle slot in slots)
                {
                    int width = Math.Max(1, slot.Width - 1);
                    int height = Math.Max(1, slot.Height - 1);
                    graphics.DrawRectangle(pen, slot.Left, slot.Top, width, height);
                }
            }
        }

        private static Rectangle CalculateContainRect(Size imageSize, Rectangle slot)
        {
            if (imageSize.Width <= 0 || imageSize.Height <= 0)
                return slot;

            float scale = Math.Min((float)slot.Width / imageSize.Width, (float)slot.Height / imageSize.Height);
            int targetWidth = Math.Max(1, (int)Math.Round(imageSize.Width * scale));
            int targetHeight = Math.Max(1, (int)Math.Round(imageSize.Height * scale));
            int left = slot.Left + (slot.Width - targetWidth) / 2;
            int top = slot.Top + (slot.Height - targetHeight) / 2;
            return new Rectangle(left, top, targetWidth, targetHeight);
        }
        // 映射毫米到像素计算
        private static int MmToPixels(decimal millimeters, decimal dpi)
        {
            decimal inches = millimeters / 25.4m;
            return Math.Max(1, (int)Math.Round(inches * dpi, MidpointRounding.AwayFromZero));
        }

        private static string SanitizeOutputFileName(string outputFileName)
        {
            string baseName = string.IsNullOrWhiteSpace(outputFileName) ? "layout-output" : outputFileName.Trim();
            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                baseName = baseName.Replace(invalidChar, '_');
            }

            if (baseName.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) || baseName.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase))
            {
                baseName = Path.GetFileNameWithoutExtension(baseName);
            }

            return string.IsNullOrWhiteSpace(baseName) ? "layout-output" : baseName;
        }
    }
}
