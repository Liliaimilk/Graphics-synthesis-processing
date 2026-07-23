using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using BitMiracle.LibTiff.Classic;
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
        public string SlotGuidePath { get; set; }
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
        private const int OutputStripHeightPx = 256;

        /// <summary>
        /// 将毫米版式参数换算为像素坐标，并生成全部格位的固定位置。
        /// 在此阶段统一验证画布、间距和格位是否超出大图边界。
        /// </summary>
        public static PreparedLayout PrepareLayout(SheetLayoutSettings settings, bool validateCanvasCapacity = true)
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

            // 预览只绘制缩略图，不会创建原始尺寸位图；实际输出仍必须执行内存保护。
            if (validateCanvasCapacity)
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

        /// <summary>
        /// 在创建位图前估算 32 位画布内存，提前阻止无法稳定输出的超大尺寸。
        /// </summary>
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

        /// <summary>
        /// 获取排版源目录中的受支持图像文件，并按文件名稳定排序。
        /// </summary>
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

        /// <summary>
        /// 生成不覆盖已有文件的输出路径，冲突时自动追加递增序号。
        /// </summary>
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

        /// <summary>
        /// 将界面格式名称规范化为对应的文件扩展名。
        /// </summary>
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

        /// <summary>
        /// 在创建大图前逐个验证源文件可读性，避免耗时排版后才因单张坏图失败。
        /// </summary>
        public static void ValidateSourceImages(IEnumerable<string> imagePaths)
        {
            foreach (string imagePath in imagePaths ?? Enumerable.Empty<string>())
            {
                using (var preview = AsposePSDHelper.GenerateLayoutPreview(imagePath))
                {
                    if (preview == null)
                        throw new InvalidOperationException($"图片无法读取或格式不兼容: {Path.GetFileName(imagePath)}");
                }
            }
        }

        /// <summary>
        /// 执行完整排版：准备格位、加载素材、等比居中绘制并导出 CMYK TIFF。
        /// 勾选定位图时会先导出格子定位图，再输出不含边框的正式排版图。
        /// </summary>
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

            // 超大画布改由条带式 TIFF 写出，不再因完整 Bitmap 的内存保护而中断。
            PreparedLayout prepared = PrepareLayout(request.Settings, validateCanvasCapacity: false);
            if (imageFiles.Count > prepared.Capacity)
            {
                progressCallback?.Invoke($"素材共 {imageFiles.Count} 张，当前版式仅排入前 {prepared.Capacity} 张，其余素材不参与本次输出。");
                imageFiles = imageFiles.Take(prepared.Capacity).ToList();
            }

            progressCallback?.Invoke("正在预检图片...");
            ValidateSourceImages(imageFiles);

            string outputFileName = SanitizeOutputFileName(request.OutputFileName);
            string outputPath = NextOutputFile(request.OutputFolder, outputFileName, ".tif");
            string slotGuidePath = null;

            if (request.DrawSlotBounds)
            {
                slotGuidePath = NextOutputFile(request.OutputFolder, outputFileName + "_格子定位", ".tif");
                progressCallback?.Invoke("正在导出格子定位图...");
                ExportSlotGuide(prepared, slotGuidePath);
            }

            if (RequiresStripOutput(prepared))
            {
                progressCallback?.Invoke("正在以条带方式输出超大 TIFF...");
                ExportLayoutStreaming(prepared, imageFiles, outputPath, false, progressCallback);
                progressCallback?.Invoke("排版完成");
                return new LayoutOutputResult
                {
                    OutputPath = outputPath,
                    SlotGuidePath = slotGuidePath,
                    PlacedImageCount = imageFiles.Count,
                    CanvasSize = new Size(prepared.CanvasWidthPx, prepared.CanvasHeightPx),
                    Slots = prepared.Slots.AsReadOnly()
                };
            }

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
                }

                progressCallback?.Invoke("正在导出 TIF...");

                // 最终输出单张大图 TIFF，并顺便写入额外通道。
                AsposePSDHelper.SaveBitmapAsTiffWithSpotChannels(canvas, outputPath, new List<string> { WhiteInkChannelName, VarnishChannelName });
            }

            progressCallback?.Invoke("排版完成");
            return new LayoutOutputResult
            {
                OutputPath = outputPath,
                SlotGuidePath = slotGuidePath,
                PlacedImageCount = imageFiles.Count,
                CanvasSize = new Size(prepared.CanvasWidthPx, prepared.CanvasHeightPx),
                Slots = prepared.Slots.AsReadOnly()
            };
        }

        /// <summary>
        /// 在成品图上绘制格位边框，便于校验排版坐标及实际打印尺寸。
        /// </summary>
        /// <summary>
        /// 输出仅包含格位边框的定位图，用于先打印定位线后放置产品。
        /// </summary>
        /// <summary>
        /// 输出仅包含格位边框的定位图，用于先打印定位线后放置产品。
        /// </summary>
        private static void ExportSlotGuide(PreparedLayout prepared, string outputPath)
        {
            if (RequiresStripOutput(prepared))
            {
                ExportLayoutStreaming(prepared, Array.Empty<string>(), outputPath, true, null);
                return;
            }

            using (var canvas = new Bitmap(prepared.CanvasWidthPx, prepared.CanvasHeightPx, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                canvas.SetResolution(prepared.Dpi, prepared.Dpi);
                using (Graphics graphics = Graphics.FromImage(canvas))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.SmoothingMode = SmoothingMode.HighQuality;
                    DrawSlotBounds(graphics, prepared.Slots, prepared.Dpi);
                }

                AsposePSDHelper.SaveBitmapAsTiffWithSpotChannels(
                    canvas,
                    outputPath,
                    new List<string> { WhiteInkChannelName, VarnishChannelName });
            }
        }

        /// <summary>
        /// 按实际毫米线宽在每个格位边界绘制洋红色测量线。
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

        /// <summary>
        /// 判断当前画布是否不适合创建完整的 32 位位图，需要切换为低内存条带输出。
        /// </summary>
        private static bool RequiresStripOutput(PreparedLayout prepared)
        {
            long rawBytes = (long)prepared.CanvasWidthPx * prepared.CanvasHeightPx * 4L;
            return prepared.CanvasWidthPx > MaxBitmapDimensionPx
                || prepared.CanvasHeightPx > MaxBitmapDimensionPx
                || rawBytes > MaxCanvasBytes;
        }

        /// <summary>
        /// 分条渲染并写入 CMYK TIFF。每次仅保留一条小画布，避免超大排版图占满托管与 GDI 内存。
        /// </summary>
        private static void ExportLayoutStreaming(
            PreparedLayout prepared,
            IReadOnlyList<string> imageFiles,
            string outputPath,
            bool drawSlotBounds,
            Action<string> progressCallback)
        {
            var placedImages = new List<PlacedLayoutImage>();
            for (int i = 0; i < imageFiles.Count; i++)
            {
                using (Bitmap source = AsposePSDHelper.LoadBitmapForLayout(imageFiles[i]))
                {
                    placedImages.Add(new PlacedLayoutImage
                    {
                        Path = imageFiles[i],
                        Target = CalculateContainRect(source.Size, prepared.Slots[i])
                    });
                }
            }

            const int colorSamples = 4;
            const int extraSamples = 3; // Alpha + 两个专色占位通道
            int totalSamples = colorSamples + extraSamples;
            int stripCount = (prepared.CanvasHeightPx + OutputStripHeightPx - 1) / OutputStripHeightPx;

            using (Tiff tif = Tiff.Open(outputPath, "w"))
            {
                if (tif == null)
                    throw new InvalidOperationException("无法创建 TIFF 输出文件。");

                tif.SetField(TiffTag.IMAGEWIDTH, prepared.CanvasWidthPx);
                tif.SetField(TiffTag.IMAGELENGTH, prepared.CanvasHeightPx);
                tif.SetField(TiffTag.SAMPLESPERPIXEL, totalSamples);
                tif.SetField(TiffTag.BITSPERSAMPLE, 8);
                tif.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);
                tif.SetField(TiffTag.PHOTOMETRIC, Photometric.SEPARATED);
                tif.SetField(TiffTag.INKSET, InkSet.CMYK);
                tif.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
                tif.SetField(TiffTag.COMPRESSION, Compression.LZW);
                tif.SetField(TiffTag.RESOLUTIONUNIT, 2);
                tif.SetField(TiffTag.XRESOLUTION, (double)prepared.Dpi);
                tif.SetField(TiffTag.YRESOLUTION, (double)prepared.Dpi);
                tif.SetField(TiffTag.ROWSPERSTRIP, Math.Min(prepared.CanvasHeightPx, OutputStripHeightPx));
                tif.SetField(TiffTag.EXTRASAMPLES, extraSamples, new short[]
                {
                    (short)ExtraSample.UNASSALPHA,
                    (short)ExtraSample.UNSPECIFIED,
                    (short)ExtraSample.UNSPECIFIED
                });
                AsposePSDHelper.WritePhotoshopChannelNames(tif, new List<string>
                {
                    "Alpha", WhiteInkChannelName, VarnishChannelName
                });

                for (int stripIndex = 0; stripIndex < stripCount; stripIndex++)
                {
                    int stripTop = stripIndex * OutputStripHeightPx;
                    int stripHeight = Math.Min(OutputStripHeightPx, prepared.CanvasHeightPx - stripTop);
                    Rectangle stripBounds = new Rectangle(0, stripTop, prepared.CanvasWidthPx, stripHeight);
                    progressCallback?.Invoke($"正在写入 TIFF 条带 {stripIndex + 1}/{stripCount}...");

                    using (var strip = new Bitmap(prepared.CanvasWidthPx, stripHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                    using (Graphics graphics = Graphics.FromImage(strip))
                    {
                        ConfigureLayoutGraphics(graphics);
                        graphics.Clear(Color.Transparent);

                        foreach (PlacedLayoutImage image in placedImages.Where(item => item.Target.IntersectsWith(stripBounds)))
                        {
                            using (Bitmap source = AsposePSDHelper.LoadBitmapForLayout(image.Path))
                            {
                                Rectangle localTarget = image.Target;
                                localTarget.Y -= stripTop;
                                graphics.DrawImage(source, localTarget);
                            }
                        }

                        if (drawSlotBounds)
                            DrawSlotBoundsInStrip(graphics, prepared.Slots, prepared.Dpi, stripTop, stripBounds);

                        WriteStripScanlines(tif, strip, stripTop, totalSamples);
                    }
                }
            }
        }

        /// <summary>
        /// 统一条带画布与原有整图排版的绘制质量设置，保证两种输出的缩放效果一致。
        /// </summary>
        private static void ConfigureLayoutGraphics(Graphics graphics)
        {
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
        }

        /// <summary>
        /// 在局部条带坐标中绘制与当前条带相交的格位边框。
        /// </summary>
        private static void DrawSlotBoundsInStrip(Graphics graphics, IEnumerable<Rectangle> slots, int dpi, int stripTop, Rectangle stripBounds)
        {
            float lineWidth = Math.Max(1f, dpi * 0.3f / 25.4f);
            using (var pen = new Pen(Color.Magenta, lineWidth))
            {
                foreach (Rectangle slot in slots.Where(slot => slot.IntersectsWith(stripBounds)))
                {
                    graphics.DrawRectangle(pen, slot.Left, slot.Top - stripTop,
                        Math.Max(1, slot.Width - 1), Math.Max(1, slot.Height - 1));
                }
            }
        }

        /// <summary>
        /// 将 ARGB 条带逐行转为 CMYK、Alpha 和专色通道后直接写入 TIFF。
        /// </summary>
        private static void WriteStripScanlines(Tiff tif, Bitmap strip, int outputTop, int totalSamples)
        {
            var bounds = new Rectangle(0, 0, strip.Width, strip.Height);
            var data = strip.LockBits(bounds, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                int sourceRowBytes = strip.Width * 4;
                byte[] sourceRow = new byte[sourceRowBytes];
                byte[] scanline = new byte[strip.Width * totalSamples];

                for (int y = 0; y < strip.Height; y++)
                {
                    IntPtr rowPointer = data.Stride >= 0
                        ? IntPtr.Add(data.Scan0, y * data.Stride)
                        : IntPtr.Add(data.Scan0, (strip.Height - 1 - y) * -data.Stride);
                    Marshal.Copy(rowPointer, sourceRow, 0, sourceRowBytes);

                    for (int x = 0; x < strip.Width; x++)
                    {
                        int sourceIndex = x * 4;
                        int destinationIndex = x * totalSamples;
                        byte alpha = sourceRow[sourceIndex + 3];
                        byte r = CompositeChannelOverWhite(sourceRow[sourceIndex + 2], alpha);
                        byte g = CompositeChannelOverWhite(sourceRow[sourceIndex + 1], alpha);
                        byte b = CompositeChannelOverWhite(sourceRow[sourceIndex], alpha);

                        ConvertRgbToCmyk(r, g, b,
                            out scanline[destinationIndex],
                            out scanline[destinationIndex + 1],
                            out scanline[destinationIndex + 2],
                            out scanline[destinationIndex + 3]);
                        scanline[destinationIndex + 4] = alpha;
                        scanline[destinationIndex + 5] = (byte)(255 - alpha);
                        scanline[destinationIndex + 6] = (byte)(255 - alpha);
                    }

                    tif.WriteScanline(scanline, outputTop + y);
                }
            }
            finally
            {
                strip.UnlockBits(data);
            }
        }

        /// <summary>
        /// 将半透明像素预合成至白色 CMYK 底板，避免透明 RGB 零值被转换为黑色。
        /// </summary>
        private static byte CompositeChannelOverWhite(byte channel, byte alpha)
        {
            return (byte)((channel * alpha + 255 * (255 - alpha) + 127) / 255);
        }

        /// <summary>
        /// 使用与现有 TIFF 输出一致的基础 RGB 转 CMYK 公式。
        /// </summary>
        private static void ConvertRgbToCmyk(byte r, byte g, byte b, out byte c, out byte m, out byte y, out byte k)
        {
            int max = Math.Max(r, Math.Max(g, b));
            int black = 255 - max;
            k = (byte)black;
            if (black >= 255)
            {
                c = 0;
                m = 0;
                y = 0;
                return;
            }

            int denominator = 255 - black;
            c = (byte)((255 - r - black) * 255 / denominator);
            m = (byte)((255 - g - black) * 255 / denominator);
            y = (byte)((255 - b - black) * 255 / denominator);
        }

        /// <summary>
        /// 保存条带渲染所需的源文件与其在整张大图中的目标位置。
        /// </summary>
        private sealed class PlacedLayoutImage
        {
            public string Path { get; set; }
            public Rectangle Target { get; set; }
        }

        /// <summary>
        /// 计算素材在格位内等比完整显示的目标矩形，剩余空间自动居中留白。
        /// </summary>
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
        /// <summary>
        /// 按 DPI 将毫米换算为像素，并使用远离零舍入保证排版坐标稳定。
        /// </summary>
        private static int MmToPixels(decimal millimeters, decimal dpi)
        {
            decimal inches = millimeters / 25.4m;
            return Math.Max(1, (int)Math.Round(inches * dpi, MidpointRounding.AwayFromZero));
        }

        /// <summary>
        /// 清理用户输入的非法文件名字符，并为未输入名称提供默认值。
        /// </summary>
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
