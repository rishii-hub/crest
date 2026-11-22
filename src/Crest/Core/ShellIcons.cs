using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Crest.Core
{
    /// <summary>High-quality Explorer thumbnails/icons for any path via IShellItemImageFactory.</summary>
    public static class ShellIcons
    {
        static readonly Dictionary<string, ImageSource?> Cache = new Dictionary<string, ImageSource?>(StringComparer.OrdinalIgnoreCase);

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SIZE { public int cx, cy; }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; public uint c0, c1, c2, c3; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [MarshalAs(UnmanagedType.Interface, IidParameterIndex = 2)] out IShellItemImageFactory ppv);

        [DllImport("gdi32.dll")] static extern int GetObject(IntPtr h, int size, ref BITMAP bm);
        [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, [Out] byte[] bits, ref BITMAPINFO bi, uint usage);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

        public static ImageSource? Get(string path, int size = 96)
        {
            var key = path + "|" + size;
            if (Cache.TryGetValue(key, out var cached)) return cached;
            ImageSource? img = null;
            try { img = FromShell(path, size); } catch (Exception ex) { Log.Error("shellicon", ex); }
            if (img == null) img = FromAssociated(path);
            Cache[key] = img;
            return img;
        }

        public static void Forget(string path)
        {
            var dead = new List<string>();
            foreach (var k in Cache.Keys) if (k.StartsWith(path + "|", StringComparison.OrdinalIgnoreCase)) dead.Add(k);
            foreach (var k in dead) Cache.Remove(k);
        }

        static ImageSource? FromShell(string path, int size)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return null;
            SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out var factory);
            try
            {
                if (factory.GetImage(new SIZE { cx = size, cy = size }, 0, out var hbm) != 0 || hbm == IntPtr.Zero) return null;
                try { return FromHBitmap(hbm); }
                finally { DeleteObject(hbm); }
            }
            finally { Marshal.ReleaseComObject(factory); }
        }

        static BitmapSource? FromHBitmap(IntPtr hbm)
        {
            var bm = new BITMAP();
            if (GetObject(hbm, Marshal.SizeOf<BITMAP>(), ref bm) == 0) return null;
            int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
            if (w <= 0 || h <= 0) return null;
            var bi = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h,
                    biPlanes = 1, biBitCount = 32, biCompression = 0
                }
            };
            var px = new byte[w * h * 4];
            var hdc = GetDC(IntPtr.Zero);
            try { if (GetDIBits(hdc, hbm, 0, (uint)h, px, ref bi, 0) == 0) return null; }
            finally { ReleaseDC(IntPtr.Zero, hdc); }

            bool anyAlpha = false;
            for (int i = 3; i < px.Length; i += 4) if (px[i] != 0) { anyAlpha = true; break; }
            if (!anyAlpha) for (int i = 3; i < px.Length; i += 4) px[i] = 255;

            var src = BitmapSource.Create(w, h, 96, 96, anyAlpha ? PixelFormats.Pbgra32 : PixelFormats.Bgra32, null, px, w * 4);
            src.Freeze();
            return src;
        }

        static ImageSource? FromAssociated(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon == null) return null;
                var src = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            catch { return null; }
        }
    }

    public static class ColorUtil
    {
        /// <summary>Vivid dominant colour of an image (used to tint the equalizer like macOS notch apps).</summary>
        public static Color? Dominant(BitmapSource? src)
        {
            try
            {
                if (src == null || src.PixelWidth < 2 || src.PixelHeight < 2) return null;
                var small = new TransformedBitmap(src, new ScaleTransform(24.0 / src.PixelWidth, 24.0 / src.PixelHeight));
                var conv = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
                int w = conv.PixelWidth, h = conv.PixelHeight;
                var px = new byte[w * h * 4];
                conv.CopyPixels(px, w * 4, 0);
                double r = 0, g = 0, b = 0, total = 0;
                for (int i = 0; i < px.Length; i += 4)
                {
                    double B = px[i] / 255.0, G = px[i + 1] / 255.0, R = px[i + 2] / 255.0;
                    double max = Math.Max(R, Math.Max(G, B)), min = Math.Min(R, Math.Min(G, B));
                    double sat = max <= 0 ? 0 : (max - min) / max;
                    if (max < 0.25 || sat < 0.2) continue;
                    double wgt = sat * sat * max;
                    r += R * wgt; g += G * wgt; b += B * wgt; total += wgt;
                }
                if (total < 0.5) return null;
                r /= total; g /= total; b /= total;
                double m = Math.Max(r, Math.Max(g, b));
                if (m < 0.75) { double k = 0.85 / Math.Max(m, 0.01); r = Math.Min(1, r * k); g = Math.Min(1, g * k); b = Math.Min(1, b * k); }
                return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
            }
            catch { return null; }
        }
    }
}
