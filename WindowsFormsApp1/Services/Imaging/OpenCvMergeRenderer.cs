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
