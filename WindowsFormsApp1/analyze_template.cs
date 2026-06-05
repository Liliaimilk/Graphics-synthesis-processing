using System;
using System.IO;
using BitMiracle.LibTiff;
using BitMiracle.LibTiff.Classic;

class AnalyzeTemplate {
    static void Main() {
        string path = @"D:\matrials\1-mod\太空壳-苹果13mini-透明.tif";
        
        Console.WriteLine($"分析文件: {path}");
        Console.WriteLine($"大小: {new FileInfo(path).Length / 1024.0 / 1024.0:F2} MB\n");
        
        using (Tiff tif = Tiff.Open(path, "r")) {
            // 基本信息
            int width = tif.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
            int height = tif.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
            int samples = tif.GetField(TiffTag.SAMPLESPERPIXEL)[0].ToInt();
            int bits = tif.GetField(TiffTag.BITSPERSAMPLE)[0].ToInt();
            int photometric = tif.GetField(TiffTag.PHOTOMETRIC)[0].ToInt();
            
            Console.WriteLine("=== 基本信息 ===");
            Console.WriteLine($"尺寸: {width}x{height}");
            Console.WriteLine($"SamplesPerPixel: {samples}");
            Console.WriteLine($"BitsPerSample: {bits}");
            Console.WriteLine($"PhotometricInterpretation: {photometric}");
            
            // 检查ExtraSamples
            try {
                var extraField = tif.GetField(TiffTag.EXTRASAMPLES);
                if (extraField != null) {
                    Console.WriteLine($"ExtraSamples: 存在");
                    for (int i = 0; i < extraField.Count; i++) {
                        Console.WriteLine($"  额外通道{i}: {extraField[i].ToInt()}");
                    }
                } else {
                    Console.WriteLine("ExtraSamples: 无");
                }
            } catch (Exception ex) {
                Console.WriteLine($"ExtraSamples: 读取失败 - {ex.Message}");
            }
            
            // 检查Alpha
            try {
                var alphaField = tif.GetField(TiffTag.EXTRASAMPLES);
                if (alphaField != null) {
                    Console.WriteLine($"Alpha信息: 有{alphaField.Count}个额外通道");
                }
            } catch {}
            
            // 读取像素分析
            Console.WriteLine("\n=== 像素分析 ===");
            int bytesPerPixel = samples * bits / 8;
            byte[] buffer = new byte[width * bytesPerPixel];
            
            int whiteCount = 0, blackCount = 0, transparentCount = 0, coloredCount = 0;
            
            // 检查几个关键行
            int[] checkLines = { 0, height/4, height/2, height*3/4, height-1 };
            foreach (int line in checkLines) {
                tif.ReadScanline(buffer, line);
                int w = 0, b = 0, c = 0;
                
                for (int x = 0; x < width; x++) {
                    int offset = x * bytesPerPixel;
                    byte r = buffer[offset];
                    byte g = samples >= 2 ? buffer[offset + 1] : r;
                    byte bl = samples >= 3 ? buffer[offset + 2] : r;
                    
                    if (r > 200 && g > 200 && bl > 200) w++;
                    else if (r < 50 && g < 50 && bl < 50) b++;
                    else c++;
                }
                Console.WriteLine($"行{line}: 白={w}, 黑={b}, 彩色={c}");
            }
            
            // 分析中间区域的颜色分布
            Console.WriteLine("\n=== 颜色分布统计(中间区域) ===");
            tif.ReadScanline(buffer, height/2);
            
            long rSum = 0, gSum = 0, bSum = 0;
            int count = 0;
            
            for (int x = 0; x < width; x++) {
                int offset = x * bytesPerPixel;
                byte r = buffer[offset];
                byte g = samples >= 2 ? buffer[offset + 1] : r;
                byte bl = samples >= 3 ? buffer[offset + 2] : r;
                
                rSum += r;
                gSum += g;
                bSum += bl;
                count++;
            }
            
            Console.WriteLine($"中间行平均: R={rSum/count:F0}, G={gSum/count:F0}, B={bSum/count:F0}");
        }
    }
}
