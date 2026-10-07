using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace DeskFence
{
    public class ItemData
    {
        public string Path;        // 文件在桌面上的位置（解散时放回这里）
        public string StoredPath;  // 收纳后实际所在位置；为空表示没有收纳（还在原处）
        public bool HiddenByUs;   // 是否是本程序把桌面原文件设为隐藏的（移出时只恢复这种）
        public bool AddedSystem;  // 为了在"显示隐藏的项目"开启时也能藏住，额外加了系统属性

        /// <summary>文件现在实际在哪</summary>
        public string CurrentPath()
        {
            if (!string.IsNullOrEmpty(StoredPath) && DesktopHelper.Exists(StoredPath)) return StoredPath;
            return Path;
        }

        public bool IsStoredPathOf(string p)
        {
            return !string.IsNullOrEmpty(StoredPath) && DesktopHelper.SamePath(StoredPath, p);
        }

        public bool IsStored()
        {
            return !string.IsNullOrEmpty(StoredPath) && DesktopHelper.Exists(StoredPath);
        }
    }

    public class FenceData
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Title = "新格子";
        public int X = 100, Y = 100, W = 300, H = 320;
        public bool Locked;
        public bool ListMode;
        public bool Collapsed;
        public List<ItemData> Items = new List<ItemData>();

        public int IndexOf(string path)
        {
            for (int i = 0; i < Items.Count; i++)
                if (DesktopHelper.SamePath(Items[i].Path, path)) return i;
            return -1;
        }

        /// <summary>把 from 位置的项目移动到插入点 to（to 是移动前列表里的插入位置，0..Count）</summary>
        public void MoveItem(int from, int to)
        {
            if (from < 0 || from >= Items.Count) return;
            if (to < 0) to = 0;
            if (to > Items.Count) to = Items.Count;
            ItemData it = Items[from];
            Items.RemoveAt(from);
            if (to > from) to--;
            Items.Insert(to, it);
        }
    }

    public class AppConfig
    {
        public int BgAlphaPercent = 50;     // 背景不透明度
        public int AllAlphaPercent = 100;   // 整体不透明度（含图标文字）
        public string Language = "";        // zh / en / ja，空=跟随系统
        public bool HideDesktopIcons = true;   // 从桌面收纳文件（移入格子，退出时放回）
        public bool CleanExit = true;
        public bool HideTray;                  // 隐藏托盘图标（设置从格子标题栏的齿轮进入）          // 上次是否正常退出（文件都已放回）
        public List<FenceData> Fences = new List<FenceData>();

        public static string DataDir
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeskFence"); }
        }
        public static string DefaultFile { get { return System.IO.Path.Combine(DataDir, "config.xml"); } }

        public static AppConfig Load(string file)
        {
            try
            {
                if (File.Exists(file))
                {
                    XmlSerializer xs = new XmlSerializer(typeof(AppConfig));
                    using (FileStream fs = File.OpenRead(file))
                    {
                        AppConfig c = (AppConfig)xs.Deserialize(fs);
                        if (c.Fences == null) c.Fences = new List<FenceData>();
                        foreach (FenceData f in c.Fences)
                        {
                            if (f.Items == null) f.Items = new List<ItemData>();
                            f.Items.RemoveAll(delegate(ItemData x) { return x == null || string.IsNullOrEmpty(x.Path); });
                            if (string.IsNullOrEmpty(f.Id)) f.Id = Guid.NewGuid().ToString("N");
                            if (f.Title == null) f.Title = "";
                        }
                        if (c.BgAlphaPercent < 5 || c.BgAlphaPercent > 95) c.BgAlphaPercent = 50;
                        if (c.AllAlphaPercent < 30 || c.AllAlphaPercent > 100) c.AllAlphaPercent = 100;
                        if (c.Language == null) c.Language = "";
                        return c;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("读取配置失败，已备份并使用新配置: " + ex.Message);
                try { File.Copy(file, file + ".bad_" + DateTime.Now.ToString("yyyyMMddHHmmss"), true); } catch { }
            }
            return new AppConfig();
        }

        public void Save(string file)
        {
            string dir = System.IO.Path.GetDirectoryName(file);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string tmp = file + ".tmp";
            XmlSerializer xs = new XmlSerializer(typeof(AppConfig));
            string xml;
            using (StringWriter sw = new Utf8StringWriter())
            {
                xs.Serialize(sw, this);
                xml = sw.ToString();
            }
            // 在开头写一段说明：收纳的桌面文件实际存放在哪里
            string note =
                "\r\n<!--\r\n" +
                "  DeskFence 配置文件\r\n" +
                "  收纳进格子的桌面文件，实际存放位置：\r\n" +
                "    " + Storage.Root + "\r\n" +
                "  每个文件放在一个随机名字的子文件夹里，文件名保持不变。\r\n" +
                "  下面每个 ItemData 里：Path = 文件原来在桌面上的位置；StoredPath = 现在实际所在的位置（为空表示没有收纳，还在原处）。\r\n" +
                "  想把文件拿回桌面：托盘/齿轮菜单选\"退出（文件放回桌面）\"，或在格子里右键文件选\"移出格子\"，\r\n" +
                "  也可以直接到上面的文件夹里把文件剪切出来。\r\n" +
                "-->";
            int decl = xml.IndexOf("?>");
            xml = decl >= 0 ? xml.Substring(0, decl + 2) + note + xml.Substring(decl + 2) : note.TrimStart() + "\r\n" + xml;
            File.WriteAllText(tmp, xml, new UTF8Encoding(false));
            if (File.Exists(file))
            {
                try { File.Replace(tmp, file, null); return; }
                catch { File.Copy(tmp, file, true); File.Delete(tmp); return; }
            }
            File.Move(tmp, file);
        }
    }

    /// <summary>
    /// 收纳 = 把桌面文件移到存放目录（桌面上就没有了）；解散 = 移回桌面原位置。
    /// 存放目录和桌面在同一个盘，移动只是改名，瞬间完成，不复制数据。
    /// </summary>
    static class Storage
    {
        static string root;

        public static string Root
        {
            get
            {
                if (root == null) root = PickRoot(DesktopHelper.UserDesktop);
                return root;
            }
            set { root = value; } // 测试用
        }

        static string PickRoot(string desktop)
        {
            string local = "";
            try { local = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskFence", "Store"); } catch { }
            try
            {
                if (local.Length > 0 && desktop.Length > 0 &&
                    string.Equals(System.IO.Path.GetPathRoot(local), System.IO.Path.GetPathRoot(desktop), StringComparison.OrdinalIgnoreCase))
                    return local;
            }
            catch { }
            // 桌面不在 C 盘（被改到别的盘）时，存到桌面下的隐藏文件夹，保证同盘
            string inDesk = System.IO.Path.Combine(desktop, ".deskfence");
            try
            {
                if (!Directory.Exists(inDesk))
                {
                    DirectoryInfo di = Directory.CreateDirectory(inDesk);
                    di.Attributes |= FileAttributes.Hidden | FileAttributes.System;
                }
            }
            catch { }
            return inDesk;
        }

        /// <summary>把文件从原位置移入存放目录。成功返回 null，失败返回原因。</summary>
        public static string Collect(ItemData it)
        {
            if (it.IsStored()) return null;
            it.StoredPath = null;
            string src = it.Path;
            if (!DesktopHelper.Exists(src)) return "not found";
            string dir = System.IO.Path.Combine(Root, Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                string dest = System.IO.Path.Combine(dir, System.IO.Path.GetFileName(src.TrimEnd('\\', '/')));
                if (Directory.Exists(src)) Directory.Move(src, dest);
                else File.Move(src, dest);
                it.StoredPath = dest;
                return null;
            }
            catch (Exception ex)
            {
                try { if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0) Directory.Delete(dir); } catch { }
                Log.Write("收纳失败 " + src + ": " + ex.Message);
                return ex.Message;
            }
        }

        /// <summary>把收纳的文件移回原位置（同名已存在时自动改成 "名字 (2)"）。成功返回 null。</summary>
        public static string Restore(ItemData it)
        {
            if (string.IsNullOrEmpty(it.StoredPath)) return null;
            if (!DesktopHelper.Exists(it.StoredPath)) { it.StoredPath = null; return null; }
            try
            {
                string target = UniqueTarget(it.Path);
                string dir = System.IO.Path.GetDirectoryName(target);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                if (Directory.Exists(it.StoredPath)) Directory.Move(it.StoredPath, target);
                else File.Move(it.StoredPath, target);
                string holder = System.IO.Path.GetDirectoryName(it.StoredPath);
                try { if (Directory.Exists(holder) && Directory.GetFileSystemEntries(holder).Length == 0) Directory.Delete(holder); } catch { }
                it.Path = target;
                it.StoredPath = null;
                return null;
            }
            catch (Exception ex)
            {
                Log.Write("放回桌面失败 " + it.StoredPath + " -> " + it.Path + ": " + ex.Message);
                return ex.Message;
            }
        }

        public static string UniqueTarget(string p)
        {
            if (!DesktopHelper.Exists(p)) return p;
            string dir = System.IO.Path.GetDirectoryName(p);
            string name = System.IO.Path.GetFileName(p);
            bool isDir = Directory.Exists(p);
            string stem = isDir ? name : System.IO.Path.GetFileNameWithoutExtension(name);
            string ext = isDir ? "" : System.IO.Path.GetExtension(name);
            for (int i = 2; i < 1000; i++)
            {
                string c = System.IO.Path.Combine(dir, stem + " (" + i + ")" + ext);
                if (!DesktopHelper.Exists(c)) return c;
            }
            return System.IO.Path.Combine(dir, stem + " (" + Guid.NewGuid().ToString("N").Substring(0, 6) + ")" + ext);
        }

        /// <summary>放回所有收纳的文件，返回没放回去的文件名</summary>
        public static List<string> RestoreAll(AppConfig c)
        {
            List<string> failed = new List<string>();
            foreach (FenceData f in c.Fences)
                foreach (ItemData it in f.Items)
                    if (Restore(it) != null) failed.Add(DesktopHelper.DisplayName(it.Path));
            return failed;
        }
    }

    class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding { get { return Encoding.UTF8; } }
    }

    static class Log
    {
        public static void Write(string msg)
        {
            try
            {
                Directory.CreateDirectory(AppConfig.DataDir);
                File.AppendAllText(System.IO.Path.Combine(AppConfig.DataDir, "error.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine);
            }
            catch { }
        }
    }

    static class DesktopHelper
    {
        public static string UserDesktop = SafeFolder(Environment.SpecialFolder.DesktopDirectory);
        public static string CommonDesktop = SafeFolder(Environment.SpecialFolder.CommonDesktopDirectory);

        static string SafeFolder(Environment.SpecialFolder f)
        {
            try { return Environment.GetFolderPath(f); } catch { return ""; }
        }

        static string Norm(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            try { p = System.IO.Path.GetFullPath(p); } catch { }
            return p.TrimEnd('\\', '/');
        }

        public static bool SamePath(string a, string b)
        {
            return string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsOnDesktop(string path)
        {
            string dir;
            try { dir = System.IO.Path.GetDirectoryName(Norm(path)); } catch { return false; }
            if (string.IsNullOrEmpty(dir)) return false;
            return (UserDesktop.Length > 0 && SamePath(dir, UserDesktop)) ||
                   (CommonDesktop.Length > 0 && SamePath(dir, CommonDesktop));
        }

        public static bool Exists(string p)
        {
            try { return File.Exists(p) || Directory.Exists(p); } catch { return false; }
        }

        public static bool IsHidden(string p)
        {
            try { return (File.GetAttributes(p) & FileAttributes.Hidden) != 0; } catch { return false; }
        }

        /// <summary>资源管理器是否开着"显示隐藏的项目"</summary>
        public static bool ExplorerShowsHidden()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"))
                {
                    if (k == null) return false;
                    object v = k.GetValue("Hidden");
                    return v is int && (int)v == 1;
                }
            }
            catch { return false; }
        }

        /// <summary>收纳时隐藏桌面原文件。返回 true 表示由本程序隐藏。</summary>
        public static bool HideForFence(string p, out bool addedSystem)
        {
            addedSystem = false;
            try
            {
                FileAttributes a = File.GetAttributes(p);
                if ((a & FileAttributes.Hidden) != 0) return false; // 原本就隐藏，不接管
                FileAttributes n = a | FileAttributes.Hidden;
                if (ExplorerShowsHidden() && (a & FileAttributes.System) == 0)
                {
                    // 开着"显示隐藏的项目"时，单纯隐藏还会看到半透明图标；加系统属性才能藏住
                    n |= FileAttributes.System;
                    addedSystem = true;
                }
                File.SetAttributes(p, n);
                FileAttributes after = File.GetAttributes(p);
                if ((after & FileAttributes.Hidden) == 0)
                {
                    Log.Write("隐藏没有生效: " + p + " 属性=" + after);
                    addedSystem = false;
                    return false;
                }
                try { Native.NotifyShell(p); } catch { }
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("隐藏失败 " + p + ": " + ex.Message);
                addedSystem = false;
                return false;
            }
        }

        public static void UnhideForFence(string p, bool removeSystem)
        {
            try
            {
                FileAttributes a = File.GetAttributes(p);
                FileAttributes n = a & ~FileAttributes.Hidden;
                if (removeSystem) n &= ~FileAttributes.System;
                if (n != a) File.SetAttributes(p, n);
                try { Native.NotifyShell(p); } catch { }
            }
            catch (Exception ex) { Log.Write("取消隐藏失败 " + p + ": " + ex.Message); }
        }

        /// <summary>设置/取消隐藏属性。返回 true 表示属性确实被改了。</summary>
        public static bool SetHidden(string p, bool hidden)
        {
            try
            {
                FileAttributes a = File.GetAttributes(p);
                bool now = (a & FileAttributes.Hidden) != 0;
                if (now == hidden) return false;
                File.SetAttributes(p, hidden ? (a | FileAttributes.Hidden) : (a & ~FileAttributes.Hidden));
                try { Native.NotifyShell(p); } catch { }
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("修改隐藏属性失败 " + p + ": " + ex.Message);
                return false;
            }
        }

        public static string DisplayName(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            string t = p.TrimEnd('\\', '/');
            string name = System.IO.Path.GetFileName(t);
            if (string.IsNullOrEmpty(name)) return p; // 盘符等
            string ext = System.IO.Path.GetExtension(name).ToLowerInvariant();
            if (ext == ".lnk" || ext == ".url")
                name = System.IO.Path.GetFileNameWithoutExtension(name);
            return name;
        }
    }

    /// <summary>纯布局计算（不依赖 Win32，方便测试）</summary>
    public class FenceLayout
    {
        public float S = 1f;
        public int Width, Height;
        public bool ListMode;
        public int Count;
        public int Scroll;

        public int TitleH { get { return R(30); } }
        public int Pad { get { return R(6); } }
        public int CellMinW { get { return R(80); } }
        public int CellH { get { return R(80); } }
        public int RowH { get { return R(26); } }
        public int Grip { get { return R(7); } }

        public int R(float v) { return (int)Math.Round(v * S); }

        public Rectangle Content
        {
            get { int top = TitleH + R(3); return new Rectangle(Pad, top, Math.Max(1, Width - Pad * 2), Math.Max(1, Height - top - Pad)); }
        }

        public int Cols
        {
            get { if (ListMode) return 1; return Math.Max(1, Content.Width / CellMinW); }
        }

        public int CellW { get { return Content.Width / Cols; } }
        public int ItemH { get { return ListMode ? RowH : CellH; } }

        public int ContentHeight
        {
            get { if (Count <= 0) return 0; int rows = (Count + Cols - 1) / Cols; return rows * ItemH + R(4); }
        }

        public int MaxScroll { get { return Math.Max(0, ContentHeight - Content.Height); } }

        public void ClampScroll()
        {
            if (Scroll > MaxScroll) Scroll = MaxScroll;
            if (Scroll < 0) Scroll = 0;
        }

        public Rectangle ItemRect(int i)
        {
            Rectangle c = Content;
            int cols = Cols;
            int col = i % cols, row = i / cols;
            return new Rectangle(c.X + col * CellW, c.Y + row * ItemH - Scroll, ListMode ? c.Width : CellW, ItemH);
        }

        public int HitItem(Point p)
        {
            Rectangle c = Content;
            if (!c.Contains(p)) return -1;
            for (int i = 0; i < Count; i++)
            {
                Rectangle r = ItemRect(i);
                r.Inflate(-R(2), -R(1));
                if (r.Contains(p)) return i;
            }
            return -1;
        }

        /// <summary>拖放插入位置 0..Count</summary>
        public int InsertIndex(Point p)
        {
            Rectangle c = Content;
            int y = p.Y - c.Y + Scroll;
            if (y < 0) y = 0;
            int idx;
            if (ListMode)
            {
                idx = (y + RowH / 2) / RowH;
            }
            else
            {
                int cols = Cols;
                int row = y / CellH;
                int col = (p.X - c.X + CellW / 2) / CellW;
                if (col < 0) col = 0;
                if (col > cols) col = cols;
                idx = row * cols + col;
            }
            if (idx < 0) idx = 0;
            if (idx > Count) idx = Count;
            return idx;
        }

        /// <summary>插入指示线的位置（窗口坐标）</summary>
        public Rectangle InsertMarker(int idx)
        {
            Rectangle c = Content;
            if (ListMode)
            {
                int y = c.Y + idx * RowH - Scroll;
                return new Rectangle(c.X + R(4), y - R(1), c.Width - R(8), Math.Max(2, R(2)));
            }
            int cols = Cols;
            int row = idx / cols, col = idx % cols;
            if (idx == Count && Count > 0 && col == 0) { row = (idx - 1) / cols; col = cols; }
            int x = c.X + col * CellW;
            int top = c.Y + row * CellH - Scroll;
            return new Rectangle(x - R(1), top + R(4), Math.Max(2, R(2)), CellH - R(8));
        }
    }
}
