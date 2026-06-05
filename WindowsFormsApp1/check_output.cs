using System;
using System.Drawing;
using BitMiracle.LibTiff;
using BitMiracle.LibTiff.Classic;

class CheckOutput {
    static void Main() {
        string outputFile = @"D:\matrials\3-save\太空壳-苹果13mini-透明-粉色海浪玫瑰.tif";
        
        using (Tiff tif = Tiff.Open(outputFile, "r")) {
            int width = tif.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
            int height = tif.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
            int samples = tif.GetField(TiffTag.SAMPLESPERPIXEL)[0].ToInt();
            int bits = tif.GetField(TiffTag.BITSPERSAMPLE)[0].ToInt();
            
            Console.WriteLine($"输出文件: {width}x{height}, 通道={samples}, 位深={bits}");
            
            byte[] buffer = new byte[width * samples];
            
            // 检查不同位置的像素
            int[] testYs = { 0, height/4, height/2, height*3/4, height-1 };
            foreach (int testY in testYs) {
                tif.ReadScanline(buffer, testY);
                int transparent = 0, nonTransparent = 0;
                for (int x = 0; x < width; x++) {
                    int offset = x * samples;
                    byte a = samples >= 4 ? buffer[offset + 3] : (byte)255;
                    if (a == 0) transparent++;
                    else nonTransparent++;
                }
                Console.WriteLine($"行{testY}: 透明={transparent}, 非透明={nonTransparent}");
            }
        }
    }
}
