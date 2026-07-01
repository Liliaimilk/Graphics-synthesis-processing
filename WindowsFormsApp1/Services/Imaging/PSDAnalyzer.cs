using Aspose.Imaging;
using Aspose.Imaging.FileFormats.OpenDocument.Objects.Graphic;
using Aspose.PSD;
using Aspose.PSD.FileFormats.Psd;
using Aspose.PSD.FileFormats.Psd.Layers;
using Aspose.PSD.ImageOptions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Drawing;
using ImageMagick;
using Image = Aspose.PSD.Image;
using BitMiracle.LibTiff;
using BitMiracle.LibTiff.Classic;

namespace WindowsFormsApp1
{
    public class PSDAnalyzer
    {
        public static void AnalyzePSD(string psdPath)
        {
            Console.WriteLine($"\n========== 分析 PSD: {Path.GetFileName(psdPath)} ==========");

            using (var psdImage = (PsdImage)Image.Load(psdPath))
            {
                Console.WriteLine($"画布尺寸: {psdImage.Width} x {psdImage.Height}");
                Console.WriteLine($"图层数量: {psdImage.Layers.Length}");
                Console.WriteLine();

                for (int i = 0; i < psdImage.Layers.Length; i++)
                {
                    var layer = psdImage.Layers[i];
                    Console.WriteLine($"图层 {i}: {layer.Name}");
                    Console.WriteLine($"  - 位置: ({layer.Left}, {layer.Top}) 到 ({layer.Right}, {layer.Bottom})");
                    Console.WriteLine($"  - 尺寸: {layer.Bounds.Width} x {layer.Bounds.Height}");
                    Console.WriteLine($"  - 可见: {layer.IsVisible}");
                    Console.WriteLine($"  - 类型: {layer.GetType().Name}");
                    Console.WriteLine();
                }
            }
        }

        public static void AnalyzeAndMatchLayer(
            string templatePath,
            string materialPath,
            string outputPath,
            bool addWhiteInk = false,
            bool addVarnish = false,
            string whiteInkName = null,
            string varnishName = null)
        {
            //(PsdImage)Image.Load(materialPath) 直接读取psd
            Console.WriteLine($"模版路径：{templatePath}");
            Console.WriteLine($"素材路径：{materialPath}");
            //CovertTiffToPsdStream(materialPath);
            //using (var templatePsd = (PsdImage)Image.Load(RenameTifToPsd(templatePath)))
            //using (var materialPsd = (PsdImage)Image.Load(RenameTifToPsd(materialPath)))
            using (var templatePsd = (PsdImage)Image.Load(templatePath))
            using (var materialPsd = (PsdImage)Image.Load(materialPath))
            //using (var templatePsd = CovertTiffToPsdStream(templatePath))
            //using (var materialPsd = CovertTiffToPsdStream(materialPath))
            {
                Console.WriteLine($"图层数量：{materialPsd.Layers.Length}");
                int templateW = templatePsd.Width;
                int templateH = templatePsd.Height;

                // 遍历模板每个图层
                foreach (Layer targetLayer in templatePsd.Layers)
                {
                    if (!targetLayer.IsVisible) continue;

                    string targetName = targetLayer.Name;
                    Layer sourceLayer = FindLayerByPrefix(materialPsd.Layers, targetName);
                    if (sourceLayer == null)
                    {
                        targetLayer.IsVisible = false;
                        Console.WriteLine($"未找到素材: {targetName}");
                        continue;
                    }

                    Console.WriteLine($"\n处理: {targetName}");

                    // 目标区域
                    int tLeft = targetLayer.Left;
                    int tTop = targetLayer.Top;
                    int tW = targetLayer.Right - targetLayer.Left;
                    int tH = targetLayer.Bottom - targetLayer.Top;

                    // 源区域（在素材画布中的位置和尺寸）
                    int sLeft = sourceLayer.Left;
                    int sTop = sourceLayer.Top;
                    int sW = sourceLayer.Right - sourceLayer.Left;
                    int sH = sourceLayer.Bottom - sourceLayer.Top;

                    Console.WriteLine($"目标: {tW}x{tH} @ ({tLeft},{tTop})");
                    Console.WriteLine($"源: {sW}x{sH} @ ({sLeft},{sTop})");

                    //if (sW <= 0 || sH <= 0 || tW <= 0 || tH <= 0) continue;
                    //templatePsd.AddLayer(sourceLayer);
                    //找到目标图层在 PSD 中的索引位置
                    int targetIndex = Array.IndexOf(templatePsd.Layers, targetLayer);
                    var layersList = new List<Layer>(templatePsd.Layers);

                    layersList.Insert(targetIndex + 1, sourceLayer);
                    templatePsd.Layers = layersList.ToArray();
                    // 将模版图层与目标图层偏移一致
                    Layer copiedLayer = templatePsd.Layers[targetIndex + 1];
                    copiedLayer.IsVisible = true;
                    copiedLayer.Resize(tW, tH, Aspose.PSD.ResizeType.NearestNeighbourResample);
                    copiedLayer.Left = tLeft;
                    copiedLayer.Top = tTop;
                    copiedLayer.Right = tLeft + tW;
                    copiedLayer.Bottom = tTop + tH;
                    Console.WriteLine($"目标图层的索引：{targetIndex},{targetLayer.Name}");
                    copiedLayer.Clipping = 1;
                }
                //添加专色通道
                //AddSpotChannels(templatePsd, addWhiteInk, addVarnish, whiteInkName, varnishName);

                // 保存结果
                Console.WriteLine($"通道数量：{templatePsd.ChannelsCount}");
                Console.WriteLine("\n保存...");
                templatePsd.Save(outputPath);
                //SaveAsTiffWithSpotChannels(templatePsd, outputPath, addWhiteInk, addVarnish, whiteInkName, varnishName);
                Console.WriteLine($"完成: {outputPath}");
            }
        }

        /// <summary>
        /// 添加专色通道（占位方法）
        /// </summary>
        private static void AddSpotChannels(PsdImage psd, bool addWhiteInk, bool addVarnish, string whiteInkName, string varnishName)
        {
            Console.WriteLine($"专色通道：白墨={addWhiteInk}({whiteInkName}), 光油={addVarnish}({varnishName})");
        }

        /// <summary>
        /// 保存为带专色通道的TIFF文件
        /// RGB(3通道) + 专色通道
        /// </summary>
        private static void SaveAsTiffWithSpotChannels(PsdImage psd, string outputPath, bool addWhiteInk, bool addVarnish, string whiteInkName, string varnishName)
        {
            int width = psd.Width;
            int height = psd.Height;

            var raster = (Aspose.PSD.RasterImage)psd;
            var rect = new Aspose.PSD.Rectangle(0, 0, width, height);
            int[] argbPixels = raster.LoadArgb32Pixels(rect);

            Console.WriteLine($"读取到像素: {argbPixels.Length}, 期望: {width * height}");

            int baseSamples = 3; // RGB
            int spotCount = (addWhiteInk ? 1 : 0) + (addVarnish ? 1 : 0);
            int totalSamples = baseSamples + spotCount;

            // 专色通道名称
            List<string> spotNamesList = new List<string>();
            if (addWhiteInk) spotNamesList.Add(whiteInkName ?? "White");
            if (addVarnish) spotNamesList.Add(varnishName ?? "Varnish");

            Console.WriteLine($"画布: {width}x{height}, 通道: {totalSamples} (RGB:{baseSamples} + 专色:{spotCount})");
            Console.WriteLine($"专色名称: {string.Join(", ", spotNamesList)}");

            using (Tiff tif = Tiff.Open(outputPath, "w"))
            {
                tif.SetField(TiffTag.IMAGEWIDTH, width);
                tif.SetField(TiffTag.IMAGELENGTH, height);
                tif.SetField(TiffTag.SAMPLESPERPIXEL, totalSamples);
                tif.SetField(TiffTag.BITSPERSAMPLE, 8);
                for (int i = 1; i < totalSamples; i++)
                    tif.SetField(TiffTag.BITSPERSAMPLE, 8);
                tif.SetField(TiffTag.ORIENTATION, BitMiracle.LibTiff.Classic.Orientation.TOPLEFT);
                tif.SetField(TiffTag.PHOTOMETRIC, 2); // RGB
                tif.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
                tif.SetField(TiffTag.COMPRESSION, Compression.LZW);
                tif.SetField(TiffTag.RESOLUTIONUNIT, 2);
                tif.SetField(TiffTag.XRESOLUTION, 300);
                tif.SetField(TiffTag.YRESOLUTION, 300);

                // 设置专色通道类型 - 一次性设置所有额外通道
                if (spotCount > 0)
                {
                    short[] extraSamples = new short[spotCount];
                    for (int i = 0; i < spotCount; i++)
                        extraSamples[i] = 2; // 2 = unassociated alpha (spot color)
                    tif.SetField(TiffTag.EXTRASAMPLES, spotCount, extraSamples);
                    Console.WriteLine($"EXTRASAMPLES设置: {spotCount}个通道");

                    // 设置 InkNames (Tag 270)
                    string inkNamesStr = string.Join("\0", spotNamesList) + "\0";
                    byte[] inkNamesBytes = System.Text.Encoding.ASCII.GetBytes(inkNamesStr);
                    tif.SetField((TiffTag)270, inkNamesBytes);
                    Console.WriteLine($"InkNames已写入: {inkNamesStr}");
                }

                byte[] scanline = new byte[width * totalSamples];
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int srcIdx = y * width + x;
                        int argb = argbPixels[srcIdx];

                        int destIdx = x * totalSamples;
                        // RGB顺序
                        scanline[destIdx + 0] = (byte)((argb >> 16) & 0xFF); // R
                        scanline[destIdx + 1] = (byte)((argb >> 8) & 0xFF);  // G
                        scanline[destIdx + 2] = (byte)(argb & 0xFF);          // B

                        // 专色通道填充为0 (无色)
                        int spotIdx = baseSamples;
                        if (addWhiteInk) { scanline[destIdx + spotIdx] = 0; spotIdx++; }
                        if (addVarnish) { scanline[destIdx + spotIdx] = 0; }
                    }
                    tif.WriteScanline(scanline, y);
                }
            }
            Console.WriteLine("TIFF保存完成(带专色通道)");
        }

     

        private static Layer FindLayerByPrefix(Layer[] layers, string prefix)
        {
            foreach (var layer in layers)
            {
                if (!string.IsNullOrEmpty(layer.Name) && layer.Name.StartsWith(prefix))
                    return layer;
            }
            return null;
        }

        public static String RenameTifToPsd(string tifFilePath)
        {
            try
            {
                if (!File.Exists(tifFilePath))
                {
                    Console.WriteLine($"文件不存在: {tifFilePath}");
                    return null;
                }

                string psdFilePath = Path.ChangeExtension(tifFilePath, ".psd");
                File.Copy(tifFilePath, psdFilePath, overwrite: true);

                Console.WriteLine($"转换成功: {psdFilePath}");
                return psdFilePath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"转换失败: {ex.Message}");
                return null;
            }
        }

        public static string FlattenTiffToSingleLayerTiff(string tiffPath, string outputPath = null)
        {
            if (!File.Exists(tiffPath))
                throw new FileNotFoundException($"文件不存在: {tiffPath}");

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                string directory = Path.GetDirectoryName(tiffPath);
                string fileName = Path.GetFileNameWithoutExtension(tiffPath);
                outputPath = Path.Combine(directory, fileName + "_flattened.tif");
            }

            EnsureMagickNetInitialized();

            using (var collection = new MagickImageCollection(tiffPath))
            {
                if (collection.Count == 0)
                    throw new InvalidOperationException("TIFF中没有可合并的图层");

                for (int i = 0; i < collection.Count; i++)
                {
                    collection[i].Compose = CompositeOperator.Over;
                    collection[i].Page = new MagickGeometry(0, 0, collection[i].Width, collection[i].Height);
                }

                using (var flattenedImage = collection.Flatten())
                {
                    flattenedImage.Format = MagickFormat.Tiff;
                    flattenedImage.Write(outputPath);
                }
            }

            Console.WriteLine($"单图层TIFF已输出: {outputPath}");
            return outputPath;
        }

        private static void EnsureMagickNetInitialized()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(baseDirectory) && Directory.Exists(baseDirectory))
            {
                MagickNET.Initialize(baseDirectory);
            }
        }

        private static void CovertTiffToPsdStream(string tiffPath)
        {
            if (!File.Exists(tiffPath))
                throw new FileNotFoundException($"文件不存在: {tiffPath}");

            //var memoryStream = new MemoryStream();
            try
            {
                using (var collection = new MagickImageCollection(tiffPath))
                {
                    // 如果文件读取出来没有任何帧，说明文件损坏或路径错误

                    if (collection.Count == 0) return;
                    for (int i = 0; i < collection.Count; i++)
                    {
                        // 强制指定图层的复合/混合动作为 Over（对应 PS 的 Normal 正常模式）
                        collection[i].Compose = CompositeOperator.Over;

                        // 关键：重置图层位置几何信息。有些 TIF 会带偏移量，导致 PS 解析为错误的混合边界
                        collection[i].Page = new MagickGeometry(0, 0, collection[i].Width, collection[i].Height);


                    }

                    // 针对 PSD 的规范优化：
                    // PSD 规范要求第 0 帧（第一层）是所有图层的"拼合预览图"，后面的才是独立图层
                    using (var flattenedPreview = collection.Flatten())
                    {
                        // 将拼合的预览图作为底图插入到最前面
                        collection.Insert(0, flattenedPreview);

                        // 保存时指定格式为 Psd，Magick.NET 会自动构建 PSD 树结构
                        collection.Write("D:\\matrials\\3-save\\素材.psd", MagickFormat.Psd);
                    }
                }


            }
            catch (Exception ex)
            {
                throw new Exception($"TIFF转PSD失败: {ex.Message}", ex);
            }
        }
    }
}