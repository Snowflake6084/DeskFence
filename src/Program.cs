using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace DeskFence
{
    static class Program
    {
        public const string SingleInstanceName = "DeskFence_SingleInstance_9F2B7C";
        public const string ShowTrayEventName = "DeskFence_ShowTray_9F2B7C";

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 1 && args[0] == "--watchdog")
            {
                int pid;
                if (int.TryParse(args[1], out pid)) Watchdog.Run(pid);
                return;
            }
            if (args.Length > 0 && args[0] == "--selftest")
            {
                Environment.ExitCode = SelfTest.Run();
                return;
            }

#if PREVIEW
            if (args.Length > 1 && args[0] == "--preview")
            {
                Preview.Make(args[1]);
                return;
            }
#endif

            bool created;
            using (Mutex mutex = new Mutex(true, SingleInstanceName, out created))
            {
                if (!created)
                {
                    // 已经在运行：通知它把托盘图标显示出来（托盘被隐藏时靠这个找回来）
                    try
                    {
                        EventWaitHandle ev;
                        if (EventWaitHandle.TryOpenExisting(ShowTrayEventName, out ev)) { ev.Set(); ev.Dispose(); return; }
                    }
                    catch { }
                    try { T.Set(AppConfig.Load(AppConfig.DefaultFile).Language); } catch { }
                    MessageBox.Show(T.S("alreadyRunning"), "DeskFence");
                    return;
                }
                try { Native.SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { Log.Write("未处理异常: " + e.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) { Log.Write("严重错误: " + e.ExceptionObject); };
                Application.Run(new Controller());
            }
        }
    }

    class InputDialog : Form
    {
        TextBox box;

        InputDialog(string prompt, string value)
        {
            Text = "DeskFence";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 9f);
            ClientSize = new Size(300, 108);

            Label lb = new Label();
            lb.Text = prompt; lb.AutoSize = true; lb.Location = new Point(12, 12);
            box = new TextBox();
            box.Text = value; box.Location = new Point(12, 36); box.Width = 276;
            Button ok = new Button();
            ok.Text = T.S("ok"); ok.DialogResult = DialogResult.OK; ok.Location = new Point(132, 72); ok.Width = 75;
            Button cancel = new Button();
            cancel.Text = T.S("cancel"); cancel.DialogResult = DialogResult.Cancel; cancel.Location = new Point(213, 72); cancel.Width = 75;
            Controls.Add(lb); Controls.Add(box); Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
            Shown += delegate { Activate(); box.Focus(); box.SelectAll(); };
        }

        public static string Ask(string prompt, string value, Point at)
        {
            using (InputDialog d = new InputDialog(prompt, value))
            {
                d.StartPosition = FormStartPosition.Manual;
                Rectangle wa = Screen.FromPoint(at).WorkingArea;
                int x = Math.Min(Math.Max(wa.X, at.X), wa.Right - d.Width);
                int y = Math.Min(Math.Max(wa.Y, at.Y), wa.Bottom - d.Height);
                d.Location = new Point(x, y);
                return d.ShowDialog() == DialogResult.OK ? d.box.Text : null;
            }
        }
    }

#if PREVIEW
    static class Preview
    {
        public static void Make(string outFile)
        {
            AppConfig cfg = new AppConfig();
            float sc = 1f;
            Controller app = new Controller(cfg, sc);
            string[] names = { "must see.md", "task.md", "temporary.md", "salary.xlsx", "售后争议待处理.txt", "直播间中控文案特别长的名字测试.txt", "微信.lnk", "Obsidian.lnk", "count.xlsx" };
            FenceData a = new FenceData(); a.Title = "desktop"; a.W = 200; a.H = 300; a.ListMode = true;
            FenceData b = new FenceData(); b.Title = "exe"; b.W = 300; b.H = 300; b.ListMode = false;
            FenceData c = new FenceData(); c.Title = "空格子"; c.W = 220; c.H = 160;
            FenceData d = new FenceData(); d.Title = "折叠的格子"; d.W = 220; d.H = 160; d.Collapsed = true; d.Locked = true;
            foreach (string n in names)
            {
                ItemData x = new ItemData(); x.Path = "/tmp/pv/" + n; a.Items.Add(x);
                ItemData y = new ItemData(); y.Path = "/tmp/pv/" + n; b.Items.Add(y);
            }
            cfg.Fences.Add(a); cfg.Fences.Add(b); cfg.Fences.Add(c); cfg.Fences.Add(d);
            Directory.CreateDirectory("/tmp/pv");
            foreach (string n in names) if (n != "count.xlsx") File.WriteAllText("/tmp/pv/" + n, "x");

            using (Bitmap canvas = new Bitmap(980, 360))
            using (Graphics g = Graphics.FromImage(canvas))
            {
                using (System.Drawing.Drawing2D.LinearGradientBrush br = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, 980, 360), Color.FromArgb(70, 110, 60), Color.FromArgb(150, 160, 170), 90f))
                    g.FillRectangle(br, 0, 0, 980, 360);
                using (Brush w = new SolidBrush(Color.FromArgb(240, 240, 240))) g.FillRectangle(w, 0, 0, 980, 60);
                int x = 10;
                int[] hov = { 2, 4, -1, -1 };
                int[] drop = { -1, -1, 0, -1 };
                for (int i = 0; i < 4; i++)
                {
                    FenceForm f = new FenceForm(app, cfg.Fences[i]);
                    using (Bitmap bm = f.Preview(hov[i], drop[i])) { g.DrawImage(bm, x, 20); x += bm.Width + 15; }
                    bool[] tf = f.TruncatedFlags();
                    for (int k = 0; k < tf.Length; k++) Console.WriteLine("fence" + i + (cfg.Fences[i].ListMode ? "(列表) " : "(图标) ") + DesktopHelper.DisplayName(cfg.Fences[i].Items[k].Path) + " => " + (tf[k] ? "显示全名提示" : "完整显示"));
                }
                canvas.Save(outFile, System.Drawing.Imaging.ImageFormat.Png);
            }
            foreach (string lc in new string[] { "zh", "en", "ja" })
            {
                T.Set(lc);
                using (OpacityDialog od = new OpacityDialog(app))
                {
                    foreach (Control ctl in od.Controls) Console.WriteLine(lc + " " + ctl.GetType().Name + " '" + ctl.Text + "' x=" + ctl.Left + " w=" + ctl.Width + " right=" + ctl.Right + " / client " + od.ClientSize.Width);
                    using (Bitmap bm = new Bitmap(od.Width, od.Height)) { od.DrawToBitmap(bm, new Rectangle(0, 0, od.Width, od.Height)); bm.Save(outFile + "." + lc + ".png"); }
                }
            }
        }
    }

#endif

    /// <summary>不依赖 Win32 的逻辑自测：DeskFence.exe --selftest</summary>
    static class SelfTest
    {
        static int fails;

        static void Check(bool ok, string name)
        {
            Console.WriteLine((ok ? "  通过  " : "  失败  ") + name);
            if (!ok) fails++;
        }

        public static int Run()
        {
            fails = 0;
            Console.WriteLine("DeskFence 自测");

            // 1. 配置保存/读取
            string dir = Path.Combine(Path.GetTempPath(), "deskfence_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "config.xml");
            AppConfig c = new AppConfig();
            c.BgAlphaPercent = 60;
            c.Language = "ja";
            c.AllAlphaPercent = 80;
            c.HideTray = true;
            FenceData f = new FenceData();
            f.Title = "测试 格子<&>";
            f.X = -1500; f.Y = 20; f.W = 333; f.H = 444; f.Locked = true; f.ListMode = true; f.Collapsed = true;
            ItemData a = new ItemData(); a.Path = Path.Combine(dir, "a.txt"); a.HiddenByUs = true; a.AddedSystem = true;
            ItemData b = new ItemData(); b.Path = Path.Combine(dir, "文件夹 b");
            f.Items.Add(a); f.Items.Add(b);
            c.Fences.Add(f);
            c.Save(file);
            c.Save(file); // 第二次走替换路径
            AppConfig c2 = AppConfig.Load(file);
            Check(c2.BgAlphaPercent == 60 && c2.Fences.Count == 1, "配置读写：基本字段");
            Check(c2.Language == "ja" && c2.AllAlphaPercent == 80, "配置读写：语言和整体透明度");
            Check(c2.HideTray, "配置读写：隐藏托盘");
            string savedText = File.ReadAllText(file);
            Check(savedText.Contains("<!--") && savedText.Contains(Storage.Root) && savedText.StartsWith("<?xml") && savedText.Contains("encoding=\"utf-8\""), "配置文件：开头有存放位置说明");
            FenceData f2 = c2.Fences[0];
            Check(f2.Title == f.Title && f2.X == -1500 && f2.W == 333 && f2.H == 444, "配置读写：位置/名称(含特殊字符)");
            Check(f2.Locked && f2.ListMode && f2.Collapsed && f2.Id == f.Id, "配置读写：锁定/模式/折叠/Id");
            Check(f2.Items[0].AddedSystem && !f2.Items[1].AddedSystem, "配置读写：系统属性标记");
            Check(f2.Items.Count == 2 && f2.Items[0].HiddenByUs && !f2.Items[1].HiddenByUs && f2.Items[1].Path == b.Path, "配置读写：项目");
            File.WriteAllText(file, "<<<坏文件");
            AppConfig c3 = AppConfig.Load(file);
            Check(c3 != null && c3.Fences.Count == 0, "损坏的配置文件不会崩溃");
            Check(Directory.GetFiles(dir, "config.xml.bad_*").Length == 1, "损坏的配置被备份");

            // 2. 排序移动
            FenceData m = new FenceData();
            foreach (string s in new string[] { "0", "1", "2", "3" }) { ItemData it = new ItemData(); it.Path = Path.Combine(dir, s); m.Items.Add(it); }
            m.MoveItem(0, 3);
            Check(Names(m) == "1,2,0,3", "移动：0 插到 3 之前");
            m.MoveItem(3, 0);
            Check(Names(m) == "3,1,2,0", "移动：末尾移到开头");
            m.MoveItem(1, 4);
            Check(Names(m) == "3,2,0,1", "移动：移到末尾");
            m.MoveItem(2, 2);
            Check(Names(m) == "3,2,0,1", "移动：原地不变");
            Check(m.IndexOf(Path.Combine(dir, "0").ToUpperInvariant()) == 2, "查找：路径不区分大小写");

            // 3. 名称显示
            Check(DesktopHelper.DisplayName(@"C:\Users\x\Desktop\微信.lnk") == "微信" || Path.DirectorySeparatorChar != '\\', "名称：快捷方式去掉 .lnk");
            Check(DesktopHelper.DisplayName(Path.Combine(dir, "salary.xlsx")) == "salary.xlsx", "名称：普通文件保留扩展名");
            Check(DesktopHelper.DisplayName(Path.Combine(dir, "网站.url")) == "网站", "名称：.url 去掉扩展名");
            Check(DesktopHelper.SamePath(dir + Path.DirectorySeparatorChar, dir), "路径比较：末尾斜杠");

            // 4. 隐藏属性（Linux 下 mono 只是模拟，不报错即可）
            string tf = Path.Combine(dir, "h.txt");
            File.WriteAllText(tf, "x");
            bool r1 = DesktopHelper.SetHidden(tf, true);
            bool r2 = DesktopHelper.SetHidden(tf, true);
            if (Path.DirectorySeparatorChar == '\\') Check(r1 && !r2 && DesktopHelper.IsHidden(tf) && DesktopHelper.SetHidden(tf, false) && !DesktopHelper.IsHidden(tf), "隐藏：设置/重复/取消");

            // 5. 布局
            FenceLayout L = new FenceLayout();
            L.S = 1f; L.Width = 300; L.Height = 320; L.Count = 10; L.ListMode = false;
            Check(L.Cols == 3, "布局：300 宽图标模式 3 列 (实际 " + L.Cols + ")");
            Rectangle r0 = L.ItemRect(0), r4 = L.ItemRect(4);
            Check(L.HitItem(Center(r0)) == 0 && L.HitItem(Center(r4)) == 4, "布局：点中图标");
            Check(L.HitItem(new Point(150, 10)) == -1, "布局：标题栏不算点中项目");
            Check(L.ContentHeight > L.Content.Height && L.MaxScroll > 0, "布局：10 个图标需要滚动");
            L.Scroll = 99999; L.ClampScroll();
            Check(L.Scroll == L.MaxScroll, "布局：滚动不越界");
            Rectangle last = L.ItemRect(9);
            Check(last.Bottom <= L.Content.Bottom + L.R(4) && last.Bottom > L.Content.Top, "布局：滚到底最后一个可见");
            L.Scroll = 0;
            Check(L.InsertIndex(new Point(r0.Left + 2, r0.Top + 5)) == 0, "拖放：放在第一个左边 → 0");
            Check(L.InsertIndex(new Point(r0.Right - 2, r0.Top + 5)) == 1, "拖放：放在第一个右半边 → 1");
            Check(L.InsertIndex(new Point(150, 5000)) == 10, "拖放：拖到最下面 → 末尾");
            Rectangle mk = L.InsertMarker(10);
            Check(mk.Width > 0 && mk.Height > 0, "拖放：末尾指示线有效");
            L.ListMode = true;
            Check(L.Cols == 1 && L.ItemRect(1).Y - L.ItemRect(0).Y == L.RowH, "布局：列表模式单列");
            Check(L.InsertIndex(new Point(50, L.Content.Top + L.RowH * 3 + 2)) == 3, "拖放：列表模式插入位置");
            L.S = 1.5f;
            Check(L.TitleH == 45 && L.RowH == 39, "布局：150% 缩放");
            L.Width = 50; L.Height = 30; L.S = 1f; L.Count = 0;
            Check(L.Content.Width >= 1 && L.Content.Height >= 1 && L.MaxScroll == 0, "布局：极小尺寸不出负数");

            // 7. 收纳 / 放回（移动文件）
            string desk = Path.Combine(dir, "桌面");
            string store = Path.Combine(dir, "store");
            Directory.CreateDirectory(desk);
            Storage.Root = store;
            string fa = Path.Combine(desk, "jd_profit.xlsx");
            File.WriteAllText(fa, "内容A");
            string fd = Path.Combine(desk, "文件夹 X");
            Directory.CreateDirectory(fd);
            File.WriteAllText(Path.Combine(fd, "inner.txt"), "in");
            ItemData ia = new ItemData(); ia.Path = fa;
            ItemData idd = new ItemData(); idd.Path = fd;
            Check(Storage.Collect(ia) == null && !File.Exists(fa) && ia.IsStored() && File.ReadAllText(ia.CurrentPath()) == "内容A", "收纳文件：桌面上消失，内容不变");
            Check(Path.GetFileName(ia.StoredPath) == "jd_profit.xlsx", "收纳文件：保留原文件名");
            Check(Storage.Collect(idd) == null && !Directory.Exists(fd) && File.Exists(Path.Combine(idd.CurrentPath(), "inner.txt")), "收纳文件夹：整个文件夹移走");
            Check(Storage.Collect(ia) == null && ia.IsStored(), "重复收纳不出错");
            File.WriteAllText(ia.CurrentPath(), "改过的内容");
            AppConfig sc = new AppConfig();
            FenceData sf = new FenceData(); sf.Items.Add(ia); sf.Items.Add(idd); sc.Fences.Add(sf);
            List<string> sfail = Storage.RestoreAll(sc);
            Check(sfail.Count == 0 && File.ReadAllText(fa) == "改过的内容" && Directory.Exists(fd) && !ia.IsStored() && ia.StoredPath == null, "全部放回：文件和修改都回到桌面");
            Check(Directory.GetFileSystemEntries(store).Length == 0, "放回后存放目录清空");
            Storage.Collect(ia);
            File.WriteAllText(fa, "桌面上新建的同名文件");
            Check(Storage.Restore(ia) == null && File.Exists(Path.Combine(desk, "jd_profit (2).xlsx")) && ia.Path.EndsWith("jd_profit (2).xlsx")
                  && File.ReadAllText(fa) == "桌面上新建的同名文件", "放回时同名：自动改成 (2)，不覆盖");
            ItemData gone = new ItemData(); gone.Path = Path.Combine(desk, "不存在.txt");
            Check(Storage.Collect(gone) != null && !gone.IsStored() && gone.CurrentPath() == gone.Path, "收纳不存在的文件：报错不崩溃");
            ItemData lost = new ItemData(); lost.Path = Path.Combine(desk, "a.txt"); lost.StoredPath = Path.Combine(store, "x", "a.txt");
            Check(Storage.Restore(lost) == null && lost.StoredPath == null, "存放文件丢失：记录自动清掉");
            AppConfig cx = new AppConfig();
            Check(cx.CleanExit, "新配置默认已正常退出");
            FenceData rf = new FenceData(); ItemData ri = new ItemData(); ri.Path = "p"; ri.StoredPath = "q"; rf.Items.Add(ri); cx.Fences.Add(rf); cx.CleanExit = false;
            cx.Save(file);
            AppConfig cy = AppConfig.Load(file);
            Check(!cy.CleanExit && cy.Fences[0].Items[0].StoredPath == "q", "配置读写：收纳位置和退出状态");

            // 10. 配置文件开头说明：保存成功（旧版这里用了 .NET Framework 没有的方法，保存直接失败）
            {
                string f10 = Path.Combine(dir, "c10.xml");
                AppConfig c10 = new AppConfig(); c10.BgAlphaPercent = 33;
                c10.Save(f10);
                Check(AppConfig.Load(f10).BgAlphaPercent == 33, "保存：带说明的配置能写入并读回");
            }

            // 9. 找回"已不存在"的项目
            {
                string rdesk = Path.Combine(dir, "rdesk"), rstore = Path.Combine(dir, "rstore");
                Directory.CreateDirectory(Path.Combine(rstore, "g1"));
                Directory.CreateDirectory(Path.Combine(rstore, "g2"));
                Directory.CreateDirectory(rdesk);
                File.WriteAllText(Path.Combine(rstore, "g1", "a.md"), "a");          // 在存放夹里，但记录丢了
                Directory.CreateDirectory(Path.Combine(rstore, "g2", "文件夹"));       // 文件夹也能找回
                File.WriteAllText(Path.Combine(rdesk, "b (2).xlsx"), "b");           // 被放回桌面时改了名
                AppConfig rc = new AppConfig(); FenceData rf2 = new FenceData(); rc.Fences.Add(rf2);
                ItemData x1 = new ItemData(); x1.Path = Path.Combine(rdesk, "a.md"); x1.StoredPath = Path.Combine(rstore, "gone", "a.md");
                ItemData x2 = new ItemData(); x2.Path = Path.Combine(rdesk, "文件夹");
                ItemData x3 = new ItemData(); x3.Path = Path.Combine(rdesk, "b.xlsx");
                ItemData x4 = new ItemData(); x4.Path = Path.Combine(rdesk, "真的没了.txt");
                rf2.Items.Add(x1); rf2.Items.Add(x2); rf2.Items.Add(x3); rf2.Items.Add(x4);
                List<string> miss = new List<string>();
                int got = Storage.Relink(rc, new string[] { rdesk }, new string[] { rstore }, miss);
                Check(got == 3 && miss.Count == 1 && miss[0] == "真的没了.txt", "找回：3 个找到，1 个确实没了");
                Check(x1.IsStored() && File.ReadAllText(x1.CurrentPath()) == "a", "找回：存放夹里的文件重新关联");
                Check(x2.IsStored() && Directory.Exists(x2.CurrentPath()), "找回：存放夹里的文件夹重新关联");
                Check(!x3.IsStored() && x3.Path.EndsWith("b (2).xlsx"), "找回：桌面上改了名的 (2)");
                Check(Storage.Relink(rc, new string[] { rdesk }, new string[] { rstore }, null) == 0, "找回：再跑一次不会乱关联");
            }

            // 8. 程序本身不能收进格子
            string appDir = Path.Combine(dir, "工具");
            Directory.CreateDirectory(appDir);
            string fakeExe = Path.Combine(appDir, "DeskFence.exe");
            File.WriteAllText(fakeExe, "x");
            string savedExe = Controller.ExePath;
            Controller.ExePath = fakeExe;
            Check(Controller.IsSelf(fakeExe), "识别自身：exe 本身");
            Check(Controller.IsSelf(appDir), "识别自身：包含 exe 的文件夹");
            Check(Controller.IsSelf(Path.Combine(desk, "deskfence.EXE")), "识别自身：同名 exe（不区分大小写）");
            Check(!Controller.IsSelf(Path.Combine(desk, "jd_profit (2).xlsx")) && !Controller.IsSelf(desk + "_none"), "识别自身：普通文件不误判");
            Check(!Controller.IsSelf(Path.Combine(desk, "DeskFence.lnk")), "识别自身：快捷方式可以放进格子");
            string sep = Path.DirectorySeparatorChar.ToString();
            Check(Controller.Relocate(fakeExe, fakeExe, desk + sep + "DeskFence.exe") == desk + sep + "DeskFence.exe", "移动后路径：exe 本身");
            Check(Controller.Relocate(fakeExe, appDir, desk + sep + "工具") == Path.Combine(desk + sep + "工具", "DeskFence.exe"), "移动后路径：所在文件夹");
            Check(Controller.Relocate(fakeExe, desk, store) == fakeExe, "移动后路径：无关移动不变");
            Controller.ExePath = savedExe;

            // 6. 多语言
            Check(T.CountIncomplete() == 0, "语言：所有词条中英日齐全");
            T.Set("en"); string en = T.S("newFence");
            T.Set("ja"); string ja = T.S("newFence");
            T.Set("zh"); string zh = T.S("newFence");
            Check(en == "New fence" && ja == "新しいボックス" && zh == "新建格子", "语言：切换生效");
            T.Set("xx");
            Check(T.Current == "en", "语言：未知代码回退英文");
            T.Set("zh");
            Check(T.F("deleteAskItems", "A", 3).Contains("3"), "语言：带参数文字");
            // 旧配置文件（没有新字段）能读
            File.WriteAllText(file, "<?xml version=\"1.0\"?><AppConfig><BgAlphaPercent>30</BgAlphaPercent><HideDesktopIcons>true</HideDesktopIcons><Fences/></AppConfig>");
            AppConfig old = AppConfig.Load(file);
            Check(old.BgAlphaPercent == 30 && old.AllAlphaPercent == 100 && old.Language == "", "旧版配置兼容");

            try { Directory.Delete(dir, true); } catch { }
            Console.WriteLine(fails == 0 ? "全部通过" : ("失败 " + fails + " 项"));
            return fails == 0 ? 0 : 1;
        }

        static Point Center(Rectangle r) { return new Point(r.X + r.Width / 2, r.Y + r.Height / 2); }

        static string Names(FenceData f)
        {
            List<string> l = new List<string>();
            foreach (ItemData it in f.Items) l.Add(Path.GetFileName(it.Path));
            return string.Join(",", l.ToArray());
        }
    }
}
