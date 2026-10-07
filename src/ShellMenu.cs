using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DeskFence
{
    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr pchEaten, out IntPtr ppidl, IntPtr pdwAttributes);
        [PreserveSig] int EnumObjects(IntPtr hwnd, int flags, out IntPtr ppenum);
        [PreserveSig] int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        [PreserveSig] int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref uint rgfInOut);
        [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref Guid riid, IntPtr rgfReserved, out IntPtr ppv);
    }

    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref ShellMenu.CMINVOKECOMMANDINFO pici);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, StringBuilder pszName, uint cchMax);
    }

    [ComImport, Guid("000214F4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref ShellMenu.CMINVOKECOMMANDINFO pici);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, StringBuilder pszName, uint cchMax);
        [PreserveSig] int HandleMenuMsg(int uMsg, IntPtr wParam, IntPtr lParam);
    }

    [ComImport, Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref ShellMenu.CMINVOKECOMMANDINFO pici);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, StringBuilder pszName, uint cchMax);
        [PreserveSig] int HandleMenuMsg(int uMsg, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int HandleMenuMsg2(int uMsg, IntPtr wParam, IntPtr lParam, out IntPtr plResult);
    }

    /// <summary>
    /// 弹出 Windows 资源管理器里那种文件右键菜单（打开方式、发送到、复制、属性、压缩……），
    /// 并在最上面加上本程序自己的几项。
    /// </summary>
    class ShellMenu : IDisposable
    {
        public const uint FirstShellId = 0x100;

        [StructLayout(LayoutKind.Sequential)]
        public struct CMINVOKECOMMANDINFO
        {
            public int cbSize;
            public int fMask;
            public IntPtr hwnd;
            public IntPtr lpVerb;
            public IntPtr lpParameters;
            public IntPtr lpDirectory;
            public int nShow;
            public int dwHotKey;
            public IntPtr hIcon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);
        [DllImport("shell32.dll")]
        static extern int SHBindToParent(IntPtr pidl, ref Guid riid, out IntPtr ppv, out IntPtr ppidlLast);
        [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr pv);
        [DllImport("user32.dll")] public static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll")] public static extern bool DestroyMenu(IntPtr hMenu);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool AppendMenu(IntPtr hMenu, uint flags, UIntPtr id, string text);
        [DllImport("user32.dll")] public static extern int GetMenuItemCount(IntPtr hMenu);
        [DllImport("user32.dll")] public static extern uint TrackPopupMenuEx(IntPtr hmenu, uint flags, int x, int y, IntPtr hwnd, IntPtr lptpm);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public const uint MF_STRING = 0x0, MF_SEPARATOR = 0x800, MF_GRAYED = 0x1;
        const uint TPM_RETURNCMD = 0x100, TPM_RIGHTBUTTON = 0x2;
        const uint CMF_NORMAL = 0x0, CMF_CANRENAME = 0x10, CMF_EXTENDEDVERBS = 0x100;
        const uint GCS_VERBW = 0x4;

        IntPtr pidl = IntPtr.Zero;
        IntPtr folderPtr = IntPtr.Zero, menuPtr = IntPtr.Zero;
        IContextMenu menu;
        public IContextMenu2 Menu2;
        public IContextMenu3 Menu3;
        public IntPtr HMenu = IntPtr.Zero;

        /// <summary>为文件创建外壳菜单。失败时抛异常，调用方退回到简单菜单。</summary>
        public ShellMenu(string path, IntPtr owner, bool extended)
        {
            uint attrs;
            int hr = SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out attrs);
            if (hr != 0) throw new COMException("SHParseDisplayName", hr);
            Guid iidFolder = typeof(IShellFolder).GUID;
            IntPtr child;
            hr = SHBindToParent(pidl, ref iidFolder, out folderPtr, out child);
            if (hr != 0) throw new COMException("SHBindToParent", hr);
            IShellFolder folder = (IShellFolder)Marshal.GetObjectForIUnknown(folderPtr);
            Guid iidMenu = typeof(IContextMenu).GUID;
            hr = folder.GetUIObjectOf(owner, 1, new IntPtr[] { child }, ref iidMenu, IntPtr.Zero, out menuPtr);
            if (hr != 0) throw new COMException("GetUIObjectOf", hr);
            menu = (IContextMenu)Marshal.GetObjectForIUnknown(menuPtr);
            try { Menu3 = (IContextMenu3)menu; } catch { Menu3 = null; }
            if (Menu3 == null) { try { Menu2 = (IContextMenu2)menu; } catch { Menu2 = null; } }

            HMenu = CreatePopupMenu();
            this.extended = extended;
        }

        bool extended;

        /// <summary>在已经加好自己菜单项之后，把系统菜单项追加进来</summary>
        public void AddShellItems()
        {
            uint flags = CMF_NORMAL | CMF_CANRENAME | (extended ? CMF_EXTENDEDVERBS : 0);
            menu.QueryContextMenu(HMenu, (uint)GetMenuItemCount(HMenu), FirstShellId, 0x7FFF, flags);
        }

        public uint Track(IntPtr owner, int x, int y)
        {
            SetForegroundWindow(owner); // 否则点菜单外面时菜单不会关闭
            uint cmd = TrackPopupMenuEx(HMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, x, y, owner, IntPtr.Zero);
            PostMessage(owner, 0, IntPtr.Zero, IntPtr.Zero);
            return cmd;
        }

        public string VerbOf(uint cmd)
        {
            try
            {
                StringBuilder sb = new StringBuilder(256);
                if (menu.GetCommandString(new UIntPtr(cmd - FirstShellId), GCS_VERBW, IntPtr.Zero, sb, (uint)sb.Capacity) == 0)
                    return sb.ToString();
            }
            catch { }
            return "";
        }

        public void Invoke(uint cmd, IntPtr owner, string directory)
        {
            CMINVOKECOMMANDINFO ci = new CMINVOKECOMMANDINFO();
            ci.cbSize = Marshal.SizeOf(typeof(CMINVOKECOMMANDINFO));
            ci.hwnd = owner;
            ci.lpVerb = new IntPtr((int)(cmd - FirstShellId));
            ci.nShow = 1; // SW_SHOWNORMAL
            IntPtr dir = IntPtr.Zero;
            try
            {
                if (!string.IsNullOrEmpty(directory)) { dir = Marshal.StringToHGlobalAnsi(directory); ci.lpDirectory = dir; }
                menu.InvokeCommand(ref ci);
            }
            finally { if (dir != IntPtr.Zero) Marshal.FreeHGlobal(dir); }
        }

        /// <summary>子菜单（打开方式、发送到等）需要把这些消息转给系统菜单</summary>
        public bool HandleMessage(ref System.Windows.Forms.Message m)
        {
            int msg = m.Msg;
            if (msg != 0x0117 /*WM_INITMENUPOPUP*/ && msg != 0x002B /*WM_DRAWITEM*/ && msg != 0x002C /*WM_MEASUREITEM*/ && msg != 0x0120 /*WM_MENUCHAR*/)
                return false;
            try
            {
                if (Menu3 != null)
                {
                    IntPtr result;
                    if (Menu3.HandleMenuMsg2(msg, m.WParam, m.LParam, out result) == 0) { m.Result = result; return true; }
                }
                else if (Menu2 != null)
                {
                    if (Menu2.HandleMenuMsg(msg, m.WParam, m.LParam) == 0) { m.Result = IntPtr.Zero; return true; }
                }
            }
            catch { }
            return false;
        }

        public void Dispose()
        {
            if (HMenu != IntPtr.Zero) { DestroyMenu(HMenu); HMenu = IntPtr.Zero; }
            try { if (menu != null) Marshal.ReleaseComObject(menu); } catch { }
            menu = null; Menu2 = null; Menu3 = null;
            if (menuPtr != IntPtr.Zero) { Marshal.Release(menuPtr); menuPtr = IntPtr.Zero; }
            if (folderPtr != IntPtr.Zero) { Marshal.Release(folderPtr); folderPtr = IntPtr.Zero; }
            if (pidl != IntPtr.Zero) { CoTaskMemFree(pidl); pidl = IntPtr.Zero; }
        }
    }
}
