using BitMiracle.LibTiff;
using BitMiracle.LibTiff.Classic;
using ImageMagick;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
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

        public static void ProcessTifMode(
            string templateTifPath,
            string materialTifPath,
            string outputPath,
            string format,
            Action<string> progressCallback,
            string whiteInkChannelName = null,
            string varnishChannelName = null,
            int offsetX = 0,
            int offsetY = 0,
            TemplateCompositeMode compositeMode = TemplateCompositeMode.Standard,
            string exclusionMaskPath = null)
        {
            try
            {
                progressCallback?.Invoke("正在读取模板...");
                var backgroundData = LoadImagePixelData(templateTifPath);

                progressCallback?.Invoke("正在读取素材...");
                var foregroundData = LoadImagePixelData(materialTifPath);

                ImagePixelData exclusionMaskData = null;
                if (compositeMode == TemplateCompositeMode.FullBleed && !string.IsNullOrWhiteSpace(exclusionMaskPath))
                {
                    progressCallback?.Invoke("正在读取摄像头遮罩...");
                    exclusionMaskData = LoadImagePixelData(exclusionMaskPath);
                    Console.WriteLine($"摄像头遮罩路径: {exclusionMaskPath}");
                    Console.WriteLine($"摄像头遮罩尺寸: W={exclusionMaskData.Width}, H={exclusionMaskData.Height}");
                }

                var opaqueBounds = GetOpaqueBounds(foregroundData.Pixels, foregroundData.Width, foregroundData.Height);
                int opaquePixels = CountOpaquePixels(foregroundData.Pixels);
                Console.WriteLine($"素材非透明像素: {opaquePixels}");
                Console.WriteLine($"素材内容区域: X={opaqueBounds.X}, Y={opaqueBounds.Y}, W={opaqueBounds.Width}, H={opaqueBounds.Height}");

                if (opaqueBounds == Rectangle.Empty)
                {
                    progressCallback?.Invoke("素材没有可用的非透明内容");
                    using (var emptyBitmap = CreateBitmapFromArgbPixels(backgroundData.Width, backgroundData.Height, backgroundData.Pixels))
                    {
                        SaveMergedAsFormat(emptyBitmap, outputPath, format);
                    }
                    return;
                }

                if (opaqueBounds.Width == foregroundData.Width && opaqueBounds.Height == foregroundData.Height)
                {
                    Console.WriteLine("警告: 素材读取后非透明区域等于整张画布，说明 TIFF 透明信息可能已在读取阶段丢失。");
                }

                progressCallback?.Invoke(compositeMode == TemplateCompositeMode.FullBleed ? "正在满版合成图像..." : "正在标准套图合成图像...");
                if (compositeMode == TemplateCompositeMode.FullBleed)
                {
                    var templateBounds = GetOpaqueBounds(backgroundData.Pixels, backgroundData.Width, backgroundData.Height);
                    Console.WriteLine($"模板手机壳区域: X={templateBounds.X}, Y={templateBounds.Y}, W={templateBounds.Width}, H={templateBounds.Height}");
                    if (templateBounds == Rectangle.Empty)
                        throw new InvalidOperationException("模板没有可用于满版模式的非透明手机壳区域");
                    if (templateBounds.Width == backgroundData.Width && templateBounds.Height == backgroundData.Height)
                        Console.WriteLine("警告: 模板非透明区域等于整张画布，满版模式会铺满整张画布。请确认模板透明信息是否读取正确。");

                    DrawFullBleedPixels(backgroundData, foregroundData, templateBounds, opaqueBounds, exclusionMaskData);
                }
                else
                {
                    Console.WriteLine($"素材贴图偏移: offsetX={offsetX}, offsetY={offsetY}");
                    DrawNonTransparentPixels(backgroundData, foregroundData, opaqueBounds, offsetX, offsetY);
                }

                using (var mergedBitmap = CreateBitmapFromArgbPixels(backgroundData.Width, backgroundData.Height, backgroundData.Pixels))
                {
                    progressCallback?.Invoke("正在保存结果...");
                    SaveMergedAsFormat(mergedBitmap, outputPath, format, whiteInkChannelName, varnishChannelName);
                }

                progressCallback?.Invoke("完成");
            }
            catch (OutOfMemoryException ex)
            {
                progressCallback?.Invoke($"图片太大或 TIFF 格式不兼容：{ex.Message}");
                throw;
            }
        }

        private static Bitmap LoadBitmapPreserveColor(string imagePath)
        {
            var pixelData = LoadImagePixelData(imagePath);
            return CreateBitmapFromArgbPixels(pixelData.Width, pixelData.Height, pixelData.Pixels);
        }

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

        private static ImagePixelData TryLoadTiffRasterPixelData(string tifPath)
        {
            try
            {
                using (var image = Aspose.PSD.Image.Load(tifPath))
                {
                    var raster = image as Aspose.PSD.RasterImage;
                    if (raster != null)
                    {
                        var rect = new Aspose.PSD.Rectangle(0, 0, raster.Width, raster.Height);
                        return new ImagePixelData(raster.Width, raster.Height, raster.LoadArgb32Pixels(rect));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Aspose.PSD 读取 TIFF 失败，转入备用路径: {ex.Message}");
            }

            try
            {
                using (var image = new MagickImage(tifPath))
                {
                    image.Alpha(AlphaOption.On);
                    using (var transparentPng = image.Clone())
                    {
                        transparentPng.Format = MagickFormat.Png32;
                        using (var ms = new MemoryStream())
                        {
                            transparentPng.Write(ms);
                            ms.Position = 0;
                            using (var pngBitmap = new Bitmap(ms))
                            using (var bitmap = new Bitmap(pngBitmap))
                            {
                                return ExtractPixelData(bitmap);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Magick.NET 读取 TIFF 失败，转入 LibTiff 备用路径: {ex.Message}");
            }

            using (var bitmap = LoadTiffToBitmap(tifPath))
            {
                return ExtractPixelData(bitmap);
            }
        }

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

        private static void DrawNonTransparentPixels(
            ImagePixelData background,
            ImagePixelData foreground,
            Rectangle opaqueBounds,
            int offsetX,
            int offsetY)
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

        private static void DrawFullBleedPixels(
            ImagePixelData background,
            ImagePixelData foreground,
            Rectangle templateBounds,
            Rectangle materialBounds,
            ImagePixelData exclusionMask)
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

            for (int destY = templateBounds.Top; destY < templateBounds.Bottom; destY++)
            {
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

                    int sourceX = materialBounds.Left + (int)Math.Floor((destX - drawLeft) / scale);
                    int sourceY = materialBounds.Top + (int)Math.Floor((destY - drawTop) / scale);
                    if (sourceX < materialBounds.Left || sourceX >= materialBounds.Right ||
                        sourceY < materialBounds.Top || sourceY >= materialBounds.Bottom ||
                        sourceX < 0 || sourceX >= foreground.Width ||
                        sourceY < 0 || sourceY >= foreground.Height)
                    {
                        continue;
                    }

                    int sourcePixel = foreground.Pixels[sourceY * foreground.Width + sourceX];
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

        private static void SaveMergedAsFormat(Bitmap bitmap, string outputPath, string format, string whiteInkChannelName = null, string varnishChannelName = null)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

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
                    SaveAsTiffWithSpotChannels(bitmap, outputPath, whiteInkChannelName, varnishChannelName);
                    break;
                default:
                    SaveAsCmykTiff(bitmap, outputPath);
                    break;
            }
        }

        private static void SaveAsPsd(Bitmap bitmap, string outputPath)
        {
            bitmap.Save(outputPath.Replace(".psd", ".png"), ImageFormat.Png);
        }

        private static void SaveAsTiffWithSpotChannels(Bitmap bitmap, string outputPath, string whiteInkChannelName = null, string varnishChannelName = null)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            bool addWhiteInk = !string.IsNullOrWhiteSpace(whiteInkChannelName);
            bool addVarnish = !string.IsNullOrWhiteSpace(varnishChannelName);

            if (!addWhiteInk && !addVarnish)
            {
                SaveAsCmykTiff(bitmap, outputPath);
                return;
            }

            SaveAsCmykTiffWithExtraChannels(bitmap, outputPath, addWhiteInk, addVarnish, whiteInkChannelName, varnishChannelName);
        }

        private static void SaveAsCmykTiff(Bitmap bitmap, string outputPath)
        {
            using (var ms = new MemoryStream())
            {
                bitmap.Save(ms, ImageFormat.Png);
                ms.Position = 0;

                using (var image = new MagickImage(ms))
                {
                    image.Alpha(AlphaOption.On);
                    image.ColorSpace = ColorSpace.CMYK;
                    image.Format = MagickFormat.Tiff;
                    image.Write(outputPath);
                }
            }
        }

        private static void SaveAsCmykTiffWithExtraChannels(Bitmap bitmap, string outputPath, bool addWhiteInk, bool addVarnish, string whiteInkChannelName, string varnishChannelName)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;
            var sourceData = ExtractPixelData(bitmap);

            bool hasTransparency = false;
            for (int i = 0; i < sourceData.Pixels.Length; i++)
            {
                if (((sourceData.Pixels[i] >> 24) & 0xFF) < 255)
                {
                    hasTransparency = true;
                    break;
                }
            }

            int placeholderChannelCount = (addWhiteInk ? 1 : 0) + (addVarnish ? 1 : 0);
            int extraSampleCount = placeholderChannelCount + (hasTransparency ? 1 : 0);
            int totalSamples = 4 + extraSampleCount;
            var placeholderChannelNames = new List<string>();
            if (addWhiteInk)
                placeholderChannelNames.Add(whiteInkChannelName);
            if (addVarnish)
                placeholderChannelNames.Add(varnishChannelName);

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
                                byte placeholderValue = alpha;

                                scanline[destIdx + 0] = pixel.GetChannel(0);
                                scanline[destIdx + 1] = pixel.GetChannel(1);
                                scanline[destIdx + 2] = pixel.GetChannel(2);
                                scanline[destIdx + 3] = pixel.GetChannel(3);

                                int extraChannelIndex = 4;
                                if (hasTransparency)
                                    scanline[destIdx + extraChannelIndex++] = alpha;

                                if (addWhiteInk)
                                    scanline[destIdx + extraChannelIndex++] = placeholderValue;

                                if (addVarnish)
                                    scanline[destIdx + extraChannelIndex++] = placeholderValue;
                            }

                            tif.WriteScanline(scanline, y);
                        }
                    }
                }
            }

            Console.WriteLine($"已输出占位双通道 TIFF: White={whiteInkChannelName}, Varnish={varnishChannelName}{(hasTransparency ? "，并保留透明通道" : string.Empty)}");
        }

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

        private static byte[] BuildPhotoshopImageResourceBlock(List<string> extraChannelNames)
        {
            var block = new List<byte>();
            block.AddRange(BuildPascalChannelNamesResource(extraChannelNames));
            block.AddRange(BuildUnicodeChannelNamesResource(extraChannelNames));
            return block.ToArray();
        }

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

        private static byte[] BuildUnicodeChannelNamesResource(List<string> extraChannelNames)
        {
            var data = new List<byte>();
            foreach (string name in extraChannelNames)
            {
                string channelName = name ?? string.Empty;
                byte[] nameBytes = Encoding.BigEndianUnicode.GetBytes(channelName);
                int charCount = channelName.Length;

                data.Add((byte)((charCount >> 24) & 0xFF));
                data.Add((byte)((charCount >> 16) & 0xFF));
                data.Add((byte)((charCount >> 8) & 0xFF));
                data.Add((byte)(charCount & 0xFF));
                data.AddRange(nameBytes);
            }

            return BuildPhotoshopResourceBlock(0x0415, data);
        }

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

        public static Bitmap GeneratePreview(string imagePath)
        {
            try
            {
                return LoadBitmapPreserveColor(imagePath);
            }
            catch
            {
                return null;
            }
        }
    }
}
