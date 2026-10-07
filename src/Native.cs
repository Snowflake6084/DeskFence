using System;
using System.Runtime.InteropServices;

namespace DeskFence
{
    [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IImageList
    {
        [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);
        [PreserveSig] int ReplaceIcon(int i, IntPtr hicon, ref int pi);
        [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
        [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
        [PreserveSig] int AddMasked(IntPtr hbmImage, int crMask, ref int pi);
        [PreserveSig] int Draw(IntPtr pimldp);
        [PreserveSig] int Remove(int i);
        [PreserveSig] int GetIcon(int i, int flags, ref IntPtr picon);
    }

    static class Native
    {
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WM_WINDOWPOSCHANGING = 0x0046;
        public const uint SWP_NOZORDER = 0x0004;
        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        public const int GWL_HWNDPARENT = -8;
        public const int ULW_ALPHA = 2;
        public const byte AC_SRC_ALPHA = 1;

        public const uint SHGFI_ICON = 0x100;
        public const uint SHGFI_SYSICONINDEX = 0x4000;
        public const uint SHGFI_OVERLAYINDEX = 0x40;
        public const uint SHGFI_USEFILEATTRIBUTES = 0x10;
        public const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        public const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
        public const int SHIL_SMALL = 1;
        public const int SHIL_EXTRALARGE = 2;
        public const int ILD_TRANSPARENT = 1;

        public const int SHCNE_ATTRIBUTES = 0x00000800;
        public const int SHCNE_UPDATEITEM = 0x00002000;
        public const int SHCNE_UPDATEDIR = 0x00001000;
        public const uint SHCNF_PATHW = 0x0005;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int x; public int y; public POINT(int x, int y) { this.x = x; this.y = y; } }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int cx; public int cy; public SIZE(int cx, int cy) { this.cx = cx; this.cy = cy; } }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION { public byte BlendOp; public byte BlendFlags; public byte SourceConstantAlpha; public byte AlphaFormat; }

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPOS { public IntPtr hwnd; public IntPtr hwndInsertAfter; public int x; public int y; public int cx; public int cy; public uint flags; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ICONINFO { public bool fIcon; public int xHotspot; public int yHotspot; public IntPtr hbmMask; public IntPtr hbmColor; }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAP { public int bmType; public int bmWidth; public int bmHeight; public int bmWidthBytes; public ushort bmPlanes; public ushort bmBitsPixel; public IntPtr bmBits; }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public int biSize; public int biWidth; public int biHeight; public short biPlanes; public short biBitCount;
            public int biCompression; public int biSizeImage; public int biXPelsPerMeter; public int biYPelsPerMeter; public int biClrUsed; public int biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public uint[] bmiColors;
        }

        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll")] public static extern int GetDeviceCaps(IntPtr hdc, int index);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT pt);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        /// <summary>找到桌面图标所在的 SHELLDLL_DefView</summary>
        public static IntPtr FindDesktopView()
        {
            IntPtr progman = FindWindow("Progman", null);
            IntPtr dv = progman == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            IntPtr w = IntPtr.Zero;
            while (dv == IntPtr.Zero)
            {
                w = FindWindowEx(IntPtr.Zero, w, "WorkerW", null);
                if (w == IntPtr.Zero) break;
                dv = FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
            }
            return dv;
        }

        public delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmod, WinEventProc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
        public const uint EVENT_SYSTEM_FOREGROUND = 0x0003, EVENT_SYSTEM_MINIMIZESTART = 0x0016, WINEVENT_OUTOFCONTEXT = 0;
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        public const uint GW_HWNDNEXT = 2, GW_HWNDPREV = 3;
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_NOOWNERZORDER = 0x200;

        /// <summary>
        /// 桌面图标实际所在的顶层窗口。平时是 Progman；
        /// 用了幻灯片壁纸/动态壁纸等之后，图标会被搬到一个 WorkerW 里，这时要以它为准。
        /// </summary>
        public static IntPtr FindDesktopHost()
        {
            IntPtr dv = FindDesktopView();
            if (dv != IntPtr.Zero)
            {
                IntPtr root = GetAncestor(dv, 2 /*GA_ROOT*/);
                if (root != IntPtr.Zero) return root;
            }
            return FindWindow("Progman", null);
        }

        /// <summary>让桌面刷新（相当于在桌面按 F5）</summary>
        public static void RefreshDesktop()
        {
            try
            {
                IntPtr dv = FindDesktopView();
                if (dv != IntPtr.Zero) PostMessage(dv, 0x0111 /*WM_COMMAND*/, new IntPtr(0x7103), IntPtr.Zero);
            }
            catch { }
        }
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder buf, int max);
        public static string ClassOf(IntPtr h)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder(256);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        public static void SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value)
        {
            if (IntPtr.Size == 8) SetWindowLongPtr64(hWnd, nIndex, value);
            else SetWindowLong32(hWnd, nIndex, value.ToInt32());
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);
        [DllImport("shell32.dll")]
        public static extern int SHGetImageList(int iImageList, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IImageList ppv);
        [DllImport("shell32.dll")]
        public static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("user32.dll")] public static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);
        [DllImport("gdi32.dll")] public static extern int GetObject(IntPtr hgdiobj, int cbBuffer, ref BITMAP lpvObject);
        [DllImport("gdi32.dll")] public static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines, [Out] int[] lpvBits, ref BITMAPINFO lpbi, uint uUsage);

        public static void NotifyShell(string path)
        {
            IntPtr p = IntPtr.Zero;
            try
            {
                p = Marshal.StringToHGlobalUni(path);
                SHChangeNotify(SHCNE_ATTRIBUTES, SHCNF_PATHW, p, IntPtr.Zero);
                SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW, p, IntPtr.Zero);
            }
            catch { }
            finally { if (p != IntPtr.Zero) Marshal.FreeHGlobal(p); }
            try
            {
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    IntPtr d = Marshal.StringToHGlobalUni(dir);
                    try { SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW, d, IntPtr.Zero); }
                    finally { Marshal.FreeHGlobal(d); }
                }
            }
            catch { }
        }
    }
}
