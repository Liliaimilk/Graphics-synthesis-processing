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
            if (string.IsNullOrWhiteSpace(request.SourceFolder) || !Directory.Exists(request.SourceFolder))
                throw new DirectoryNotFoundException("套图结果源目录无效");
            if (string.IsNullOrWhiteSpace(request.OutputFolder) || !Directory.Exists(request.OutputFolder))
                throw new DirectoryNotFoundException("大图输出目录无效");

            List<string> imageFiles = GetImageFiles(request.SourceFolder);
            if (imageFiles.Count == 0)
                throw new InvalidOperationException("源目录中未找到可用图片");

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
                            Rectangle targetRect = CalculateContainRect(source.Size, slot);
                            graphics.DrawImage(source, targetRect);
                        }
                    }
                }

                progressCallback?.Invoke("正在导出 TIF...");
                AsposePSDHelper.SaveBitmapAsFlatTiff(canvas, outputPath);
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
