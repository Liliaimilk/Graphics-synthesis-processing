using OpenCvSharp;
using System;
using System.Runtime.InteropServices;
using Rectangle = System.Drawing.Rectangle;

namespace WindowsFormsApp1
{
    /// <summary>
    /// 套图流程的 OpenCV 像素加速器。
    /// 仅负责 ARGB 素材区域缩放，模板遮罩和 Alpha 贴合仍由现有业务规则控制。
    /// </summary>
    internal static class OpenCvMergeRenderer
    {
        /// <summary>
        /// 使用 OpenCV 读取普通 8 位 BGR/BGRA 图像，并转换为项目统一使用的 ARGB 整数像素。
        /// CMYK、专色和分层 TIFF 由调用方继续走 LibTiff/Aspose 兼容链路。
        /// </summary>
        public static bool TryReadArgbPixels(string imagePath, bool forceOpaqueFourChannel, out int width, out int height, out int[] pixels)
        {
            width = 0;
            height = 0;
            pixels = null;

            try
            {
                using (var source = Cv2.ImRead(imagePath, ImreadModes.Unchanged))
                {
                    if (source.Empty() || source.Depth() != MatType.CV_8U ||
                        (source.Channels() != 1 && source.Channels() != 3 && source.Channels() != 4))
                        return false;

                    width = source.Cols;
                    height = source.Rows;
                    int channelCount = source.Channels();
                    int rowByteCount = checked(width * channelCount);
                    var sourceBytes = new byte[checked(rowByteCount * height)];

                    for (int row = 0; row < height; row++)
                    {
                        IntPtr rowAddress = IntPtr.Add(source.Data, checked((int)(row * source.Step())));
                        Marshal.Copy(rowAddress, sourceBytes, row * rowByteCount, rowByteCount);
                    }

                    pixels = new int[checked(width * height)];
                    int sourceOffset = 0;
                    for (int index = 0; index < pixels.Length; index++)
                    {
                        byte blue;
                        byte green;
                        byte red;
                        byte alpha = byte.MaxValue;
                        if (channelCount == 1)
                        {
                            blue = sourceBytes[sourceOffset++];
                            green = blue;
                            red = blue;
                        }
                        else
                        {
                            blue = sourceBytes[sourceOffset++];
                            green = sourceBytes[sourceOffset++];
                            red = sourceBytes[sourceOffset++];
                            if (channelCount == 4)
                            {
                                byte fourthChannel = sourceBytes[sourceOffset++];
                                // 部分 CMYK TIFF 经 OpenCV 解码后已是 BGR 颜色，但第 4 通道不是可靠的 Alpha。
                                // 测试模式只强制其不透明，避免将彩色区域错误裁成透明；不再重复做 CMYK 颜色换算。
                                alpha = forceOpaqueFourChannel ? byte.MaxValue : fourthChannel;
                            }
                        }

                        pixels[index] = (alpha << 24) | (red << 16) | (green << 8) | blue;
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OpenCV 读取图像失败，改用兼容读取链路: {ex.Message}");
                width = 0;
                height = 0;
                pixels = null;
                return false;
            }
        }

        /// <summary>
        /// 将 ARGB 像素数组指定区域缩放为目标尺寸。
        /// 使用最近邻插值，保持原满版流程按像素取样的边缘和透明度极性。
        /// </summary>
        public static int[] ResizeArgbRegion(
            int[] sourcePixels,
            int sourceWidth,
            int sourceHeight,
            Rectangle sourceBounds,
            int targetWidth,
            int targetHeight)
        {
            if (sourcePixels == null)
                throw new ArgumentNullException(nameof(sourcePixels));
            if (sourceWidth <= 0 || sourceHeight <= 0 || sourcePixels.Length < checked(sourceWidth * sourceHeight))
                throw new ArgumentException("源像素数组尺寸无效。", nameof(sourcePixels));
            if (sourceBounds.Width <= 0 || sourceBounds.Height <= 0 || targetWidth <= 0 || targetHeight <= 0)
                return Array.Empty<int>();

            int[] targetPixels = new int[checked(targetWidth * targetHeight)];
            GCHandle sourceHandle = GCHandle.Alloc(sourcePixels, GCHandleType.Pinned);
            GCHandle targetHandle = GCHandle.Alloc(targetPixels, GCHandleType.Pinned);
            try
            {
                using (var sourceMat = Mat.FromPixelData(sourceHeight, sourceWidth, MatType.CV_8UC4, sourceHandle.AddrOfPinnedObject()))
                using (var sourceRegion = new Mat(sourceMat, new OpenCvSharp.Rect(
                    sourceBounds.Left,
                    sourceBounds.Top,
                    sourceBounds.Width,
                    sourceBounds.Height)))
                using (var targetMat = Mat.FromPixelData(targetHeight, targetWidth, MatType.CV_8UC4, targetHandle.AddrOfPinnedObject()))
                {
                    Cv2.Resize(sourceRegion, targetMat, new OpenCvSharp.Size(targetWidth, targetHeight),
                        0, 0, InterpolationFlags.Nearest);
                }

                return targetPixels;
            }
            finally
            {
                targetHandle.Free();
                sourceHandle.Free();
            }
        }
    }
}
