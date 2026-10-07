using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace DeskFence
{
    static class IconCache
    {
        static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        static IImageList smallList, bigList;

        public static Bitmap Get(string path, bool big)
        {
            string key = (big ? "L|" : "S|") + path;
            Bitmap b;
            if (cache.TryGetValue(key, out b)) return b;
            b = null;
            try { b = Load(path, big); } catch (Exception ex) { Log.Write("取图标失败 " + path + ": " + ex.Message); }
            if (b == null) b = Fallback(path, big);
            cache[key] = b;
            return b;
        }

        public static void Invalidate(string path)
        {
            Remove("L|" + path);
            Remove("S|" + path);
        }

        static void Remove(string key)
        {
            Bitmap b;
            if (cache.TryGetValue(key, out b)) { cache.Remove(key); if (b != null) b.Dispose(); }
        }

        static IImageList GetList(bool big)
        {
            if (big && bigList != null) return bigList;
            if (!big && smallList != null) return smallList;
            Guid iid = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");
            IImageList il;
            int hr = Native.SHGetImageList(big ? Native.SHIL_EXTRALARGE : Native.SHIL_SMALL, ref iid, out il);
            if (hr != 0) return null;
            if (big) bigList = il; else smallList = il;
            return il;
        }

        static Bitmap Load(string path, bool big)
        {
            Native.SHFILEINFO sfi = new Native.SHFILEINFO();
            uint cb = (uint)Marshal.SizeOf(typeof(Native.SHFILEINFO));
            uint flags = Native.SHGFI_SYSICONINDEX | Native.SHGFI_ICON | Native.SHGFI_OVERLAYINDEX;
            IntPtr r = IntPtr.Zero;
            bool exists = DesktopHelper.Exists(path);
            if (exists) r = Native.SHGetFileInfo(path, 0, ref sfi, cb, flags);
            if (r == IntPtr.Zero)
            {
                uint attr = Directory.Exists(path) ? Native.FILE_ATTRIBUTE_DIRECTORY : Native.FILE_ATTRIBUTE_NORMAL;
                r = Native.SHGetFileInfo(path, attr, ref sfi, cb, flags | Native.SHGFI_USEFILEATTRIBUTES);
                if (r == IntPtr.Zero) return null;
            }
            if (sfi.hIcon != IntPtr.Zero) Native.DestroyIcon(sfi.hIcon);

            int index = sfi.iIcon & 0x00FFFFFF;
            int overlay = (sfi.iIcon >> 24) & 0xFF;
            IImageList il = GetList(big);
            if (il == null) return null;
            IntPtr hicon = IntPtr.Zero;
            int hr = il.GetIcon(index, Native.ILD_TRANSPARENT | (overlay << 8), ref hicon);
            if (hr != 0 || hicon == IntPtr.Zero) return null;
            try { return IconToBitmap(hicon); }
            finally { Native.DestroyIcon(hicon); }
        }

        /// <summary>HICON → 带透明通道的 Bitmap（Icon.ToBitmap 对某些图标会丢透明度）</summary>
        public static Bitmap IconToBitmap(IntPtr hicon)
        {
            Native.ICONINFO ii;
            if (!Native.GetIconInfo(hicon, out ii)) return null;
            IntPtr dc = IntPtr.Zero;
            try
            {
                if (ii.hbmColor == IntPtr.Zero)
                {
                    using (Icon ic = Icon.FromHandle(hicon)) return ic.ToBitmap();
                }
                Native.BITMAP bm = new Native.BITMAP();
                Native.GetObject(ii.hbmColor, Marshal.SizeOf(typeof(Native.BITMAP)), ref bm);
                int w = bm.bmWidth, h = bm.bmHeight;
                if (w <= 0 || h <= 0) return null;

                dc = Native.GetDC(IntPtr.Zero);
                int[] px = new int[w * h];
                Native.BITMAPINFO bi = NewInfo(w, h);
                if (Native.GetDIBits(dc, ii.hbmColor, 0, (uint)h, px, ref bi, 0) == 0) return null;

                bool hasAlpha = false;
                for (int i = 0; i < px.Length; i++) if ((px[i] & unchecked((int)0xFF000000)) != 0) { hasAlpha = true; break; }

                if (!hasAlpha && ii.hbmMask != IntPtr.Zero)
                {
                    int[] mask = new int[w * h];
                    Native.BITMAPINFO bi2 = NewInfo(w, h);
                    if (Native.GetDIBits(dc, ii.hbmMask, 0, (uint)h, mask, ref bi2, 0) != 0)
                    {
                        for (int i = 0; i < px.Length; i++)
                            px[i] = (mask[i] & 0xFFFFFF) != 0 ? 0 : (px[i] | unchecked((int)0xFF000000));
                    }
                    else
                    {
                        for (int i = 0; i < px.Length; i++) px[i] |= unchecked((int)0xFF000000);
                    }
                }
                else if (!hasAlpha)
                {
                    for (int i = 0; i < px.Length; i++) px[i] |= unchecked((int)0xFF000000);
                }

                Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                BitmapData data = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    for (int y = 0; y < h; y++)
                        Marshal.Copy(px, y * w, new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), w);
                }
                finally { b.UnlockBits(data); }
                return b;
            }
            finally
            {
                if (dc != IntPtr.Zero) Native.ReleaseDC(IntPtr.Zero, dc);
                if (ii.hbmColor != IntPtr.Zero) Native.DeleteObject(ii.hbmColor);
                if (ii.hbmMask != IntPtr.Zero) Native.DeleteObject(ii.hbmMask);
            }
        }

        static Native.BITMAPINFO NewInfo(int w, int h)
        {
            Native.BITMAPINFO bi = new Native.BITMAPINFO();
            bi.bmiColors = new uint[256];
            bi.bmiHeader.biSize = Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER));
            bi.bmiHeader.biWidth = w;
            bi.bmiHeader.biHeight = -h; // 自上而下
            bi.bmiHeader.biPlanes = 1;
            bi.bmiHeader.biBitCount = 32;
            bi.bmiHeader.biCompression = 0;
            return bi;
        }

        static Bitmap Fallback(string path, bool big)
        {
            try
            {
                if (File.Exists(path))
                {
                    using (Icon ic = Icon.ExtractAssociatedIcon(path))
                        if (ic != null) return ic.ToBitmap();
                }
            }
            catch { }
            int sz = big ? 48 : 16;
            Bitmap b = new Bitmap(sz, sz, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b))
            using (Brush br = new SolidBrush(Color.FromArgb(200, 200, 200, 210)))
                g.FillRectangle(br, sz / 6, sz / 6, sz * 2 / 3, sz * 2 / 3);
            return b;
        }
    }
}
