using System;
using System.IO;
using System.Drawing;
using BitMiracle.LibTiff;
using BitMiracle.LibTiff.Classic;

class DebugTiff {
    static void AnalyzeFile(string path, string name) {
        Console.WriteLine($"\n===== 分析 {name} =====");
        Console.WriteLine($"路径: {path}");
        Console.WriteLine($"文件存在: {File.Exists(path)}");
        
        if (!File.Exists(path)) return;
        
        var fi = new FileInfo(path);
        Console.WriteLine($"文件大小: {fi.Length} bytes");
        
        using (Tiff tif = Tiff.Open(path, "r")) {
            if (tif == null) {
                Console.WriteLine("无法打开TIFF文件");
                return;
            }
            
            int width = tif.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
            int height = tif.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
            
            int samples = 0, bits = 0, photometric = 0, planarConfig = 0;
            try { samples = tif.GetField(TiffTag.SAMPLESPERPIXEL)[0].ToInt(); } catch {}
            try { bits = tif.GetField(TiffTag.BITSPERSAMPLE)[0].ToInt(); } catch {}
            try { photometric = tif.GetField(TiffTag.PHOTOMETRIC)[0].ToInt(); } catch {}
            try { planarConfig = tif.GetField(TiffTag.PLANARCONFIG)[0].ToInt(); } catch {}
            
            Console.WriteLine($"尺寸: {width} x {height}");
            Console.WriteLine($"通道数(SamplesPerPixel): {samples}");
            Console.WriteLine($"位深(BitsPerSample): {bits}");
            Console.WriteLine($"颜色空间(Photometric): {photometric} (2=RGB, 6=CMYK, 0=白黑)");
            Console.WriteLine($"存储方式(PlanarConfig): {planarConfig} (1=连续, 2=分离)");
            
            // 读取中间行像素分析
            byte[] buffer = new byte[width * samples * bits / 8];
            tif.ReadScanline(buffer, height / 2);
            
            // 统计像素颜色
            int pureWhite = 0, pureBlack = 0, transparent = 0, colored = 0;
            long rSum = 0, gSum = 0, bSum = 0, aSum = 0;
            
            for (int x = 0; x < Math.Min(width, 100); x++) {
                int offset = x * samples * bits / 8;
                
                if (samples >= 4) {
                    byte r = buffer[offset];
                    byte g = buffer[offset + 1];
                    byte b = buffer[offset + 2];
                    byte a = buffer[offset + 3];
                    
                    rSum += r; gSum += g; bSum += b; aSum += a;
                    
                    if (a < 10) transparent++;
                    else if (r > 200 && g > 200 && b > 200) pureWhite++;
                    else if (r < 50 && g < 50 && b < 50) pureBlack++;
                    else colored++;
                } else if (samples == 3) {
                    byte r = buffer[offset];
                    byte g = buffer[offset + 1];
                    byte b = buffer[offset + 2];
                    
                    rSum += r; gSum += g; bSum += b;
                    
                    if (r > 200 && g > 200 && b > 200) pureWhite++;
                    else if (r < 50 && g < 50 && b < 50) pureBlack++;
                    else colored++;
                }
            }
            
            Console.WriteLine($"\n前100像素统计:");
            Console.WriteLine($"  透明: {transparent}");
            Console.WriteLine($"  全白: {pureWhite}");
            Console.WriteLine($"  全黑: {pureBlack}");
            Console.WriteLine($"  有颜色: {colored}");
            
            if (samples >= 4) {
                Console.WriteLine($"\n平均颜色值: R={rSum/100} G={gSum/100} B={bSum/100} A={aSum/100}");
            } else {
                Console.WriteLine($"\n平均颜色值: R={rSum/100} G={gSum/100} B={bSum/100}");
            }
        }
    }
    
    static void Main() {
        AnalyzeFile(@"D:\matrials\1-mod\太空壳-苹果13mini-透明.tif", "模版");
        AnalyzeFile(@"D:\matrials\2-picture\粉色海浪玫瑰.tif", "素材");
    }
}
