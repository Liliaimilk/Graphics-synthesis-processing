using System;
using System.Drawing;
using BitMiracle.LibTiff;
using BitMiracle.LibTiff.Classic;

class TestCheck {
    static void Main() {
        string outputFile = @"D:\matrials\3-save\太空壳-苹果13mini-透明-粉色海浪玫瑰.tif";
        
        using (Tiff tif = Tiff.Open(outputFile, "r")) {
            int width = tif.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
            int height = tif.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
            int samples = tif.GetField(TiffTag.SAMPLESPERPIXEL)[0].ToInt();
            
            Console.WriteLine("输出: " + width + "x" + height + ", samples=" + samples);
            
            byte[] buffer = new byte[width * samples];
            
            // 中间行
            tif.ReadScanline(buffer, height/2);
            int transparent = 0, nonTransparent = 0, allWhite = 0;
            for (int x = 0; x < width; x++) {
                int offset = x * samples;
                byte a = samples >= 4 ? buffer[offset + 3] : (byte)255;
                if (a == 0) transparent++;
                else {
                    nonTransparent++;
                    if (samples >= 3 && buffer[offset] > 200 && buffer[offset+1] > 200 && buffer[offset+2] > 200)
                        allWhite++;
                }
            }
            Console.WriteLine("中间行: 透明=" + transparent + ", 非透明=" + nonTransparent + ", 全白=" + allWhite);
        }
    }
}
