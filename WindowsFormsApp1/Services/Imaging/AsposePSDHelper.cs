using BitMiracle.LibTiff;
using BitMiracle.LibTiff.Classic;
using ImageMagick;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Bitmap = System.Drawing.Bitmap;
using Color = System.Drawing.Color;
using Graphics = System.Drawing.Graphics;
using ImageCodecInfo = System.Drawing.Imaging.ImageCodecInfo;
using ImageFormat = System.Drawing.Imaging.ImageFormat;
using ImageLockMode = System.Drawing.Imaging.ImageLockMode;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using Rectangle = System.Drawing.Rectangle;

namespace WindowsFormsApp1
{
    public enum TemplateCompositeMode
    {
        Standard,
        FullBleed
    }

    public static class AsposePSDHelper
    {
        private const float OutputDpi = 300f;
        private static readonly ConcurrentDictionary<string, CachedImagePixelData> ImagePixelCache = new ConcurrentDictionary<string, CachedImagePixelData>(StringComparer.OrdinalIgnoreCase);

        private sealed class ImagePixelData
        {
            public int Width { get; }
            public int Height { get; }
            public int[] Pixels { get; }

            public ImagePixelData(int width, int height, int[] pixels)
            {
                Width = width;
                Height = height;
                Pixels = pixels ?? throw new ArgumentNullException(nameof(pixels));
            }
        }

        private sealed class LayerPixelData
        {
            public Rectangle CanvasBounds { get; }
            public int[] Pixels { get; }
            public Rectangle OpaqueBounds { get; }

            public LayerPixelData(Rectangle canvasBounds, int[] pixels, Rectangle opaqueBounds)
            {
                CanvasBounds = canvasBounds;
                Pixels = pixels ?? throw new ArgumentNullException(nameof(pixels));
                OpaqueBounds = opaqueBounds;
            }
        }

        /// <summary>
        /// 执行模板与素材的套图合成，并按目标格式写出最终文件。
        /// 该流程负责读取透明像素、满版/标准排版、镜像旋转及专色通道导出。
        /// </summary>
        public static void ProcessTifMode(
            string templateTifPath,
            string materialTifPath,
            string outputPath,
            string format,
            Action<string> progressCallback,
            List<string> channelNames,
            string rotation,
            string mirror,
            int offsetX = 0,
            int offsetY = 0,
            TemplateCompositeMode compositeMode = TemplateCompositeMode.Standard,
            string exclusionMaskPath = null,
            
            Action controlCheckpoint = null)
        {
            try
            {
                controlCheckpoint?.Invoke();
                progressCallback?.Invoke("正在读取模板...");
                var backgroundData = CloneImagePixelData(GetCachedImagePixelData(templateTifPath));

                controlCheckpoint?.Invoke();
                progressCallback?.Invoke("正在读取素材...");
                var foregroundData = LoadImagePixelData(materialTifPath);
                
                // 读取mask图像数据，如果是满版模式且提供了遮罩路径，则读取遮罩图像
                ImagePixelData exclusionMaskData = null;
                if (compositeMode == TemplateCompositeMode.FullBleed && !string.IsNullOrWhiteSpace(exclusionMaskPath))
                {
                    controlCheckpoint?.Invoke();
                    progressCallback?.Invoke("正在读取摄像头遮罩...");
                    exclusionMaskData = GetCachedImagePixelData(exclusionMaskPath);
                    Console.WriteLine($"摄像头遮罩路径: {exclusionMaskPath}");
                    Console.WriteLine($"摄像头遮罩尺寸: W={exclusionMaskData.Width}, H={exclusionMaskData.Height}");
                }

                // 先找出素材里真正有内容的非透明区域，后续贴图都以它为准。
                var opaqueBounds = GetOpaqueBounds(foregroundData.Pixels, foregroundData.Width, foregroundData.Height);
                int opaquePixels = CountOpaquePixels(foregroundData.Pixels);
                Console.WriteLine($"素材非透明像素: {opaquePixels}");
                Console.WriteLine($"素材内容区域: X={opaqueBounds.X}, Y={opaqueBounds.Y}, W={opaqueBounds.Width}, H={opaqueBounds.Height}");

                if (opaqueBounds == Rectangle.Empty)
                {
                    progressCallback?.Invoke("素材没有可用的非透明内容");
                    using (var emptyBitmap = CreateBitmapFromArgbPixels(backgroundData.Width, backgroundData.Height, backgroundData.Pixels))
                    {
                        controlCheckpoint?.Invoke();
                        SaveMergedAsFormat(emptyBitmap, outputPath, format, channelNames);
                    }
                    return;
                }

                if (opaqueBounds.Width == foregroundData.Width && opaqueBounds.Height == foregroundData.Height)
                {
                    Console.WriteLine("警告: 素材读取后非透明区域等于整张画布，说明 TIFF 透明信息可能已在读取阶段丢失。");
                }

                controlCheckpoint?.Invoke();
                progressCallback?.Invoke(compositeMode == TemplateCompositeMode.FullBleed ? "正在满版合成图像..." : "正在标准套图合成图像...");

                // 满版模式
                // 满版模式按模板可用区域和素材有效区域做覆盖式合成。
                if (compositeMode == TemplateCompositeMode.FullBleed)
                {
                    var templateBounds = GetOpaqueBounds(backgroundData.Pixels, backgroundData.Width, backgroundData.Height);
                    Console.WriteLine($"模板手机壳区域: X={templateBounds.X}, Y={templateBounds.Y}, W={templateBounds.Width}, H={templateBounds.Height}");
                    if (templateBounds == Rectangle.Empty)
                        throw new InvalidOperationException("模板没有可用于满版模式的非透明手机壳区域");
                    if (templateBounds.Width == backgroundData.Width && templateBounds.Height == backgroundData.Height)
                        Console.WriteLine("警告: 模板非透明区域等于整张画布，满版模式会铺满整张画布。请确认模板透明信息是否读取正确。");

                    DrawFullBleedPixels(backgroundData, foregroundData, templateBounds, opaqueBounds, exclusionMaskData, controlCheckpoint);
                }
                // 标准模式
                else
                {
                    Console.WriteLine($"素材贴图偏移: offsetX={offsetX}, offsetY={offsetY}");
                    DrawNonTransparentPixels(backgroundData, foregroundData, opaqueBounds, offsetX, offsetY, controlCheckpoint);
                }

                using (var mergedBitmap = CreateBitmapFromArgbPixels(backgroundData.Width, backgroundData.Height, backgroundData.Pixels))
                {
                    Bitmap current = mergedBitmap;
                    Bitmap intermediateMirror = null;
                    Bitmap intermediateRotation = null;
                    try
                    {
                        // 应用镜像
                        if (!string.IsNullOrWhiteSpace(mirror) && mirror != "无" && mirror != "none")
                        {
                            controlCheckpoint?.Invoke();
                            progressCallback?.Invoke($"正在应用镜像 ({mirror})...");
                            intermediateMirror = ApplyMirror(current, mirror);
                            current = intermediateMirror;
                        }

                        // 应用旋转
                        if (!string.IsNullOrWhiteSpace(rotation) && rotation != "0" && rotation != "无")
                        {
                            controlCheckpoint?.Invoke();
                            progressCallback?.Invoke($"正在应用旋转 ({rotation})...");
                            intermediateRotation = ApplyRotation(current, rotation);
                            current = intermediateRotation;
                        }

                        controlCheckpoint?.Invoke();
                        progressCallback?.Invoke("正在保存结果...");
                        SaveMergedAsFormat(current, outputPath, format, channelNames);
                    }
                    finally
                    {
                        if (intermediateRotation != null && intermediateRotation != mergedBitmap)
                            intermediateRotation.Dispose();
                        if (intermediateMirror != null && intermediateMirror != mergedBitmap)
                            intermediateMirror.Dispose();
                    }
                }

                controlCheckpoint?.Invoke();
                progressCallback?.Invoke("完成");
            }
            catch (OutOfMemoryException ex)
            {
                progressCallback?.Invoke($"图片太大或 TIFF 格式不兼容：{ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 从两个 Photoshop 分层 TIFF 中分别提取指定图层，再复用既有套图与专色输出流程。
        /// 临时 PNG 仅用于把图层透明像素交给现有稳定的图像读取链路，任务结束后会立即删除。
        /// </summary>
        public static void ProcessTiffLayerPair(
            string templateTifPath,
            string templateLayerName,
            string materialTifPath,
            string materialLayerName,
            string outputPath,
            string format,
            Action<string> progressCallback,
            List<string> channelNames,
            string rotation,
            string mirror,
            TemplateCompositeMode compositeMode = TemplateCompositeMode.Standard,
            string exclusionMaskPath = null,
            Action controlCheckpoint = null)
        {
            if (string.Equals(format, "TIF", StringComparison.OrdinalIgnoreCase) || string.Equals(format, "TIFF", StringComparison.OrdinalIgnoreCase))
            {
                ProcessCmykLayerPair(templateTifPath, materialTifPath, materialLayerName, outputPath, channelNames, compositeMode, progressCallback, controlCheckpoint);
                return;
            }

            string temporaryFolder = Path.Combine(Path.GetTempPath(), "WindowsFormsApp1", "LayerMerge");
            string token = Guid.NewGuid().ToString("N");
            string templateTemporaryPath = Path.Combine(temporaryFolder, token + "_template.png");
            string materialTemporaryPath = Path.Combine(temporaryFolder, token + "_material.png");

            try
            {
                Directory.CreateDirectory(temporaryFolder);
                controlCheckpoint?.Invoke();
                progressCallback?.Invoke(string.IsNullOrWhiteSpace(templateLayerName)
                    ? "正在提取模板可见图层..."
                    : $"正在提取模板图层: {templateLayerName}");
                using (Bitmap templateLayer = string.IsNullOrWhiteSpace(templateLayerName)
                    ? PhotoshopTiffLayerParser.RenderVisibleLayers(templateTifPath)
                    : PhotoshopTiffLayerParser.RenderLayer(templateTifPath, templateLayerName))
                {
                    templateLayer.Save(templateTemporaryPath, ImageFormat.Png);
                }

                controlCheckpoint?.Invoke();
                progressCallback?.Invoke($"正在提取素材图层: {materialLayerName}");
                using (Bitmap materialLayer = PhotoshopTiffLayerParser.RenderLayer(materialTifPath, materialLayerName))
                {
                    materialLayer.Save(materialTemporaryPath, ImageFormat.Png);
                }

                ProcessTifMode(
                    templateTemporaryPath,
                    materialTemporaryPath,
                    outputPath,
                    format,
                    progressCallback,
                    channelNames,
                    rotation,
                    mirror,
                    0,
                    0,
                    compositeMode,
                    exclusionMaskPath,
                    controlCheckpoint);
            }
            finally
            {
                // ProcessTifMode 会缓存模板像素；临时模板不能长期留在缓存中，避免批量双面任务累积内存。
                ImagePixelCache.TryRemove(Path.GetFullPath(templateTemporaryPath), out CachedImagePixelData ignoredCacheItem);
                TryDeleteTemporaryFile(templateTemporaryPath);
                TryDeleteTemporaryFile(materialTemporaryPath);
            }
        }

        /// <summary>双面 CMYK TIFF 专用流程：图层提取、满版合成、输出全程保留 C/M/Y/K/Alpha。</summary>
        private static void ProcessCmykLayerPair(string templatePath, string materialPath, string materialLayerName, string outputPath, List<string> channelNames, TemplateCompositeMode mode, Action<string> progress, Action checkpoint)
        {
            progress?.Invoke("正在读取 CMYK 模板图层...");
            PhotoshopTiffCmykLayerImage target = PhotoshopTiffLayerParser.RenderVisibleCmykLayers(templatePath);
            progress?.Invoke("正在读取 CMYK 素材图层...");
            PhotoshopTiffCmykLayerImage source = PhotoshopTiffLayerParser.RenderCmykLayer(materialPath, materialLayerName);
            Rectangle targetBounds = GetCmykBounds(target), sourceBounds = GetCmykBounds(source);
            if (targetBounds == Rectangle.Empty || sourceBounds == Rectangle.Empty) throw new InvalidOperationException("CMYK 图层没有可用的非透明区域。");
            float scale = mode == TemplateCompositeMode.FullBleed ? Math.Max((float)targetBounds.Width / sourceBounds.Width, (float)targetBounds.Height / sourceBounds.Height) : 1f;
            int sw = (int)Math.Ceiling(sourceBounds.Width * scale), sh = (int)Math.Ceiling(sourceBounds.Height * scale);
            float left = mode == TemplateCompositeMode.FullBleed ? targetBounds.Left + (targetBounds.Width - sw) / 2f : sourceBounds.Left;
            float top = mode == TemplateCompositeMode.FullBleed ? targetBounds.Top + (targetBounds.Height - sh) / 2f : sourceBounds.Top;
            Rectangle draw = mode == TemplateCompositeMode.FullBleed ? targetBounds : Rectangle.Intersect(sourceBounds, new Rectangle(0, 0, target.Width, target.Height));
            for (int y = draw.Top; y < draw.Bottom; y++)
            {
                if ((y - draw.Top) % 32 == 0) checkpoint?.Invoke();
                for (int x = draw.Left; x < draw.Right; x++)
                {
                    int sx = sourceBounds.Left + (int)Math.Floor((x - left) / scale), sy = sourceBounds.Top + (int)Math.Floor((y - top) / scale);
                    if (sx < sourceBounds.Left || sx >= sourceBounds.Right || sy < sourceBounds.Top || sy >= sourceBounds.Bottom) continue;
                    BlendCmykPixel(target, y * target.Width + x, source, sy * source.Width + sx);
                }
            }
            progress?.Invoke("正在写入 CMYK TIFF...");
            SaveRawCmykTiff(target, outputPath, channelNames);
            progress?.Invoke("完成");
        }

        private static Rectangle GetCmykBounds(PhotoshopTiffCmykLayerImage image)
        {
            int l = image.Width, t = image.Height, r = -1, b = -1;
            for (int y = 0; y < image.Height; y++) for (int x = 0; x < image.Width; x++) if (image.A[y * image.Width + x] > 0) { l = Math.Min(l, x); t = Math.Min(t, y); r = Math.Max(r, x); b = Math.Max(b, y); }
            return r < l ? Rectangle.Empty : Rectangle.FromLTRB(l, t, r + 1, b + 1);
        }

        private static void BlendCmykPixel(PhotoshopTiffCmykLayerImage target, int d, PhotoshopTiffCmykLayerImage source, int s)
        {
            int sa = source.A[s] * target.A[d] / 255; if (sa == 0) return; int da = target.A[d], oa = sa + da * (255 - sa) / 255;
            target.C[d] = BlendCmyk(target.C[d], source.C[s], da, sa, oa); target.M[d] = BlendCmyk(target.M[d], source.M[s], da, sa, oa); target.Y[d] = BlendCmyk(target.Y[d], source.Y[s], da, sa, oa); target.K[d] = BlendCmyk(target.K[d], source.K[s], da, sa, oa); target.A[d] = (byte)oa;
        }
        private static byte BlendCmyk(byte d, byte s, int da, int sa, int oa) => oa == 0 ? (byte)0 : (byte)((s * sa + d * da * (255 - sa) / 255) / oa);

        private static void SaveRawCmykTiff(PhotoshopTiffCmykLayerImage image, string outputPath, List<string> channelNames)
        {
            var names = (channelNames ?? new List<string>()).Select(n => n ?? string.Empty).ToList(); bool alpha = image.A.Any(a => a < 255); int samples = 4 + names.Count + (alpha ? 1 : 0);
            using (Tiff tif = Tiff.Open(outputPath, "w"))
            {
                tif.SetField(TiffTag.IMAGEWIDTH, image.Width); tif.SetField(TiffTag.IMAGELENGTH, image.Height); tif.SetField(TiffTag.SAMPLESPERPIXEL, samples); tif.SetField(TiffTag.BITSPERSAMPLE, 8); tif.SetField(TiffTag.PHOTOMETRIC, Photometric.SEPARATED); tif.SetField(TiffTag.INKSET, InkSet.CMYK); tif.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG); tif.SetField(TiffTag.COMPRESSION, Compression.LZW); tif.SetField(TiffTag.XRESOLUTION, OutputDpi); tif.SetField(TiffTag.YRESOLUTION, OutputDpi); tif.SetField(TiffTag.RESOLUTIONUNIT, 2);
                if (alpha || names.Count > 0) { short[] extras = new short[samples - 4]; int n = 0; if (alpha) extras[n++] = (short)ExtraSample.UNASSALPHA; while (n < extras.Length) extras[n++] = (short)ExtraSample.UNSPECIFIED; tif.SetField(TiffTag.EXTRASAMPLES, extras.Length, extras); }
                // EXTRASAMPLES 只定义通道类型；Photoshop 读取自定义名称依赖 34377 图像资源块。
                var photoshopChannelNames = new List<string>();
                if (alpha) photoshopChannelNames.Add("Alpha");
                photoshopChannelNames.AddRange(names);
                TryWritePhotoshopChannelNames(tif, photoshopChannelNames);
                byte[] row = new byte[image.Width * samples]; for (int y = 0; y < image.Height; y++) { for (int x = 0; x < image.Width; x++) { int p = y * image.Width + x, o = x * samples; byte pixelAlpha = image.A[p]; // Photoshop 图层通道以白色表示无墨，写入 TIFF 分色时需反相；同时按 Alpha 减墨，防止透明底被写成满墨黑色。
                    row[o] = ConvertPhotoshopCmykToTiffInk(image.C[p], pixelAlpha); row[o+1] = ConvertPhotoshopCmykToTiffInk(image.M[p], pixelAlpha); row[o+2] = ConvertPhotoshopCmykToTiffInk(image.Y[p], pixelAlpha); row[o+3] = ConvertPhotoshopCmykToTiffInk(image.K[p], pixelAlpha); int e=4; if(alpha)row[o+e++]=pixelAlpha; for(int i=0;i<names.Count;i++)row[o+e++]=(byte)(255-pixelAlpha); } tif.WriteScanline(row,y); }
            }
        }

        /// <summary>
        /// 将 Photoshop 图层通道的白色无墨极性转换为 TIFF 墨量，并消除透明区域的底墨。
        /// </summary>
        private static byte ConvertPhotoshopCmykToTiffInk(byte photoshopChannel, byte alpha)
        {
            return (byte)((255 - photoshopChannel) * alpha / 255);
        }

        /// <summary>
        /// 删除任务使用的临时图层文件；删除失败不影响已完成的输出。
        /// </summary>
        private static void TryDeleteTemporaryFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"清理临时图层文件失败: {ex.Message}");
            }
        }

        // 镜像
        /// <summary>
        /// 根据指定方向创建图像镜像副本；未指定有效方向时直接返回原对象。
        /// </summary>
        private static Bitmap ApplyMirror(Bitmap source, string mirror)
        {
            if (source == null)
                return null;
            if (string.IsNullOrWhiteSpace(mirror) || mirror == "无" || mirror == "none")
                return source;

            string normalized = (mirror ?? string.Empty).Trim().ToLowerInvariant();
            RotateFlipType flipType;

            if (normalized.Contains("horizontal") || normalized.Contains("水平") || normalized == "h")
            {
                flipType = RotateFlipType.RotateNoneFlipX;
            }
            else if (normalized.Contains("vertical") || normalized.Contains("垂直") || normalized == "v")
            {
                flipType = RotateFlipType.RotateNoneFlipY;
            }
            else
            {
                Console.WriteLine($"未识别的镜像方向: {mirror}，跳过镜像");
                return source;
            }

            var result = new Bitmap(source);
            ApplyOutputResolution(result);
            result.RotateFlip(flipType);
            return result;
        }

        // 旋转
        /// <summary>
        /// 根据角度创建旋转副本，并同步保留统一的输出 DPI。
        /// </summary>
        private static Bitmap ApplyRotation(Bitmap source, string rotation)
        {
            if (source == null)
                return null;
            if (string.IsNullOrWhiteSpace(rotation) || rotation == "0" || rotation == "无")
                return source;

            string normalized = (rotation ?? string.Empty).Trim().ToLowerInvariant();
            RotateFlipType rotateType;

            if (normalized == "90" || normalized == "90度" || normalized.Contains("90"))
            {
                rotateType = RotateFlipType.Rotate90FlipNone;
            }
            else if (normalized == "180" || normalized == "180度" || normalized.Contains("180"))
            {
                rotateType = RotateFlipType.Rotate180FlipNone;
            }
            else if (normalized == "270" || normalized == "270度" || normalized.Contains("270"))
            {
                rotateType = RotateFlipType.Rotate270FlipNone;
            }
            else if (normalized == "0" || string.IsNullOrWhiteSpace(normalized))
            {
                return source;
            }
            else
            {
                Console.WriteLine($"未识别的旋转角度: {rotation}，跳过旋转");
                return source;
            }

            var result = new Bitmap(source);
            ApplyOutputResolution(result);
            result.RotateFlip(rotateType);
            return result;
        }

        /// <summary>
        /// 读取排版画布使用的位图，并尽可能保留源文件的颜色与透明信息。
        /// </summary>
        public static Bitmap LoadBitmapForLayout(string imagePath)
        {
            if (string.Equals(Path.GetExtension(imagePath), ".tif", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetExtension(imagePath), ".tiff", StringComparison.OrdinalIgnoreCase))
            {
                // 排版仅需要 TIFF 的最终可见栅格。不要尝试 Photoshop 图层重建，
                // 否则部分 CMYK 分层 TIFF 会在 ARGB 重组阶段产生色彩偏移。
                return CreateBitmapFromArgbPixelsForLayout(LoadTiffRasterPixelDataForLayout(imagePath));
            }

            return LoadBitmapPreserveColor(imagePath);
        }

        /// <summary>
        /// 生成排版窗口专用缩略图，确保预览和实际排版使用同一条 TIFF 读取链路。
        /// </summary>
        public static Bitmap GenerateLayoutPreview(string imagePath)
        {
            return LoadBitmapForLayout(imagePath);
        }

        /// <summary>
        /// 轻量验证图片是否可打开。仅读取文件头、尺寸与 TIFF 基础标签，
        /// 不解码整张像素数据，供批量套图预检使用。
        /// </summary>
        public static void ValidateImageHeader(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
                throw new ArgumentException("图片路径不能为空。", nameof(imagePath));
            if (!File.Exists(imagePath))
                throw new FileNotFoundException("图片文件不存在。", imagePath);

            string extension = Path.GetExtension(imagePath).ToLowerInvariant();
            if (extension == ".tif" || extension == ".tiff")
            {
                using (Tiff tif = Tiff.Open(imagePath, "r"))
                {
                    if (tif == null)
                        throw new InvalidOperationException("无法打开 TIFF 文件。");

                    FieldValue[] widthField = tif.GetField(TiffTag.IMAGEWIDTH);
                    FieldValue[] heightField = tif.GetField(TiffTag.IMAGELENGTH);
                    if (widthField == null || heightField == null ||
                        widthField[0].ToInt() <= 0 || heightField[0].ToInt() <= 0)
                        throw new InvalidOperationException("TIFF 尺寸无效。");
                }

                return;
            }

            using (var image = Aspose.PSD.Image.Load(imagePath))
            {
                if (image == null || image.Width <= 0 || image.Height <= 0)
                    throw new InvalidOperationException("图片尺寸无效。");
            }
        }

        /// <summary>
        /// 将位图导出为不带额外专色通道的 CMYK TIFF。
        /// </summary>
        public static void SaveBitmapAsFlatTiff(Bitmap bitmap, string outputPath)
        {
            SaveAsCmykTiff(bitmap, outputPath);
        }

        /// <summary>
        /// 将位图导出为带命名额外通道的 CMYK TIFF，供白墨、光油等专色流程使用。
        /// </summary>
        public static void SaveBitmapAsTiffWithSpotChannels(Bitmap bitmap, string outputPath, List<string> channelNames)
        {
            SaveAsTiffWithSpotChannels(bitmap, outputPath, channelNames);
        }

        /// <summary>
        /// 将统一像素数据重新封装为可绘制位图，避免不同文件格式走不同画布路径。
        /// </summary>
        private static Bitmap LoadBitmapPreserveColor(string imagePath)
        {
            var pixelData = LoadImagePixelData(imagePath);
            return CreateBitmapFromArgbPixels(pixelData.Width, pixelData.Height, pixelData.Pixels);
        }

        /// <summary>
        /// 获取缓存中的图像像素数据；如果源文件已变化则自动刷新缓存。
        /// </summary>
        private static ImagePixelData GetCachedImagePixelData(string imagePath)
        {
            string cacheKey = Path.GetFullPath(imagePath);
            var fileInfo = new FileInfo(cacheKey);
            if (!fileInfo.Exists)
                throw new FileNotFoundException("找不到图像文件。", cacheKey);

            var cached = ImagePixelCache.AddOrUpdate(
                cacheKey,
                _ => new CachedImagePixelData(fileInfo.Length, fileInfo.LastWriteTimeUtc, LoadImagePixelData(cacheKey)),
                (_, existing) =>
                {
                    if (existing.FileLength == fileInfo.Length && existing.LastWriteTimeUtc == fileInfo.LastWriteTimeUtc)
                    {
                        return existing;
                    }

                    return new CachedImagePixelData(fileInfo.Length, fileInfo.LastWriteTimeUtc, LoadImagePixelData(cacheKey));
                });

            return cached.PixelData;
        }

        /// <summary>
        /// 为需要写入的图像创建独立副本，防止缓存像素被当前任务修改。
        /// </summary>
        private static ImagePixelData CloneImagePixelData(ImagePixelData source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            var clonedPixels = new int[source.Pixels.Length];
            Array.Copy(source.Pixels, clonedPixels, source.Pixels.Length);
            return new ImagePixelData(source.Width, source.Height, clonedPixels);
        }

        /// <summary>
        /// 按文件类型读取 ARGB 像素；TIFF 使用专用读取链路以优先恢复透明区域。
        /// </summary>
        private static ImagePixelData LoadImagePixelData(string imagePath)
        {
            var ext = Path.GetExtension(imagePath).ToLowerInvariant();
            if (ext == ".tif" || ext == ".tiff")
            {
                return LoadTiffPixelData(imagePath);
            }

            using (var source = System.Drawing.Image.FromFile(imagePath))
            {
                var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.Transparent);
                    g.DrawImage(source, 0, 0, source.Width, source.Height);
                }

                try
                {
                    return ExtractPixelData(bitmap);
                }
                finally
                {
                    bitmap.Dispose();
                }
            }
        }

        /// <summary>
        /// 读取 TIFF 像素并按图层、原始透明度、白底抠图三个层级尝试恢复可见区域。
        /// </summary>
        private static ImagePixelData LoadTiffPixelData(string tifPath)
        {
            var layeredData = TryLoadTiffLayerPixelData(tifPath);
            if (layeredData != null)
                return layeredData;

            var pixelData = TryLoadTiffRasterPixelData(tifPath);
            var opaqueBounds = GetOpaqueBounds(pixelData.Pixels, pixelData.Width, pixelData.Height);
            if (opaqueBounds != Rectangle.Empty &&
                opaqueBounds.Width == pixelData.Width &&
                opaqueBounds.Height == pixelData.Height)
            {
                Console.WriteLine("TIFF整图都被读成了非透明，尝试按白底抠图恢复透明...");
                var maskedData = TryLoadTiffPixelDataWithWhiteMask(tifPath);
                if (maskedData != null)
                    return maskedData;
            }

            return pixelData;
        }

        /// <summary>
        /// 读取排版素材的最终 TIFF 栅格，保留透明度恢复逻辑但跳过 Photoshop 图层像素重组。
        /// </summary>
        private static ImagePixelData LoadTiffRasterPixelDataForLayout(string tifPath)
        {
            var cmykPixelData = TryLoadCmykTiffPixelDataForLayout(tifPath);
            if (cmykPixelData != null)
                return cmykPixelData;

            var pixelData = TryLoadTiffRasterPixelData(tifPath);
            var opaqueBounds = GetOpaqueBounds(pixelData.Pixels, pixelData.Width, pixelData.Height);
            if (opaqueBounds != Rectangle.Empty &&
                opaqueBounds.Width == pixelData.Width &&
                opaqueBounds.Height == pixelData.Height)
            {
                var maskedData = TryLoadTiffPixelDataWithWhiteMask(tifPath);
                if (maskedData != null)
                    return maskedData;
            }

            return pixelData;
        }

        /// <summary>
        /// 直接读取 CMYK TIFF 的扫描线。排版时必须忽略 Alpha 后的专色通道，
        /// 避免通用解码器将 C、M、Y 或额外通道误解释为 RGB 而出现偏绿等颜色错误。
        /// </summary>
        private static ImagePixelData TryLoadCmykTiffPixelDataForLayout(string tifPath)
        {
            try
            {
                using (var tif = Tiff.Open(tifPath, "r"))
                {
                    if (tif == null)
                        return null;

                    var widthField = tif.GetField(TiffTag.IMAGEWIDTH);
                    var heightField = tif.GetField(TiffTag.IMAGELENGTH);
                    var samplesField = tif.GetField(TiffTag.SAMPLESPERPIXEL);
                    var bitsField = tif.GetField(TiffTag.BITSPERSAMPLE);
                    var photometricField = tif.GetField(TiffTag.PHOTOMETRIC);
                    var planarField = tif.GetField(TiffTag.PLANARCONFIG);
                    if (widthField == null || heightField == null || samplesField == null || bitsField == null ||
                        photometricField == null || planarField == null)
                        return null;

                    int width = widthField[0].ToInt();
                    int height = heightField[0].ToInt();
                    int samples = samplesField[0].ToInt();
                    if (width <= 0 || height <= 0 || samples < 4 || bitsField[0].ToInt() != 8 ||
                        photometricField[0].ToInt() != (int)Photometric.SEPARATED ||
                        planarField[0].ToInt() != (int)PlanarConfig.CONTIG)
                        return null;

                    var pixels = new int[checked(width * height)];
                    var scanline = new byte[checked(width * samples)];
                    bool hasAlpha = samples > 4;

                    for (int y = 0; y < height; y++)
                    {
                        tif.ReadScanline(scanline, y);
                        int rowOffset = y * width;
                        for (int x = 0; x < width; x++)
                        {
                            int offset = x * samples;
                            byte c = scanline[offset];
                            byte m = scanline[offset + 1];
                            byte yellow = scanline[offset + 2];
                            byte k = scanline[offset + 3];
                            byte alpha = hasAlpha ? scanline[offset + 4] : byte.MaxValue;

                            // TIFF 的 CMYK 分量表示墨量，先按减色法合成，再转换为供 GDI 绘制的 RGB。
                            byte r = ConvertCmykInkToRgb(c, k);
                            byte g = ConvertCmykInkToRgb(m, k);
                            byte b = ConvertCmykInkToRgb(yellow, k);
                            pixels[rowOffset + x] = (alpha << 24) | (r << 16) | (g << 8) | b;
                        }
                    }

                    return new ImagePixelData(width, height, pixels);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"按 CMYK 扫描线读取排版 TIFF 失败，改用通用读取链路: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 将单个 CMYK 墨量分量与黑版合成为对应的 RGB 显示分量。
        /// </summary>
        private static byte ConvertCmykInkToRgb(byte colorInk, byte blackInk)
        {
            int ink = colorInk * (255 - blackInk) / 255 + blackInk;
            return (byte)(255 - Math.Min(255, ink));
        }

        /// <summary>
        /// 将排版读取到的像素数据封装为可绘制位图。
        /// </summary>
        private static Bitmap CreateBitmapFromArgbPixelsForLayout(ImagePixelData pixelData)
        {
            if (pixelData == null)
                throw new ArgumentNullException(nameof(pixelData));

            return CreateBitmapFromArgbPixels(pixelData.Width, pixelData.Height, pixelData.Pixels);
        }

        /// <summary>
        /// 依次使用 Aspose、Magick.NET 与 LibTiff 读取 TIFF 栅格，保证格式兼容性。
        /// </summary>
        private static ImagePixelData TryLoadTiffRasterPixelData(string tifPath)
        {
            // try
            // {
            //     using (var image = Aspose.PSD.Image.Load(tifPath))
            //     {
            //         var raster = image as Aspose.PSD.RasterImage;
            //         if (raster != null)
            //         {
            //             var rect = new Aspose.PSD.Rectangle(0, 0, raster.Width, raster.Height);
            //             return new ImagePixelData(raster.Width, raster.Height, raster.LoadArgb32Pixels(rect));
            //         }
            //     }
            // }
            // catch (Exception ex)
            // {
            //     Console.WriteLine($"Aspose.PSD 读取 TIFF 失败，转入备用路径: {ex.Message}");
            // }

            // try
            // {
            //     using (var image = new MagickImage(tifPath))
            //     {
            //         image.Alpha(AlphaOption.On);
            //         using (var transparentPng = image.Clone())
            //         {
            //             transparentPng.Format = MagickFormat.Png32;
            //             using (var ms = new MemoryStream())
            //             {
            //                 transparentPng.Write(ms);
            //                 ms.Position = 0;
            //                 using (var pngBitmap = new Bitmap(ms))
            //                 using (var bitmap = new Bitmap(pngBitmap))
            //                 {
            //                     return ExtractPixelData(bitmap);
            //                 }
            //             }
            //         }
            //     }
            // }
            // catch (Exception ex)
            // {
            //     Console.WriteLine($"Magick.NET 读取 TIFF 失败，转入 LibTiff 备用路径: {ex.Message}");
            // }

            using (var bitmap = LoadTiffToBitmap(tifPath))
            {
                return ExtractPixelData(bitmap);
            }
        }

        /// <summary>
        /// 当 TIFF 丢失透明度且背景为白色时，尝试将接近白色的像素恢复为透明。
        /// </summary>
        private static ImagePixelData TryLoadTiffPixelDataWithWhiteMask(string tifPath)
        {
            try
            {
                using (var image = new MagickImage(tifPath))
                {
                    // 转RGB处理
                    image.ColorSpace = ColorSpace.sRGB;
                    image.Alpha(AlphaOption.On);
                    image.ColorFuzz = new Percentage(3);
                    image.Transparent(MagickColors.White);

                    using (var maskedPng = image.Clone())
                    {
                        maskedPng.Format = MagickFormat.Png32;
                        using (var ms = new MemoryStream())
                        {
                            maskedPng.Write(ms);
                            ms.Position = 0;
                            using (var pngBitmap = new Bitmap(ms))
                            using (var bitmap = new Bitmap(pngBitmap))
                            {
                                var maskedData = ExtractPixelData(bitmap);
                                var maskedBounds = GetOpaqueBounds(maskedData.Pixels, maskedData.Width, maskedData.Height);
                                
                                Console.WriteLine($"白底抠图后的内容区域: X={maskedBounds.X}, Y={maskedBounds.Y}, W={maskedBounds.Width}, H={maskedBounds.Height}");
                                return maskedData;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"按白底抠图恢复透明失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 尝试从包含 Photoshop 图层信息的 TIFF 中重建透明画布，忽略整画布背景层。
        /// </summary>
        private static ImagePixelData TryLoadTiffLayerPixelData(string tifPath)
        {
            try
            {
                using (var psdImage = Aspose.PSD.Image.Load(tifPath) as Aspose.PSD.FileFormats.Psd.PsdImage)
                {
                    if (psdImage == null || psdImage.Layers == null || psdImage.Layers.Length == 0)
                        return null;

                    Console.WriteLine($"TIFF图层数: {psdImage.Layers.Length}");

                    var contentLayers = new List<LayerPixelData>();
                    for (int i = 0; i < psdImage.Layers.Length; i++)
                    {
                        var layer = psdImage.Layers[i];
                        if (layer == null || !layer.IsVisible)
                            continue;

                        int layerWidth = layer.Right - layer.Left;
                        int layerHeight = layer.Bottom - layer.Top;
                        if (layerWidth <= 0 || layerHeight <= 0)
                            continue;

                        var layerRect = new Aspose.PSD.Rectangle(layer.Left, layer.Top, layerWidth, layerHeight);
                        var layerPixels = layer.LoadArgb32Pixels(layerRect);
                        var opaqueBounds = GetOpaqueBounds(layerPixels, layerWidth, layerHeight);
                        if (opaqueBounds == Rectangle.Empty)
                            continue;

                        bool fullCanvasOpaque =
                            layer.Left <= 0 &&
                            layer.Top <= 0 &&
                            layer.Right >= psdImage.Width &&
                            layer.Bottom >= psdImage.Height &&
                            opaqueBounds.X == 0 &&
                            opaqueBounds.Y == 0 &&
                            opaqueBounds.Width == layerWidth &&
                            opaqueBounds.Height == layerHeight;

                        Console.WriteLine($"图层[{i}] {layer.Name}: X={layer.Left}, Y={layer.Top}, W={layerWidth}, H={layerHeight}, Opaque=({opaqueBounds.X},{opaqueBounds.Y},{opaqueBounds.Width},{opaqueBounds.Height}), FullCanvasOpaque={fullCanvasOpaque}");

                        if (fullCanvasOpaque)
                            continue;

                        contentLayers.Add(new LayerPixelData(
                            new Rectangle(layer.Left, layer.Top, layerWidth, layerHeight),
                            layerPixels,
                            opaqueBounds));
                    }

                    if (contentLayers.Count == 0)
                    {
                        Console.WriteLine("未找到带透明边界的内容图层，回退到整图读取。");
                        return null;
                    }

                    var canvas = new ImagePixelData(psdImage.Width, psdImage.Height, new int[psdImage.Width * psdImage.Height]);
                    foreach (var layerData in contentLayers)
                    {
                        DrawLayerPixels(canvas, layerData);
                    }

                    Console.WriteLine($"已按内容图层重建透明画布，内容图层数: {contentLayers.Count}");
                    return canvas;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"按图层读取 TIFF 失败，回退到整图读取: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 使用 LockBits 高效读取 32 位 ARGB 位图，转换为便于合成的整型像素数组。
        /// </summary>
        private static ImagePixelData ExtractPixelData(Bitmap bitmap)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var bitmapData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            try
            {
                int bytes = Math.Abs(bitmapData.Stride) * bitmap.Height;
                var buffer = new byte[bytes];
                var pixels = new int[bitmap.Width * bitmap.Height];
                Marshal.Copy(bitmapData.Scan0, buffer, 0, bytes);

                for (int y = 0; y < bitmap.Height; y++)
                {
                    int row = bitmapData.Stride > 0
                        ? y * bitmapData.Stride
                        : (bitmap.Height - 1 - y) * -bitmapData.Stride;

                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        int index = row + x * 4;
                        int b = buffer[index];
                        int g = buffer[index + 1];
                        int r = buffer[index + 2];
                        int a = buffer[index + 3];
                        pixels[y * bitmap.Width + x] = (a << 24) | (r << 16) | (g << 8) | b;
                    }
                }

                return new ImagePixelData(bitmap.Width, bitmap.Height, pixels);
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }
        }

        /// <summary>
        /// 将 ARGB 整型像素数组写入新的 32 位位图，用于合成结果和画布显示。
        /// </summary>
        private static Bitmap CreateBitmapFromArgbPixels(int width, int height, int[] argbPixels)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, width, height);
            var bitmapData = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            try
            {
                int bytes = Math.Abs(bitmapData.Stride) * height;
                var buffer = new byte[bytes];

                for (int y = 0; y < height; y++)
                {
                    int row = bitmapData.Stride > 0
                        ? y * bitmapData.Stride
                        : (height - 1 - y) * -bitmapData.Stride;

                    for (int x = 0; x < width; x++)
                    {
                        int pixel = argbPixels[y * width + x];
                        int index = row + x * 4;

                        buffer[index] = (byte)(pixel & 0xFF);
                        buffer[index + 1] = (byte)((pixel >> 8) & 0xFF);
                        buffer[index + 2] = (byte)((pixel >> 16) & 0xFF);
                        buffer[index + 3] = (byte)((pixel >> 24) & 0xFF);
                    }
                }

                Marshal.Copy(buffer, 0, bitmapData.Scan0, bytes);
                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }
        }

        /// <summary>
        /// 标准套图：按素材原坐标和偏移量，将非透明像素混合到模板画布。
        /// </summary>
        private static void DrawNonTransparentPixels(
            ImagePixelData background,
            ImagePixelData foreground,
            Rectangle opaqueBounds,
            int offsetX,
            int offsetY,
            Action controlCheckpoint = null)
        {
            if (background == null)
                throw new ArgumentNullException(nameof(background));
            if (foreground == null)
                throw new ArgumentNullException(nameof(foreground));
            if (opaqueBounds == Rectangle.Empty)
                return;

            int destLeft = opaqueBounds.Left + offsetX;
            int destTop = opaqueBounds.Top + offsetY;
            int destRight = opaqueBounds.Right + offsetX;
            int destBottom = opaqueBounds.Bottom + offsetY;

            var drawBounds = Rectangle.Intersect(
                new Rectangle(destLeft, destTop, destRight - destLeft, destBottom - destTop),
                new Rectangle(0, 0, background.Width, background.Height));

            if (drawBounds == Rectangle.Empty)
                return;

            for (int destY = drawBounds.Top; destY < drawBounds.Bottom; destY++)
            {
                if ((destY - drawBounds.Top) % 32 == 0)
                {
                    controlCheckpoint?.Invoke();
                }

                int sourceY = destY - offsetY;
                int backgroundRow = destY * background.Width;
                int foregroundRow = sourceY * foreground.Width;

                for (int destX = drawBounds.Left; destX < drawBounds.Right; destX++)
                {
                    int sourceX = destX - offsetX;
                    int backgroundIndex = backgroundRow + destX;
                    int foregroundIndex = foregroundRow + sourceX;
                    BlendPixel(background.Pixels, backgroundIndex, foreground.Pixels[foregroundIndex]);
                }
            }
        }


        // 贴合区域大小及比例操作
        /// <summary>
        /// 满版套图：按模板可打印区域等比放大素材，并跳过摄像头等排除区域。
        /// </summary>
        private static void DrawFullBleedPixels(
            ImagePixelData background,
            ImagePixelData foreground,
            Rectangle templateBounds,
            Rectangle materialBounds,
            ImagePixelData exclusionMask,
            Action controlCheckpoint = null)
        {
            if (background == null)
                throw new ArgumentNullException(nameof(background));
            if (foreground == null)
                throw new ArgumentNullException(nameof(foreground));
            if (templateBounds == Rectangle.Empty || materialBounds == Rectangle.Empty)
                return;

            float scale = Math.Max(
                (float)templateBounds.Width / materialBounds.Width,
                (float)templateBounds.Height / materialBounds.Height);

            int scaledWidth = (int)Math.Ceiling(materialBounds.Width * scale);
            int scaledHeight = (int)Math.Ceiling(materialBounds.Height * scale);
            float drawLeft = templateBounds.Left + (templateBounds.Width - scaledWidth) / 2f;
            float drawTop = templateBounds.Top + (templateBounds.Height - scaledHeight) / 2f;
            int[] scaledPixels = OpenCvMergeRenderer.ResizeArgbRegion(
                foreground.Pixels,
                foreground.Width,
                foreground.Height,
                materialBounds,
                scaledWidth,
                scaledHeight);

            for (int destY = templateBounds.Top; destY < templateBounds.Bottom; destY++)
            {
                if ((destY - templateBounds.Top) % 32 == 0)
                {
                    controlCheckpoint?.Invoke();
                }

                if (destY < 0 || destY >= background.Height)
                    continue;

                int backgroundRow = destY * background.Width;
                for (int destX = templateBounds.Left; destX < templateBounds.Right; destX++)
                {
                    if (destX < 0 || destX >= background.Width)
                        continue;

                    if (IsExcludedByMask(exclusionMask, destX, destY, background.Width, background.Height))
                        continue;

                    int backgroundIndex = backgroundRow + destX;
                    byte templateAlpha = (byte)((background.Pixels[backgroundIndex] >> 24) & 0xFF);
                    if (templateAlpha == 0)
                        continue;

                    int sourceX = (int)Math.Floor(destX - drawLeft);
                    int sourceY = (int)Math.Floor(destY - drawTop);
                    if (sourceX < 0 || sourceX >= scaledWidth || sourceY < 0 || sourceY >= scaledHeight)
                    {
                        continue;
                    }

                    int sourcePixel = scaledPixels[sourceY * scaledWidth + sourceX];
                    byte sourceAlpha = (byte)((sourcePixel >> 24) & 0xFF);
                    if (sourceAlpha == 0)
                        continue;

                    if (templateAlpha < 255)
                    {
                        int adjustedAlpha = sourceAlpha * templateAlpha / 255;
                        sourcePixel = (sourcePixel & 0x00FFFFFF) | (adjustedAlpha << 24);
                    }

                    BlendPixel(background.Pixels, backgroundIndex, sourcePixel);
                }
            }
        }

        /// <summary>
        /// 判断目标坐标是否命中排除遮罩中的深色区域，用于保护摄像头等不可打印位置。
        /// </summary>
        private static bool IsExcludedByMask(
            ImagePixelData exclusionMask,
            int x,
            int y,
            int targetWidth,
            int targetHeight)
        {
            if (exclusionMask == null || targetWidth <= 0 || targetHeight <= 0)
                return false;

            int maskX = x;
            int maskY = y;
            if (exclusionMask.Width != targetWidth || exclusionMask.Height != targetHeight)
            {
                maskX = (int)((long)x * exclusionMask.Width / targetWidth);
                maskY = (int)((long)y * exclusionMask.Height / targetHeight);
            }

            if (maskX < 0 || maskX >= exclusionMask.Width || maskY < 0 || maskY >= exclusionMask.Height)
                return false;

            int pixel = exclusionMask.Pixels[maskY * exclusionMask.Width + maskX];
            byte alpha = (byte)((pixel >> 24) & 0xFF);
            if (alpha == 0)
                return false;

            byte r = (byte)((pixel >> 16) & 0xFF);
            byte g = (byte)((pixel >> 8) & 0xFF);
            byte b = (byte)(pixel & 0xFF);
            int brightness = (r + g + b) / 3;
            return brightness < 128;
        }

        /// <summary>
        /// 将单个图层的可见像素按其画布偏移混合到目标透明画布。
        /// </summary>
        private static void DrawLayerPixels(ImagePixelData background, LayerPixelData layerData)
        {
            if (background == null)
                throw new ArgumentNullException(nameof(background));
            if (layerData == null)
                throw new ArgumentNullException(nameof(layerData));
            if (layerData.OpaqueBounds == Rectangle.Empty)
                return;

            for (int localY = layerData.OpaqueBounds.Top; localY < layerData.OpaqueBounds.Bottom; localY++)
            {
                int canvasY = layerData.CanvasBounds.Top + localY;
                if (canvasY < 0 || canvasY >= background.Height)
                    continue;

                int backgroundRow = canvasY * background.Width;
                int layerRow = localY * layerData.CanvasBounds.Width;

                for (int localX = layerData.OpaqueBounds.Left; localX < layerData.OpaqueBounds.Right; localX++)
                {
                    int canvasX = layerData.CanvasBounds.Left + localX;
                    if (canvasX < 0 || canvasX >= background.Width)
                        continue;

                    int backgroundIndex = backgroundRow + canvasX;
                    int layerIndex = layerRow + localX;
                    BlendPixel(background.Pixels, backgroundIndex, layerData.Pixels[layerIndex]);
                }
            }
        }

        /// <summary>
        /// 以 SourceOver 规则混合单像素，保留半透明边缘的正确颜色和透明度。
        /// </summary>
        private static void BlendPixel(int[] backgroundPixels, int backgroundIndex, int sourcePixel)
        {
            byte sourceA = (byte)((sourcePixel >> 24) & 0xFF);
            if (sourceA == 0)
                return;

            if (sourceA == 255)
            {
                backgroundPixels[backgroundIndex] = sourcePixel;
                return;
            }

            int destPixel = backgroundPixels[backgroundIndex];
            byte destA = (byte)((destPixel >> 24) & 0xFF);
            byte sourceR = (byte)((sourcePixel >> 16) & 0xFF);
            byte sourceG = (byte)((sourcePixel >> 8) & 0xFF);
            byte sourceB = (byte)(sourcePixel & 0xFF);
            byte destR = (byte)((destPixel >> 16) & 0xFF);
            byte destG = (byte)((destPixel >> 8) & 0xFF);
            byte destB = (byte)(destPixel & 0xFF);

            float srcAlpha = sourceA / 255f;
            float dstAlpha = destA / 255f;
            float outAlpha = srcAlpha + dstAlpha * (1f - srcAlpha);

            if (outAlpha <= 0f)
                return;

            int outR = (int)Math.Round((sourceR * srcAlpha + destR * dstAlpha * (1f - srcAlpha)) / outAlpha);
            int outG = (int)Math.Round((sourceG * srcAlpha + destG * dstAlpha * (1f - srcAlpha)) / outAlpha);
            int outB = (int)Math.Round((sourceB * srcAlpha + destB * dstAlpha * (1f - srcAlpha)) / outAlpha);
            int outA = (int)Math.Round(outAlpha * 255f);

            backgroundPixels[backgroundIndex] = (outA << 24) | (outR << 16) | (outG << 8) | outB;
        }
        //遍历元素，去除空白
        /// <summary>
        /// 扫描 ARGB 像素的非透明边界，作为素材裁切和缩放的有效内容范围。
        /// </summary>
        private static Rectangle GetOpaqueBounds(int[] argbPixels, int width, int height)
        {
            int minX = width;
            int minY = height;
            int maxX = -1;
            int maxY = -1;

            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    if (((argbPixels[row + x] >> 24) & 0xFF) > 0)
                    {
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            if (maxX < minX || maxY < minY)
                return Rectangle.Empty;

            return Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        }

        /// <summary>
        /// 统计非透明像素数量，用于记录素材透明度读取是否正常。
        /// </summary>
        private static int CountOpaquePixels(int[] argbPixels)
        {
            int count = 0;
            for (int i = 0; i < argbPixels.Length; i++)
            {
                if (((argbPixels[i] >> 24) & 0xFF) > 0)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// 使用 LibTiff 的低层扫描线读取作为最终 TIFF 兼容性兜底路径。
        /// </summary>
        private static Bitmap LoadTiffToBitmap(string tifPath)
        {
            using (var tif = Tiff.Open(tifPath, "r"))
            {
                if (tif == null)
                    throw new InvalidOperationException($"无法打开TIFF文件: {tifPath}");

                int width = tif.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
                int height = tif.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
                int samples = tif.GetField(TiffTag.SAMPLESPERPIXEL)[0].ToInt();
                int bitsPerSample = 8;

                try
                {
                    var bitsField = tif.GetField(TiffTag.BITSPERSAMPLE);
                    if (bitsField != null && bitsField.Length > 0)
                        bitsPerSample = bitsField[0].ToInt();
                }
                catch { }

                if (width <= 0 || height <= 0)
                    throw new InvalidOperationException($"TIFF尺寸无效: {width}x{height}");

                int effectiveSamples = samples;
                if (samples == 4)
                {
                    int photometric = 2;
                    try
                    {
                        var photoField = tif.GetField(TiffTag.PHOTOMETRIC);
                        if (photoField != null && photoField.Length > 0)
                            photometric = photoField[0].ToInt();
                    }
                    catch { }

                    effectiveSamples = photometric == 5 ? 4 : 4;
                }
                else if (samples == 3)
                {
                    effectiveSamples = 3;
                }
                else if (samples < 3)
                {
                    effectiveSamples = 3;
                }

                var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                byte[] scanline = new byte[width * effectiveSamples];

                for (int y = 0; y < height; y++)
                {
                    tif.ReadScanline(scanline, y);

                    for (int x = 0; x < width; x++)
                    {
                        int offset = x * effectiveSamples;
                        byte r, g, b, a = 255;

                        if (effectiveSamples >= 3)
                        {
                            r = scanline[offset];
                            g = scanline[offset + 1];
                            b = scanline[offset + 2];
                        }
                        else
                        {
                            r = g = b = scanline[offset];
                        }

                        if (effectiveSamples == 4)
                        {
                            int photometric = 2;
                            try
                            {
                                var photoField = tif.GetField(TiffTag.PHOTOMETRIC);
                                if (photoField != null && photoField.Length > 0)
                                    photometric = photoField[0].ToInt();
                            }
                            catch { }

                            if (photometric == 5)
                            {
                                byte k = scanline[offset + 3];
                                a = (byte)(255 - k);
                                byte c = r, m = g, y2 = b;
                                int kk = k;
                                r = (byte)Math.Min(255, c * (255 - kk) / 255 + kk);
                                g = (byte)Math.Min(255, m * (255 - kk) / 255 + kk);
                                b = (byte)Math.Min(255, y2 * (255 - kk) / 255 + kk);
                            }
                            else
                            {
                                a = scanline[offset + 3];
                            }
                        }

                        if (samples > effectiveSamples)
                        {
                            a = scanline[offset + effectiveSamples];
                            if (a > 0) a = 255;
                        }

                        bitmap.SetPixel(x, y, Color.FromArgb(a, r, g, b));
                    }
                }

                return bitmap;
            }
        }

        /// <summary>
        /// 根据用户选择的格式保存套图结果，并仅在 TIFF 中写入额外专色通道。
        /// </summary>
        private static void SaveMergedAsFormat(Bitmap bitmap, string outputPath, string format, List<string> channelNames)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            ApplyOutputResolution(bitmap);

            switch (format.ToUpper())
            {
                case "PSD":
                    SaveAsPsd(bitmap, outputPath);
                    break;
                case "PNG":
                    bitmap.Save(outputPath, ImageFormat.Png);
                    break;
                case "JPEG":
                case "JPG":
                    var jpegEncoder = GetEncoder(ImageFormat.Jpeg);
                    var jpegParams = new EncoderParameters(1);
                    jpegParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 100L);
                    bitmap.Save(outputPath, jpegEncoder, jpegParams);
                    break;
                case "TIF":
                case "TIFF":
                    SaveAsTiffWithSpotChannels(bitmap, outputPath, channelNames);
                    break;
                default:
                    SaveAsCmykTiff(bitmap, outputPath);
                    break;
            }
        }

        /// <summary>
        /// 明确拒绝当前尚未实现的真实 PSD 导出，避免生成伪 PSD 文件。
        /// </summary>
        private static void SaveAsPsd(Bitmap bitmap, string outputPath)
        {
            throw new NotSupportedException("当前版本暂不支持真实 PSD 导出，请改用 TIF、PNG 或 JPEG。");
        }

        /// <summary>
        /// 根据通道名称列表选择普通 CMYK TIFF 或带额外通道的 TIFF 写入流程。
        /// </summary>
        private static void SaveAsTiffWithSpotChannels(Bitmap bitmap, string outputPath, List<string> channelNames)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            
            // 不添加额外通道时，直接保存为 CMYK TIFF
            if (channelNames == null || channelNames.Count == 0)
            {
                SaveAsCmykTiff(bitmap, outputPath);
                return;
            }

            // 添加额外通道时，保存为 CMYK TIFF 并附加占位通道
            SaveAsCmykTiffWithExtraChannels(bitmap, outputPath, channelNames);
        }

        /// <summary>
        /// 通过 Magick.NET 将位图转换为标准 CMYK TIFF，不附带额外通道。
        /// </summary>
        private static void SaveAsCmykTiff(Bitmap bitmap, string outputPath)
        {
            ApplyOutputResolution(bitmap);
            using (var ms = new MemoryStream())
            {
                bitmap.Save(ms, ImageFormat.Png);
                ms.Position = 0;

                using (var image = new MagickImage(ms))
                {
                    image.Alpha(AlphaOption.On);
                    image.ColorSpace = ColorSpace.CMYK;
                    image.Format = MagickFormat.Tiff;
                    image.Density = new Density(OutputDpi, OutputDpi);
                    image.Write(outputPath);
                }
            }
        }

        /// <summary>
        /// 缓存图像像素数据，避免批量任务重复读取同一模板或遮罩文件。
        /// </summary>
        private sealed class CachedImagePixelData
        {
            public long FileLength { get; }
            public DateTime LastWriteTimeUtc { get; }
            public ImagePixelData PixelData { get; }

            public CachedImagePixelData(long fileLength, DateTime lastWriteTimeUtc, ImagePixelData pixelData)
            {
                FileLength = fileLength;
                LastWriteTimeUtc = lastWriteTimeUtc;
                PixelData = pixelData ?? throw new ArgumentNullException(nameof(pixelData));
            }
        }
        // 辅助类
        class SpotColorInfo
        {
            public string Name { get; set; }
            public byte[] Color { get; set; } // RGB
            public byte Opacity { get; set; }
        }

        /// <summary>
        /// 导出带白墨、光油等额外通道的 CMYK TIFF；大图默认走逐行流式写入以控制内存。
        /// </summary>
        private static void SaveAsCmykTiffWithExtraChannels(Bitmap bitmap, string outputPath, List<string> channelNames)
        {
            if (UseStreamingTiffWriter(bitmap))
            {
                SaveAsCmykTiffWithExtraChannelsStreaming(bitmap, outputPath, channelNames);
                return;
            }

            ApplyOutputResolution(bitmap);
            int width = bitmap.Width;
            int height = bitmap.Height;
            var sourceData = ExtractPixelData(bitmap);

            bool hasTransparency = false;
            // 检查图片是否有透明或半透明区域
            for (int i = 0; i < sourceData.Pixels.Length; i++)
            {
                if (((sourceData.Pixels[i] >> 24) & 0xFF) < 255)
                {
                    hasTransparency = true;
                    break;
                }
            }
            Console.WriteLine(hasTransparency ? "图像包含透明或半透明区域，将保留透明通道。" : "图像不包含透明区域。");

            int placeholderChannelCount = channelNames.Count;
            int extraSampleCount = placeholderChannelCount + (hasTransparency ? 1 : 0);
            int totalSamples = 4 + extraSampleCount;
            var placeholderChannelNames = new List<string>();
            foreach (var name in channelNames)
            {
                placeholderChannelNames.Add(name ?? string.Empty);
            }

            using (var ms = new MemoryStream())
            {
                bitmap.Save(ms, ImageFormat.Png);
                ms.Position = 0;

                using (var image = new MagickImage(ms))
                {
                    image.Alpha(AlphaOption.Remove);
                    image.ColorSpace = ColorSpace.CMYK;

                    var pixels = image.GetPixels();
                    using (Tiff tif = Tiff.Open(outputPath, "w"))
                    {
                        tif.SetField(TiffTag.IMAGEWIDTH, width);
                        tif.SetField(TiffTag.IMAGELENGTH, height);
                        tif.SetField(TiffTag.SAMPLESPERPIXEL, totalSamples);
                        tif.SetField(TiffTag.BITSPERSAMPLE, 8);
                        tif.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);
                        tif.SetField(TiffTag.PHOTOMETRIC, Photometric.SEPARATED);
                        tif.SetField(TiffTag.INKSET, InkSet.CMYK);
                        tif.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
                        tif.SetField(TiffTag.COMPRESSION, Compression.LZW);
                        tif.SetField(TiffTag.RESOLUTIONUNIT, 2);
                        tif.SetField(TiffTag.XRESOLUTION, 300.0);
                        tif.SetField(TiffTag.YRESOLUTION, 300.0);
                        tif.SetField(TiffTag.ROWSPERSTRIP, height);
                        tif.SetField(TiffTag.IMAGEDESCRIPTION,
                            $"Placeholder TIFF extra channels: {string.Join(", ", placeholderChannelNames)}");

                        var photoshopChannelNames = new List<string>();
                        if (hasTransparency)
                            photoshopChannelNames.Add("Alpha");
                        photoshopChannelNames.AddRange(placeholderChannelNames);
                        TryWritePhotoshopChannelNames(tif, photoshopChannelNames);

                        if (extraSampleCount > 0)
                        {
                            short[] extraSamples = new short[extraSampleCount];
                            int extraIndex = 0;
                            if (hasTransparency)
                                extraSamples[extraIndex++] = (short)ExtraSample.UNASSALPHA;

                            while (extraIndex < extraSampleCount)
                                extraSamples[extraIndex++] = (short)ExtraSample.UNSPECIFIED;

                            tif.SetField(TiffTag.EXTRASAMPLES, extraSampleCount, extraSamples);
                        }

                        var scanline = new byte[width * totalSamples];
                        for (int y = 0; y < height; y++)
                        {
                            int sourceRow = y * width;
                            for (int x = 0; x < width; x++)
                            {
                                int pixelIndex = sourceRow + x;
                                int destIdx = x * totalSamples;
                                var pixel = pixels.GetPixel(x, y);
                                byte alpha = (byte)((sourceData.Pixels[pixelIndex] >> 24) & 0xFF);
                                // 当前打印流程以黑色表示上专色、白色表示不上专色。相反则是 byte placeholderValue = alpha;下面同理
                                byte placeholderValue = (byte)(255 - alpha);

                                scanline[destIdx + 0] = pixel.GetChannel(0);
                                scanline[destIdx + 1] = pixel.GetChannel(1);
                                scanline[destIdx + 2] = pixel.GetChannel(2);
                                scanline[destIdx + 3] = pixel.GetChannel(3);

                                int extraChannelIndex = 4;
                                if (hasTransparency)
                                    scanline[destIdx + extraChannelIndex++] = alpha;

                                for (int i = 0; i < placeholderChannelCount; i++)
                                {
                                    scanline[destIdx + extraChannelIndex++] = placeholderValue;
                                }
                            }

                            tif.WriteScanline(scanline, y);
                        }
                    }
                }
            }

            Console.WriteLine($"已输出占位扩展通道 TIFF: {placeholderChannelCount} 个通道{(hasTransparency ? "，并保留透明通道" : string.Empty)}");
        }

        /// <summary>
        /// 逐行写入 CMYK 与额外通道，避免一次性构建整张大图的像素数组导致内存溢出。
        /// </summary>
        private static void SaveAsCmykTiffWithExtraChannelsStreaming(Bitmap bitmap, string outputPath, List<string> channelNames)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            ApplyOutputResolution(bitmap);
            int width = bitmap.Width;
            int height = bitmap.Height;
            var placeholderChannelNames = (channelNames ?? new List<string>())
                .Select(name => name ?? string.Empty)
                .ToList();

            bool hasTransparency = HasTransparentPixelsStreaming(bitmap);
            int placeholderChannelCount = placeholderChannelNames.Count;
            int extraSampleCount = placeholderChannelCount + (hasTransparency ? 1 : 0);
            int totalSamples = 4 + extraSampleCount;

            using (Tiff tif = Tiff.Open(outputPath, "w"))
            {
                tif.SetField(TiffTag.IMAGEWIDTH, width);
                tif.SetField(TiffTag.IMAGELENGTH, height);
                tif.SetField(TiffTag.SAMPLESPERPIXEL, totalSamples);
                tif.SetField(TiffTag.BITSPERSAMPLE, 8);
                tif.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);
                tif.SetField(TiffTag.PHOTOMETRIC, Photometric.SEPARATED);
                tif.SetField(TiffTag.INKSET, InkSet.CMYK);
                tif.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
                tif.SetField(TiffTag.COMPRESSION, Compression.LZW);
                tif.SetField(TiffTag.RESOLUTIONUNIT, 2);
                tif.SetField(TiffTag.XRESOLUTION, 300.0);
                tif.SetField(TiffTag.YRESOLUTION, 300.0);
                tif.SetField(TiffTag.ROWSPERSTRIP, Math.Min(height, 128));
                tif.SetField(TiffTag.IMAGEDESCRIPTION,
                    $"Placeholder TIFF extra channels: {string.Join(", ", placeholderChannelNames)}");

                var photoshopChannelNames = new List<string>();
                if (hasTransparency)
                    photoshopChannelNames.Add("Alpha");
                photoshopChannelNames.AddRange(placeholderChannelNames);
                TryWritePhotoshopChannelNames(tif, photoshopChannelNames);

                if (extraSampleCount > 0)
                {
                    short[] extraSamples = new short[extraSampleCount];
                    int extraIndex = 0;
                    if (hasTransparency)
                        extraSamples[extraIndex++] = (short)ExtraSample.UNASSALPHA;

                    while (extraIndex < extraSampleCount)
                        extraSamples[extraIndex++] = (short)ExtraSample.UNSPECIFIED;

                    tif.SetField(TiffTag.EXTRASAMPLES, extraSampleCount, extraSamples);
                }

                WriteCmykTiffScanlinesStreaming(tif, bitmap, width, height, totalSamples, placeholderChannelCount, hasTransparency);
            }
        }

        /// <summary>
        /// 决定是否启用流式 TIFF 写入；当前统一启用以保证大图输出稳定。
        /// </summary>
        private static bool UseStreamingTiffWriter(Bitmap bitmap)
        {
            return true;
        }

        /// <summary>
        /// 按行检测 Alpha，决定 TIFF 是否需要额外写入透明通道。
        /// </summary>
        private static bool HasTransparentPixelsStreaming(Bitmap bitmap)
        {
            Rectangle bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData bitmapData = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            try
            {
                int stride = bitmapData.Stride;
                int rowBytes = bitmap.Width * 4;
                byte[] row = new byte[rowBytes];

                for (int y = 0; y < bitmap.Height; y++)
                {
                    IntPtr rowPtr = stride >= 0
                        ? IntPtr.Add(bitmapData.Scan0, y * stride)
                        : IntPtr.Add(bitmapData.Scan0, (bitmap.Height - 1 - y) * -stride);
                    Marshal.Copy(rowPtr, row, 0, rowBytes);

                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        if (row[x * 4 + 3] < 255)
                            return true;
                    }
                }

                return false;
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }
        }

        /// <summary>
        /// 将 ARGB 源图逐行转换为 CMYK，并同步写入 Alpha 和普通专色通道。
        /// 专色通道采用黑色上墨、白色不上墨的当前打印极性。
        /// </summary>
        private static void WriteCmykTiffScanlinesStreaming(
            Tiff tif,
            Bitmap bitmap,
            int width,
            int height,
            int totalSamples,
            int placeholderChannelCount,
            bool hasTransparency)
        {
            Rectangle bounds = new Rectangle(0, 0, width, height);
            BitmapData bitmapData = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            try
            {
                int stride = bitmapData.Stride;
                int sourceRowBytes = width * 4;
                byte[] sourceRow = new byte[sourceRowBytes];
                byte[] scanline = new byte[width * totalSamples];

                for (int y = 0; y < height; y++)
                {
                    IntPtr rowPtr = stride >= 0
                        ? IntPtr.Add(bitmapData.Scan0, y * stride)
                        : IntPtr.Add(bitmapData.Scan0, (height - 1 - y) * -stride);
                    Marshal.Copy(rowPtr, sourceRow, 0, sourceRowBytes);

                    for (int x = 0; x < width; x++)
                    {
                        int sourceIdx = x * 4;
                        int destIdx = x * totalSamples;
                        byte b = sourceRow[sourceIdx + 0];
                        byte g = sourceRow[sourceIdx + 1];
                        byte r = sourceRow[sourceIdx + 2];
                        byte alpha = sourceRow[sourceIdx + 3];

                        // TIFF 仍保留 Alpha，但 CMYK 底色统一垫白，避免透明的 RGB(0,0,0)
                        // 被转换为 K=255，导致不识别透明度的预览或 RIP 输出黑底。
                        if (alpha < byte.MaxValue)
                        {
                            r = CompositeChannelOverWhite(r, alpha);
                            g = CompositeChannelOverWhite(g, alpha);
                            b = CompositeChannelOverWhite(b, alpha);
                        }

                        ConvertRgbToCmyk(r, g, b,
                            out scanline[destIdx + 0],
                            out scanline[destIdx + 1],
                            out scanline[destIdx + 2],
                            out scanline[destIdx + 3]);

                        int extraChannelIndex = 4;
                        if (hasTransparency)
                            scanline[destIdx + extraChannelIndex++] = alpha;

                        for (int i = 0; i < placeholderChannelCount; i++)
                        {
                            // 当前打印流程以黑色表示上专色、白色表示不上专色。
                            scanline[destIdx + extraChannelIndex++] = (byte)(255 - alpha);
                        }
                    }

                    tif.WriteScanline(scanline, y);
                }
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }
        }

        /// <summary>
        /// 将一个半透明色彩分量按白色底板预合成，透明度仍由 TIFF Alpha 通道单独保存。
        /// </summary>
        /// <summary>
        /// 将半透明颜色分量与白色底板预合成，避免透明 RGB 零值在 CMYK 中转成黑底。
        /// </summary>
        private static byte CompositeChannelOverWhite(byte channel, byte alpha)
        {
            return (byte)((channel * alpha + 255 * (255 - alpha) + 127) / 255);
        }

        /// <summary>
        /// 使用基础 RGB 转 CMYK 公式生成 8 位 CMYK 分量，供底层 TIFF 扫描线写入。
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
        /// 将 Photoshop 通道名称资源写入 TIFF；失败时不阻断图像导出。
        /// </summary>
        private static void TryWritePhotoshopChannelNames(Tiff tif, List<string> extraChannelNames)
        {
            if (tif == null || extraChannelNames == null || extraChannelNames.Count == 0)
                return;

            try
            {
                byte[] imageResourceBlock = BuildPhotoshopImageResourceBlock(extraChannelNames);
                if (imageResourceBlock.Length > 0)
                    tif.SetField((TiffTag)34377, imageResourceBlock.Length, imageResourceBlock);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"写入 Photoshop 通道名称资源失败，继续输出 TIFF: {ex.Message}");
            }
        }

        /// <summary>
        /// 为外部流式 TIFF 输出写入 Photoshop 可识别的额外通道名称。
        /// </summary>
        public static void WritePhotoshopChannelNames(Tiff tif, List<string> channelNames)
        {
            TryWritePhotoshopChannelNames(tif, channelNames);
        }

        /// <summary>
        /// 组装 TIFF 私有标签 34377 所需的 Photoshop 图像资源块。
        /// </summary>
        private static byte[] BuildPhotoshopImageResourceBlock(List<string> extraChannelNames)
        {
            var block = new List<byte>();
            block.AddRange(BuildPascalChannelNamesResource(extraChannelNames));
            return block.ToArray();
        }

        /// <summary>
        /// 按 Photoshop 0x03EE 资源格式，将通道名编码为连续 Pascal 字符串。
        /// </summary>
        private static byte[] BuildPascalChannelNamesResource(List<string> extraChannelNames)
        {
            var data = new List<byte>();
            foreach (string name in extraChannelNames)
            {
                string channelName = name ?? string.Empty;
                byte[] nameBytes = Encoding.Default.GetBytes(channelName);
                if (nameBytes.Length > 255)
                    throw new InvalidOperationException($"通道名称过长: {channelName}");

                data.Add((byte)nameBytes.Length);
                data.AddRange(nameBytes);
            }

            return BuildPhotoshopResourceBlock(0x03EE, data);
        }

        /// <summary>
        /// 为指定资源编号封装 8BIM 头、空名称、数据长度及偶数字节对齐。
        /// </summary>
        private static byte[] BuildPhotoshopResourceBlock(int resourceId, List<byte> data)
        {
            var block = new List<byte>();
            block.AddRange(new byte[] { (byte)'8', (byte)'B', (byte)'I', (byte)'M' });
            block.Add((byte)((resourceId >> 8) & 0xFF));
            block.Add((byte)(resourceId & 0xFF));
            block.Add(0x00);
            if ((block.Count % 2) != 0)
                block.Add(0x00);

            int length = data.Count;
            block.Add((byte)((length >> 24) & 0xFF));
            block.Add((byte)((length >> 16) & 0xFF));
            block.Add((byte)((length >> 8) & 0xFF));
            block.Add((byte)(length & 0xFF));
            block.AddRange(data);
            if ((data.Count % 2) != 0)
                block.Add(0x00);

            return block.ToArray();
        }

        /// <summary>
        /// 从系统图像编码器中查找与目标格式匹配的编码器。
        /// </summary>
        private static ImageCodecInfo GetEncoder(ImageFormat format)
        {
            var codecs = ImageCodecInfo.GetImageEncoders();
            foreach (var codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                    return codec;
            }
            return null;
        }

        /// <summary>
        /// 生成用于界面预览的位图副本，避免源文件句柄长期被占用。
        /// </summary>
        public static Bitmap GeneratePreview(string imagePath)
        {
            try
            {
                return LoadBitmapPreserveColor(imagePath);
            }
            catch (Exception ex)
            {
                // 不要吞掉异常：打包后预检失败时，真正原因（缺 Magick.Native-*.dll、
                // 缺 Aspose/LibTiff 程序集、Aspose license、x86/x64 架构不匹配等）就在这里。
                // 记录完整异常类型与堆栈，便于在打包环境下定位“IDE 正常但发布后失败”的问题。
                try
                {
                    string logPath = Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "GeneratePreview_error.log");
                    File.AppendAllText(logPath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] imagePath={imagePath}{Environment.NewLine}" +
                        $"{ex.GetType().FullName}: {ex.Message}{Environment.NewLine}" +
                        $"{ex.StackTrace}{Environment.NewLine}" +
                        (ex.InnerException != null
                            ? $"INNER {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}{Environment.NewLine}{ex.InnerException.StackTrace}{Environment.NewLine}"
                            : string.Empty) +
                        new string('-', 60) + Environment.NewLine);
                }
                catch { /* 日志写入失败时不影响主流程 */ }

                Console.WriteLine($"GeneratePreview 失败: {ex.GetType().FullName}: {ex.Message}");
                return null;
            }
        }
        /// <summary>
        /// 将输出位图统一设置为 300 DPI，保证毫米尺寸与打印流程一致。
        /// </summary>
        private static void ApplyOutputResolution(Bitmap bitmap)
        {
            if (bitmap == null)
                return;

            bitmap.SetResolution(OutputDpi, OutputDpi);
        }
    }
}
