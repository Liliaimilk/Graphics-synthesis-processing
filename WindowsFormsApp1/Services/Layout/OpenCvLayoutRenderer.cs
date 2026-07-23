using OpenCvSharp;
using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace WindowsFormsApp1
{
    /// <summary>
    /// 使用 OpenCV 完成排版素材的高质量缩放和像素复制。
    /// TIFF 通道读取与最终写出仍由 LibTiff 流程负责。
    /// </summary>
    internal static class OpenCvLayoutRenderer
    {
        /// <summary>
        /// 将素材缩放到目标矩形后复制到当前条带。版式格子不重叠，因此可直接复制 BGRA 像素。
        /// </summary>
        public static void DrawIntoStrip(Bitmap source, Bitmap strip, Rectangle targetOnCanvas, int stripTop)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (strip == null)
                throw new ArgumentNullException(nameof(strip));
            if (targetOnCanvas.Width <= 0 || targetOnCanvas.Height <= 0)
                return;

            Rectangle localTarget = targetOnCanvas;
            localTarget.Y -= stripTop;
            Rectangle visibleTarget = Rectangle.Intersect(new Rectangle(0, 0, strip.Width, strip.Height), localTarget);
            if (visibleTarget.Width <= 0 || visibleTarget.Height <= 0)
                return;

            Rectangle sourceBounds = new Rectangle(0, 0, source.Width, source.Height);
            Rectangle stripBounds = new Rectangle(0, 0, strip.Width, strip.Height);
            BitmapData sourceData = source.LockBits(sourceBounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData stripData = strip.LockBits(stripBounds, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);

            try
            {
                using (var sourceMat = Mat.FromPixelData(source.Height, source.Width, MatType.CV_8UC4, sourceData.Scan0, sourceData.Stride))
                using (var scaledMat = new Mat())
                using (var stripMat = Mat.FromPixelData(strip.Height, strip.Width, MatType.CV_8UC4, stripData.Scan0, stripData.Stride))
                {
                    Cv2.Resize(sourceMat, scaledMat, new OpenCvSharp.Size(targetOnCanvas.Width, targetOnCanvas.Height),
                        0, 0, InterpolationFlags.Lanczos4);

                    int sourceX = visibleTarget.Left - localTarget.Left;
                    int sourceY = visibleTarget.Top - localTarget.Top;
                    var sourceRect = new OpenCvSharp.Rect(sourceX, sourceY, visibleTarget.Width, visibleTarget.Height);
                    var destinationRect = new OpenCvSharp.Rect(visibleTarget.Left, visibleTarget.Top, visibleTarget.Width, visibleTarget.Height);
                    using (var sourceRegion = new Mat(scaledMat, sourceRect))
                    using (var destinationRegion = new Mat(stripMat, destinationRect))
                    {
                        sourceRegion.CopyTo(destinationRegion);
                    }
                }
            }
            finally
            {
                strip.UnlockBits(stripData);
                source.UnlockBits(sourceData);
            }
        }
    }
}
