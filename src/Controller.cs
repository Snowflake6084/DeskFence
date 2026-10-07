using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Timer = System.Windows.Forms.Timer;

namespace DeskFence
{
    class Controller : ApplicationContext
    {
        public const string RestoreMutexName = "DeskFence_Restore_9F2B7C";

        public AppConfig Config;
        public float Scale = 1f;
        readonly string configFile;
        readonly List<FenceForm> forms = new List<FenceForm>();
        readonly Control ui;
        NotifyIcon tray;
        readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        Timer retryTimer;
        bool exiting;
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>仅用于生成预览图</summary>
        public Controller(AppConfig cfg, float scale)
        {
            Config = cfg; Scale = scale; configFile = Path.Combine(Path.GetTempPath(), "deskfence_preview.xml"); ui = new Control();
        }

        public Controller()
        {
            configFile = AppConfig.DefaultFile;
            ui = new Control();
            ui.CreateControl();
            IntPtr h = ui.Handle; // 确保句柄在 UI 线程创建

            try
            {
                IntPtr dc = Native.GetDC(IntPtr.Zero);
                int dpi = Native.GetDeviceCaps(dc, 88 /*LOGPIXELSX*/);
                Native.ReleaseDC(IntPtr.Zero, dc);
                if (dpi > 0) Scale = dpi / 96f;
            }
            catch { }

            // 如果上次崩溃后守护进程正在把文件放回桌面，等它做完
            using (Mutex rm = new Mutex(false, RestoreMutexName))
            {
                bool got = false;
                try { got = rm.WaitOne(30000); } catch (AbandonedMutexException) { got = true; }
                try
                {
                    bool firstRun = !File.Exists(configFile);
                    Config = AppConfig.Load(configFile);
                    T.Set(Config.Language);
                    if (Config.Fences.Count == 0)
                    {
                        FenceData f = new FenceData();
                        f.Title = T.S("defaultFirst");
                        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                        f.X = wa.X + (int)(60 * Scale); f.Y = wa.Y + (int)(60 * Scale);
                        f.W = (int)(300 * Scale); f.H = (int)(320 * Scale);
                        Config.Fences.Add(f);
                    }
                    MigrateAndReconcile();
                    if (Config.HideDesktopIcons) CollectAll(false);
                    Config.CleanExit = false;
                    Save();
                    if (firstRun || IsAutoStart()) SetAutoStart(true, true);
                }
                finally { if (got) rm.ReleaseMutex(); }
            }

            foreach (FenceData f in Config.Fences) ShowFence(f);
            BuildTray();
            StartWatchers();
            StartWatchdog();
            ListenShowTray();
            SystemEvents.SessionEnding += delegate { Save(); };

            // 收纳失败的（文件正被打开等）每 20 秒重试
            retryTimer = new Timer();
            retryTimer.Interval = 20000;
            retryTimer.Tick += delegate { if (Config.HideDesktopIcons && CollectAll(true)) RenderAll(); };
            retryTimer.Start();

            // 每 2 秒检查格子是否还在桌面上（防止被最小化、被压到桌面下面、跑到屏幕外）
            Timer guard = new Timer();
            guard.Interval = 2000;
            guard.Tick += delegate { if (!exiting) foreach (FenceForm ff in forms.ToArray()) ff.Guard(); };
            guard.Start();
            // 前台窗口变化（按 Win+D 显示桌面、点桌面等）时立刻自检，不用等 2 秒
            HookForeground();
            SystemEvents.DisplaySettingsChanged += delegate
            {
                ui.BeginInvoke((MethodInvoker)delegate { foreach (FenceForm ff in forms.ToArray()) ff.Guard(); });
            };
        }

        Native.WinEventProc winEventProc; // 必须保存引用，否则会被回收导致崩溃
        IntPtr winEventHook = IntPtr.Zero;
        bool guardPending;

        void HookForeground()
        {
            try
            {
                winEventProc = delegate(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
                {
                    if (exiting || guardPending) return;
                    guardPending = true;
                    // 等系统把"显示桌面"的动作做完再检查
                    Timer t = new Timer();
                    t.Interval = 150;
                    t.Tick += delegate
                    {
                        t.Stop(); t.Dispose();
                        guardPending = false;
                        if (!exiting) foreach (FenceForm ff in forms.ToArray()) ff.Guard();
                    };
                    t.Start();
                };
                winEventHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
                    IntPtr.Zero, winEventProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
                if (winEventHook == IntPtr.Zero) Log.Write("前台窗口监听失败，只靠 2 秒自检");
            }
            catch (Exception ex) { Log.Write("前台窗口监听失败: " + ex.Message); }
        }

        /// <summary>旧版（隐藏属性方式）迁移；修正记录与实际文件不一致的地方</summary>
        void MigrateAndReconcile()
        {
            foreach (FenceData f in Config.Fences)
                foreach (ItemData it in f.Items)
                {
                    if (it.HiddenByUs)
                    {
                        if (DesktopHelper.Exists(it.Path)) DesktopHelper.UnhideForFence(it.Path, it.AddedSystem);
                        it.HiddenByUs = false;
                        it.AddedSystem = false;
                    }
                    if (!string.IsNullOrEmpty(it.StoredPath) && !DesktopHelper.Exists(it.StoredPath))
                        it.StoredPath = null;
                }
            Native.RefreshDesktop();
        }

        /// <summary>把所有还在桌面上的项目收进格子。返回是否有变化。</summary>
        bool CollectAll(bool quiet)
        {
            bool changed = false;
            foreach (FenceData f in Config.Fences)
                foreach (ItemData it in f.Items)
                {
                    if (it.IsStored()) continue;
                    if (!DesktopHelper.IsOnDesktop(it.Path) || !DesktopHelper.Exists(it.Path)) continue;
                    if (Storage.Collect(it) == null) changed = true;
                }
            if (changed) Save();
            return changed;
        }

        void StartWatchdog()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, "--watchdog " + Process.GetCurrentProcess().Id);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process.Start(psi);
            }
            catch (Exception ex) { Log.Write("启动守护进程失败: " + ex.Message); }
        }

        // ================= 格子 =================

        FenceForm ShowFence(FenceData f)
        {
            FenceForm form = new FenceForm(this, f);
            form.HandleDestroyed += OnFenceHandleDestroyed;
            forms.Add(form);
            form.Show();
            return form;
        }

        void OnFenceHandleDestroyed(object sender, EventArgs e)
        {
            FenceForm f = sender as FenceForm;
            if (f == null || exiting || f.ClosingByApp || f.RecreatingHandle) return;
            // 资源管理器重启时，挂在桌面下的窗口会被一起销毁；稍后重新创建
            forms.Remove(f);
            FenceData data = f.Data;
            Timer t = new Timer();
            t.Interval = 2000;
            t.Tick += delegate
            {
                t.Stop(); t.Dispose();
                try { f.Dispose(); } catch { }
                if (!exiting && Config.Fences.Contains(data)) ShowFence(data);
            };
            t.Start();
        }

        public void CreateFence(Point at)
        {
            FenceData f = new FenceData();
            f.Title = T.S("defaultNew");
            f.W = (int)(260 * Scale);
            f.H = (int)(280 * Scale);
            f.X = at.X; f.Y = at.Y;
            Rectangle wa = Screen.FromPoint(at).WorkingArea;
            if (f.X + f.W > wa.Right) f.X = Math.Max(wa.X, wa.Right - f.W - 20);
            if (f.Y + f.H > wa.Bottom) f.Y = Math.Max(wa.Y, wa.Bottom - f.H - 20);
            Config.Fences.Add(f);
            Save();
            ShowFence(f);
        }

        public void DeleteFence(FenceForm form)
        {
            string msg = form.Data.Items.Count > 0
                ? T.F("deleteAskItems", form.Data.Title, form.Data.Items.Count)
                : T.F("deleteAsk", form.Data.Title);
            if (MessageBox.Show(msg, "DeskFence", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            List<string> failed = new List<string>();
            foreach (ItemData it in form.Data.Items)
                if (!ReleaseItem(it)) failed.Add(DesktopHelper.DisplayName(it.Path));
            if (failed.Count > 0)
            {
                MessageBox.Show(T.F("cantRelease", string.Join("\n", failed.ToArray())), "DeskFence");
                Save();
                return;
            }
            Config.Fences.Remove(form.Data);
            forms.Remove(form);
            if (Config.Fences.Count == 0 && Config.HideTray)
            {
                Config.HideTray = false; // 最后一个格子删掉了，托盘图标必须回来，否则找不到设置
                if (tray != null) tray.Visible = true;
            }
            form.ClosingByApp = true;
            form.Close();
            form.Dispose();
            Save();
        }

        public void Save()
        {
            try { Config.Save(configFile); }
            catch (Exception ex) { Log.Write("保存配置失败: " + ex.Message); }
        }

        public void RenderAll()
        {
            foreach (FenceForm f in forms) f.Render();
        }

        FenceForm FormOf(FenceData d)
        {
            foreach (FenceForm f in forms) if (f.Data == d) return f;
            return null;
        }

        void Notify(string text)
        {
            try { if (tray != null) tray.ShowBalloonTip(5000, "DeskFence", text, ToolTipIcon.Info); } catch { }
        }

        // ================= 项目 =================

        /// <summary>移出格子：把收纳的文件放回桌面。返回 false 表示文件正被占用、没放回去。</summary>
        public bool ReleaseItem(ItemData it)
        {
            if (!it.IsStored()) { it.StoredPath = null; return true; }
            string err = Storage.Restore(it);
            if (err == null) { IconCache.Invalidate(it.Path); return true; }
            return false;
        }

        /// <summary>移出格子并从列表删除（失败时提示，保留在格子里）</summary>
        public void RemoveItem(FenceForm src, ItemData it)
        {
            if (!src.Data.Items.Contains(it)) return;
            if (!ReleaseItem(it))
            {
                MessageBox.Show(T.F("cantRelease", DesktopHelper.DisplayName(it.Path)), "DeskFence");
                return;
            }
            src.Data.Items.Remove(it);
            src.ResetHover();
            Save();
            src.Render();
        }

        /// <summary>格子之间 / 格子内部拖动</summary>
        public void DropItem(string srcFenceId, string path, FenceForm target, int index)
        {
            FenceData src = null;
            foreach (FenceData f in Config.Fences) if (f.Id == srcFenceId) { src = f; break; }
            if (src == null) return;
            int from = src.IndexOf(path);
            if (from < 0) return;
            if (src == target.Data)
            {
                src.MoveItem(from, index);
            }
            else
            {
                ItemData it = src.Items[from];
                src.Items.RemoveAt(from);
                if (index > target.Data.Items.Count) index = target.Data.Items.Count;
                target.Data.Items.Insert(index, it);
                FenceForm sf = FormOf(src);
                if (sf != null) { sf.ResetHover(); sf.Render(); }
            }
            target.ResetHover();
            Save();
        }

        /// <summary>从桌面/资源管理器拖进来的文件</summary>
        public void DropFiles(string[] files, FenceForm target, int index)
        {
            if (index < 0 || index > target.Data.Items.Count) index = target.Data.Items.Count;
            List<string> failed = new List<string>();
            foreach (string raw in files)
            {
                if (string.IsNullOrEmpty(raw)) continue;
                string p = raw;
                if (DesktopHelper.SamePath(p, Storage.Root) || DesktopHelper.SamePath(p, AppConfig.DataDir)) continue;
                ItemData it = null;
                // 已在某个格子里：移过来
                foreach (FenceData f in Config.Fences)
                {
                    int i = f.IndexOf(p);
                    if (i >= 0)
                    {
                        it = f.Items[i];
                        if (f == target.Data && i < index) index--;
                        f.Items.RemoveAt(i);
                        if (f != target.Data) { FenceForm sf = FormOf(f); if (sf != null) { sf.ResetHover(); sf.Render(); } }
                        break;
                    }
                }
                if (it == null)
                {
                    it = new ItemData();
                    it.Path = p;
                }
                target.Data.Items.Insert(index, it);
                index++;
                if (Config.HideDesktopIcons && !it.IsStored() && DesktopHelper.IsOnDesktop(it.Path))
                {
                    if (Storage.Collect(it) != null) failed.Add(DesktopHelper.DisplayName(it.Path));
                }
            }
            target.ResetHover();
            Save();
            if (failed.Count > 0) Notify(T.F("cantCollect", string.Join("\n", failed.ToArray())));
        }

        // ================= 文件变化 =================

        void StartWatchers()
        {
            foreach (string dir in new string[] { DesktopHelper.UserDesktop, DesktopHelper.CommonDesktop })
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                try
                {
                    FileSystemWatcher w = new FileSystemWatcher(dir);
                    w.IncludeSubdirectories = false;
                    w.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite;
                    w.SynchronizingObject = ui;
                    w.Renamed += OnRenamed;
                    w.Deleted += OnChanged;
                    w.Created += OnChanged;
                    w.Changed += OnChanged;
                    w.EnableRaisingEvents = true;
                    watchers.Add(w);
                }
                catch (Exception ex) { Log.Write("监视桌面失败: " + ex.Message); }
            }
        }

        void OnRenamed(object sender, RenamedEventArgs e)
        {
            bool changed = false;
            foreach (FenceData f in Config.Fences)
                foreach (ItemData it in f.Items)
                    if (!it.IsStored() && DesktopHelper.SamePath(it.Path, e.OldFullPath))
                    {
                        IconCache.Invalidate(it.Path);
                        it.Path = e.FullPath;
                        changed = true;
                    }
            if (changed) { Save(); RenderAll(); }
        }

        void OnChanged(object sender, FileSystemEventArgs e)
        {
            foreach (FenceData f in Config.Fences)
                foreach (ItemData it in f.Items)
                    if (DesktopHelper.SamePath(it.Path, e.FullPath))
                    {
                        IconCache.Invalidate(it.Path);
                        FenceForm ff = FormOf(f);
                        if (ff != null) ff.Render();
                    }
        }

        // ================= 托盘 =================

        void BuildTray()
        {
            tray = new NotifyIcon();
            tray.Icon = MakeTrayIcon();
            tray.Text = T.S("trayTip");
            if (Config.Fences.Count == 0) Config.HideTray = false; // 没有格子时必须留个入口
            tray.Visible = !Config.HideTray;
            tray.ContextMenuStrip = new ContextMenuStrip();
            tray.ContextMenuStrip.Opening += delegate { FillTrayMenu(tray.ContextMenuStrip); };
            FillTrayMenu(tray.ContextMenuStrip);
            tray.DoubleClick += delegate { CreateFence(Cursor.Position); };
        }

        void FillTrayMenu(ContextMenuStrip m)
        {
            m.Items.Clear();
            m.Items.Add(T.S("newFence"), null, delegate
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                int n = Config.Fences.Count;
                CreateFence(new Point(wa.X + (int)((120 + n * 30) * Scale), wa.Y + (int)((120 + n * 30) * Scale)));
            });

            bool allLocked = Config.Fences.Count > 0 && Config.Fences.TrueForAll(delegate(FenceData f) { return f.Locked; });
            m.Items.Add(allLocked ? T.S("unlockAll") : T.S("lockAll"), null, delegate
            {
                foreach (FenceData f in Config.Fences) f.Locked = !allLocked;
                Save(); RenderAll();
            });

            m.Items.Add(T.S("opacity"), null, delegate { ShowOpacityDialog(); });

            ToolStripMenuItem collect = new ToolStripMenuItem(T.S("collectMode"));
            collect.Checked = Config.HideDesktopIcons;
            collect.Click += delegate
            {
                Config.HideDesktopIcons = !Config.HideDesktopIcons;
                if (Config.HideDesktopIcons) CollectAll(false);
                else
                {
                    List<string> failed = Storage.RestoreAll(Config);
                    if (failed.Count > 0) Notify(T.F("cantRelease", string.Join("\n", failed.ToArray())));
                }
                Save(); RenderAll();
            };
            m.Items.Add(collect);

            ToolStripMenuItem auto = new ToolStripMenuItem(T.S("autostart"));
            auto.Checked = IsAutoStart();
            auto.Click += delegate { SetAutoStart(!IsAutoStart(), false); };
            m.Items.Add(auto);

            ToolStripMenuItem lang = new ToolStripMenuItem(T.S("language"));
            for (int i = 0; i < T.Codes.Length; i++)
            {
                string code = T.Codes[i];
                ToolStripMenuItem li = new ToolStripMenuItem(T.NativeNames[i]);
                li.Checked = T.Current == code;
                li.Click += delegate { SetLanguage(code); };
                lang.DropDownItems.Add(li);
            }
            m.Items.Add(lang);

            ToolStripMenuItem hideTray = new ToolStripMenuItem(T.S("hideTray"));
            hideTray.Checked = Config.HideTray;
            hideTray.Click += delegate { SetTrayHidden(!Config.HideTray); };
            m.Items.Add(hideTray);

            m.Items.Add(T.S("openConfig"), null, delegate
            {
                try { Directory.CreateDirectory(AppConfig.DataDir); Process.Start("explorer.exe", "\"" + AppConfig.DataDir + "\""); } catch { }
            });

            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(T.S("exit"), null, delegate { Exit(); });
        }

        /// <summary>格子标题栏的齿轮按钮：弹出和托盘右键一样的设置菜单</summary>
        public void ShowSettingsMenu(Point screenPt)
        {
            ContextMenuStrip m = new ContextMenuStrip();
            FillTrayMenu(m);
            m.Closed += delegate { ui.BeginInvoke((MethodInvoker)delegate { m.Dispose(); }); };
            m.Show(screenPt);
        }

        void SetTrayHidden(bool hide)
        {
            if (hide && Config.Fences.Count == 0) hide = false;
            Config.HideTray = hide;
            if (tray != null) tray.Visible = !hide;
            Save();
            if (hide) MessageBox.Show(T.S("trayHiddenInfo"), "DeskFence", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>再次运行 DeskFence.exe 时：恢复托盘图标</summary>
        void ListenShowTray()
        {
            try
            {
                EventWaitHandle ev = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowTrayEventName);
                Thread th = new Thread(delegate()
                {
                    while (true)
                    {
                        ev.WaitOne();
                        try
                        {
                            ui.BeginInvoke((MethodInvoker)delegate
                            {
                                if (exiting) return;
                                Config.HideTray = false;
                                if (tray != null) { tray.Visible = true; Notify(T.S("trayRestored")); }
                                Save();
                            });
                        }
                        catch { }
                    }
                });
                th.IsBackground = true;
                th.Start();
            }
            catch (Exception ex) { Log.Write("监听恢复托盘失败: " + ex.Message); }
        }

        public void ShowOpacityDialog()
        {
            OpacityDialog.Open(this);
        }

        void SetLanguage(string code)
        {
            Config.Language = code;
            T.Set(code);
            if (tray != null) tray.Text = T.S("trayTip");
            Save();
            RenderAll();
        }

        public bool IsOverFence(Point p)
        {
            foreach (FenceForm f in forms) if (f.Visible && f.Bounds.Contains(p)) return true;
            return false;
        }

        /// <summary>该点下面是不是桌面（包括挂在桌面上的其他整理软件的窗口）</summary>
        public static bool IsDesktopAt(Point p)
        {
            try
            {
                IntPtr h = Native.WindowFromPoint(new Native.POINT(p.X, p.Y));
                if (h == IntPtr.Zero) return false;
                IntPtr root = Native.GetAncestor(h, 2 /*GA_ROOT*/);
                if (root == IntPtr.Zero) root = h;
                string cls = Native.ClassOf(root);
                if (cls == "Progman" || cls == "WorkerW") return true;
                IntPtr progman = Native.FindWindow("Progman", null);
                IntPtr owner = Native.GetWindow(root, 4 /*GW_OWNER*/);
                if (owner != IntPtr.Zero && owner == progman) return true;
                Log.Write("拖出位置不是桌面，窗口类名: " + cls + " / " + Native.ClassOf(h));
                return false;
            }
            catch { return false; }
        }

        bool IsAutoStart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue("DeskFence") != null;
            }
            catch { return false; }
        }

        void SetAutoStart(bool on, bool silent)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue("DeskFence", "\"" + Application.ExecutablePath + "\"");
                    else if (k.GetValue("DeskFence") != null) k.DeleteValue("DeskFence");
                }
            }
            catch (Exception ex)
            {
                Log.Write("设置开机启动失败: " + ex.Message);
                if (!silent) MessageBox.Show(T.S("autostartFailed") + ex.Message, "DeskFence");
            }
        }

        /// <summary>退出：所有收纳的文件放回桌面（格子的布局保留，下次启动再收回来）</summary>
        void Exit()
        {
            exiting = true;
            if (retryTimer != null) retryTimer.Stop();
            if (winEventHook != IntPtr.Zero) { try { Native.UnhookWinEvent(winEventHook); } catch { } winEventHook = IntPtr.Zero; }
            foreach (FileSystemWatcher w in watchers) { try { w.EnableRaisingEvents = false; w.Dispose(); } catch { } }
            List<string> failed = Storage.RestoreAll(Config);
            Native.RefreshDesktop();
            // 有文件正被占用没放回去时，CleanExit 保持 false，守护进程会继续重试
            Config.CleanExit = failed.Count == 0;
            Save();
            if (failed.Count > 0)
                MessageBox.Show(T.F("restoreLater", string.Join("\n", failed.ToArray())), "DeskFence");
            foreach (FenceForm f in forms.ToArray()) { f.ClosingByApp = true; try { f.Close(); f.Dispose(); } catch { } }
            forms.Clear();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            ExitThread();
        }

        static Icon MakeTrayIcon()
        {
            using (Bitmap b = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (Brush bg = new SolidBrush(Color.FromArgb(255, 40, 120, 220)))
                        g.FillRectangle(bg, 1, 1, 30, 30);
                    using (Brush w = new SolidBrush(Color.White))
                    {
                        g.FillRectangle(w, 5, 5, 10, 10);
                        g.FillRectangle(w, 17, 5, 10, 10);
                        g.FillRectangle(w, 5, 17, 10, 10);
                        g.FillRectangle(w, 17, 17, 10, 10);
                    }
                }
                return Icon.FromHandle(b.GetHicon());
            }
        }
    }

    /// <summary>
    /// 守护进程：主程序被关掉、崩溃、卡死后被结束时，把收纳的文件放回桌面。
    /// </summary>
    static class Watchdog
    {
        public static void Run(int pid)
        {
            try
            {
                try
                {
                    using (Process p = Process.GetProcessById(pid)) p.WaitForExit();
                }
                catch (ArgumentException) { } // 主程序已经不在了
                Thread.Sleep(800);
                bool shuttingDown = false;
                try { shuttingDown = Native.GetSystemMetrics(0x2000 /*SM_SHUTTINGDOWN*/) != 0; } catch { }
                if (shuttingDown) return; // 关机/注销时不动文件，下次开机再处理

                using (Mutex rm = new Mutex(false, Controller.RestoreMutexName))
                {
                    bool got = false;
                    try { got = rm.WaitOne(60000); } catch (AbandonedMutexException) { got = true; }
                    try
                    {
                        // 10 分钟内反复尝试（文件被 Excel 等占用时等它关掉）
                        for (int round = 0; round < 120; round++)
                        {
                            if (MainRunning()) return; // 主程序又被打开了，交给它
                            AppConfig c = AppConfig.Load(AppConfig.DefaultFile);
                            if (c.CleanExit) return;
                            List<string> failed = Storage.RestoreAll(c);
                            c.CleanExit = failed.Count == 0;
                            try { c.Save(AppConfig.DefaultFile); } catch (Exception ex) { Log.Write("守护进程保存失败: " + ex.Message); }
                            if (c.CleanExit)
                            {
                                Log.Write("主程序意外退出，已把收纳的文件放回桌面");
                                Native.RefreshDesktop();
                                return;
                            }
                            Thread.Sleep(5000);
                        }
                    }
                    finally { if (got) rm.ReleaseMutex(); }
                }
            }
            catch (Exception ex) { Log.Write("守护进程出错: " + ex); }
        }

        static bool MainRunning()
        {
            bool created;
            using (Mutex m = new Mutex(false, Program.SingleInstanceName, out created)) { }
            return !created;
        }
    }
}
