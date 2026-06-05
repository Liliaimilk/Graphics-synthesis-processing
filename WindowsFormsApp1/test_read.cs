using System;
using System.Drawing;
using System.Drawing.Imaging;
using BitMiracle.LibTiff;
using BitMiracle.LibTiff.Classic;

class Test {
    static void Main() {
        string templateFile = @"C:\Users\Administrator\Pictures\eg\太空壳-苹果13mini-透明.tif";
        string materialFile = @"C:\Users\Administrator\Pictures\eg\粉色海浪玫瑰.tif";
        
        using (Tiff tif = Tiff.Open(templateFile, "r")) {
            int width = tif.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
            int height = tif.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
            int samples = tif.GetField(TiffTag.SAMPLESPERPIXEL)[0].ToInt();
            int bitsPerSample = tif.GetField(TiffTag.BITSPERSAMPLE)[0].ToInt();
            
            Console.WriteLine($"模版: {width}x{height}, samples={samples}, bits={bitsPerSample}");
            
            // 读取第一行像素看颜色值
            byte[] buffer = new byte[width * samples];
            tif.ReadScanline(buffer, 0);
            
            int nonTransparent = 0;
            int transparent = 0;
            for (int x = 0; x < Math.Min(100, width); x++) {
                int offset = x * samples;
                if (samples >= 4) {
                    byte a = buffer[offset + 3];
                    if (a > 0) nonTransparent++;
                    else transparent++;
                }
            }
            Console.WriteLine($"前100像素: 透明={transparent}, 非透明={nonTransparent}");
        }
        
        using (Tiff tif = Tiff.Open(materialFile, "r")) {
            int width = tif.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
            int height = tif.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
            int samples = tif.GetField(TiffTag.SAMPLESPERPIXEL)[0].ToInt();
            
            Console.WriteLine($"素材: {width}x{height}, samples={samples}");
        }
    }
}
