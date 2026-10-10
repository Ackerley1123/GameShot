using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GameShot
{
    internal sealed class CapturedImage : IDisposable
    {
        internal Bitmap Bitmap;
        internal string Backend;
        internal string Warning;
        public void Dispose() { if (Bitmap != null) { Bitmap.Dispose(); Bitmap = null; } }
    }

    internal static class CaptureEngine
    {
        internal static CapturedImage Read(Rectangle bounds, bool directX)
        {
            bounds = Rectangle.Intersect(bounds, SystemInformation.VirtualScreen);
            if (bounds.Width < 1 || bounds.Height < 1) throw new InvalidOperationException("截图区域不在可见桌面内。");
            var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
            var backends = new HashSet<string>();
            var warnings = new List<string>();
            try
            {
                using (var canvas = Graphics.FromImage(bitmap))
                {
                    canvas.Clear(Color.Black);
                    // 逐个显示器合成，可以处理左侧负坐标以及不同 GPU 上的显示器。
                    foreach (var screen in Screen.AllScreens)
                    {
                        Rectangle intersection = Rectangle.Intersect(bounds, screen.Bounds);
                        if (intersection.IsEmpty) continue;
                        Bitmap frame = null;
                        try
                        {
                            if (directX)
                            {
                                try
                                {
                                    bool protectedContent;
                                    frame = DesktopDuplication.Capture(screen.DeviceName, out protectedContent);
                                    if (frame.Size != screen.Bounds.Size) throw new InvalidOperationException("显示器模式发生变化，请重试。");
                                    if (protectedContent) warnings.Add("部分画面受系统保护，可能被遮黑。");
                                    backends.Add("DirectX");
                                }
                                catch (Exception ex)
                                {
                                    if (frame != null) { frame.Dispose(); frame = null; }
                                    warnings.Add("DirectX 不可用，已使用普通捕获（" + ex.Message + "）。");
                                }
                            }
                            if (frame == null)
                            {
                                frame = new Bitmap(screen.Bounds.Width, screen.Bounds.Height, PixelFormat.Format32bppRgb);
                                CopyDesktop(frame, screen.Bounds.Location);
                                backends.Add("普通捕获");
                            }
                            Rectangle source = new Rectangle(intersection.X - screen.Bounds.X, intersection.Y - screen.Bounds.Y, intersection.Width, intersection.Height);
                            Rectangle destination = new Rectangle(intersection.X - bounds.X, intersection.Y - bounds.Y, intersection.Width, intersection.Height);
                            canvas.DrawImage(frame, destination, source, GraphicsUnit.Pixel);
                        }
                        finally { if (frame != null) frame.Dispose(); }
                    }
                }
                return new CapturedImage { Bitmap = bitmap, Backend = string.Join(" + ", backends), Warning = string.Join(" ", warnings.Distinct()) };
            }
            catch { bitmap.Dispose(); throw; }
        }

        private static void CopyDesktop(Bitmap image, Point origin)
        {
            IntPtr desktop = Native.GetDC(IntPtr.Zero);
            if (desktop == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                using (var graphics = Graphics.FromImage(image))
                {
                    IntPtr destination = graphics.GetHdc();
                    try
                    {
                        // CAPTUREBLT 保留分层窗口；直接调用 BitBlt，避免 .NET 的枚举校验拒绝组合标志。
                        if (!Native.BitBlt(destination, 0, 0, image.Width, image.Height, desktop, origin.X, origin.Y, 0x40CC0020))
                            throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                    finally { graphics.ReleaseHdc(destination); }
                }
            }
            finally { Native.ReleaseDC(IntPtr.Zero, desktop); }
        }

        internal static bool MostlyBlack(Bitmap image)
        {
            // 仅作提醒：暗场游戏也可能是正常画面，不能因为黑图就丢掉用户截图。
            int bright = 0;
            for (int y = 0; y < 24; y++)
                for (int x = 0; x < 32; x++)
                {
                    Color pixel = image.GetPixel(Math.Min(image.Width - 1, image.Width * x / 32), Math.Min(image.Height - 1, image.Height * y / 24));
                    if (pixel.R > 12 || pixel.G > 12 || pixel.B > 12) bright++;
                }
            return bright < 2;
        }
    }
}
