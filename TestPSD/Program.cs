using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using ImageLockMode = System.Drawing.Imaging.ImageLockMode;
using Bitmap = System.Drawing.Bitmap;
using Rectangle = System.Drawing.Rectangle;
using Image = Aspose.PSD.Image;
using Aspose.PSD;
using Aspose.PSD.FileFormats.Psd;

namespace TestPSD
{
    class Program
    {
        static void Main(string[] args)
        {
            string templatePath = @"D:\matrials\1-mod\太空壳-苹果13mini-透明.psd";
            string layerPngPath = @"D:\matrials\output\layer_10_________.png";
            string outputPath = @"D:\matrials\output\merged.psd";
            string tempTiffPath = Path.Combine(Path.GetTempPath(), "template_with_layer.tiff");

            try
            {
                // 1. 加载模板 PSD
                Console.WriteLine("加载模板 PSD...");
                using (var templatePsd = (PsdImage)Image.Load(templatePath))
                {
                    int templateW = templatePsd.Width;
                    int templateH = templatePsd.Height;
                    Console.WriteLine($"模板尺寸: {templateW} x {templateH}");

                    // 2. 读取模板像素
                    Console.WriteLine("读取模板像素...");
                    var templateRect = new Aspose.PSD.Rectangle(0, 0, templateW, templateH);
                    var raster = (Aspose.PSD.RasterImage)templatePsd;
                    int[] templatePixels = raster.LoadArgb32Pixels(templateRect);
                    Console.WriteLine($"读取了 {templatePixels.Length} 个像素");

                    // 3. 加载 PNG
                    Console.WriteLine("加载 PNG...");
                    using (var pngBitmap = new Bitmap(layerPngPath))
                    {
                        int pngW = pngBitmap.Width;
                        int pngH = pngBitmap.Height;
                        Console.WriteLine($"PNG 尺寸: {pngW} x {pngH}");

                        // 读取 PNG 像素
                        var pngRect = new Rectangle(0, 0, pngW, pngH);
                        var pngData = pngBitmap.LockBits(pngRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                        int[] pngPixels = new int[pngW * pngH];
                        System.Runtime.InteropServices.Marshal.Copy(pngData.Scan0, pngPixels, 0, pngPixels.Length);
                        pngBitmap.UnlockBits(pngData);
                        Console.WriteLine($"读取了 {pngPixels.Length} 个 PNG 像素");

                        // 4. 计算贴合位置（居中）
                        int offsetX = (templateW - pngW) / 2;
                        int offsetY = (templateH - pngH) / 2;
                        Console.WriteLine($"贴合位置: ({offsetX}, {offsetY})");

                        // 5. 合并像素（PNG 叠加到模板上）
                        Console.WriteLine("合并像素...");
                        for (int py = 0; py < pngH; py++)
                        {
                            for (int px = 0; px < pngW; px++)
                            {
                                int destX = offsetX + px;
                                int destY = offsetY + py;

                                if (destX >= 0 && destX < templateW && destY >= 0 && destY < templateH)
                                {
                                    int srcIdx = py * pngW + px;
                                    int destIdx = destY * templateW + destX;

                                    int srcPixel = pngPixels[srcIdx];
                                    int srcA = (srcPixel >> 24) & 0xFF;

                                    // 如果 PNG 像素不透明，则覆盖
                                    if (srcA > 128)
                                    {
                                        templatePixels[destIdx] = srcPixel;
                                    }
                                }
                            }
                        }

                        // 6. 保存合并后的 PSD
                        Console.WriteLine("保存合并结果...");
                        raster.SaveArgb32Pixels(templateRect, templatePixels);
                        templatePsd.Save(outputPath);
                        Console.WriteLine($"保存到: {outputPath}");
                    }
                }

                Console.WriteLine("完成！");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"错误: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
            finally
            {
                if (File.Exists(tempTiffPath))
                    try { File.Delete(tempTiffPath); } catch { }
            }
        }
    }
}