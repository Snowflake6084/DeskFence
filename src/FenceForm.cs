using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DeskFence
{
    class FenceForm : Form
    {
        public const string DragFormat = "DeskFenceItem";

        public readonly FenceData Data;
        readonly Controller app;
        readonly FenceLayout L = new FenceLayout();
        public bool ClosingByApp;

        enum Mode { None, Move, Resize, ItemPending }
        Mode mode = Mode.None;
        Point downScreen;
        Rectangle downBounds;
        bool resizeRight, resizeBottom;
        int downItem = -1;

        int hover = -1, selected = -1, dropIndex = -1;
        bool dropActive;
        int hoverButton = -1; // 0 折叠 1 锁 2 图标/列表切换 3 设置

        Font titleFont, itemFont;

        public FenceForm(Controller app, FenceData data)
        {
            this.app = app;
            Data = data;
            L.S = app.Scale;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "DeskFence - " + data.Title;
            AllowDrop = true;
            MinimumSize = new Size(1, 1);
            MinimizeBox = false;   // 防止"显示桌面"把格子最小化
            MaximizeBox = false;
            titleFont = new Font("Microsoft YaHei UI", 13f * L.S, FontStyle.Regular, GraphicsUnit.Pixel);
            itemFont = new Font("Microsoft YaHei UI", 12f * L.S, FontStyle.Regular, GraphicsUnit.Pixel);
            FixBounds();
            Bounds = new Rectangle(Data.X, Data.Y, Data.W, Data.Collapsed ? L.TitleH : Data.H);
        }

        int MinW { get { return L.R(120); } }
        int MinH { get { return L.TitleH + L.R(40); } }

        IntPtr desktopHost = IntPtr.Zero;
        bool wasBelow;

        /// <summary>
        /// 定时自检（由 Controller 每 2 秒调用）：
        /// 被最小化/隐藏了就恢复；桌面窗口换了（资源管理器重启、换了壁纸方式）就重新挂上；
        /// 被压到桌面下面去了就提上来。
        /// </summary>
        public void Guard()
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                if (Native.IsIconic(Handle) || !Native.IsWindowVisible(Handle))
                {
                    Log.Write("格子「" + Data.Title + "」被最小化/隐藏，已恢复");
                    Native.ShowWindow(Handle, 4 /*SW_SHOWNOACTIVATE*/);
                    Render();
                }
                IntPtr host = Native.FindDesktopHost();
                if (host != IntPtr.Zero && host != desktopHost)
                {
                    if (desktopHost != IntPtr.Zero) Log.Write("桌面窗口变了，格子重新挂载");
                    AttachToDesktop();
                }
                if (desktopHost != IntPtr.Zero && !IsAboveDesktop())
                {
                    if (!wasBelow) Log.Write("格子「" + Data.Title + "」被压到桌面下面，已提上来");
                    wasBelow = true;
                    PlaceAboveDesktop();
                }
                else wasBelow = false;
                if (!IsOnAnyScreen())
                {
                    Data.X = Left; Data.Y = Top;
                    FixBounds();
                    Location = new Point(Data.X, Data.Y);
                    Render();
                }
            }
            catch (Exception ex) { Log.Write("格子自检出错: " + ex.Message); }
        }

        bool IsOnAnyScreen()
        {
            Rectangle r = new Rectangle(Left, Top, Width, L.TitleH);
            foreach (Screen s in Screen.AllScreens)
                if (s.WorkingArea.IntersectsWith(r)) return true;
            return false;
        }

        /// <summary>从自己往下找，能找到桌面窗口说明自己在桌面上面</summary>
        bool IsAboveDesktop()
        {
            IntPtr h = Handle;
            for (int i = 0; i < 2000; i++)
            {
                h = Native.GetWindow(h, Native.GW_HWNDNEXT);
                if (h == IntPtr.Zero) return false;
                if (h == desktopHost) return true;
            }
            return true;
        }

        /// <summary>紧贴桌面窗口的上方</summary>
        IntPtr SlotAboveDesktop()
        {
            IntPtr prev = Native.GetWindow(desktopHost, Native.GW_HWNDPREV);
            return prev;
        }

        void PlaceAboveDesktop()
        {
            IntPtr prev = SlotAboveDesktop();
            if (prev == Handle) return;
            IntPtr after = prev == IntPtr.Zero ? new IntPtr(0) /*HWND_TOP*/ : prev;
            Native.SetWindowPos(Handle, after, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
        }

        public void FixBounds()
        {
            if (Data.W < MinW) Data.W = MinW;
            if (Data.H < MinH) Data.H = MinH;
            Rectangle r = new Rectangle(Data.X, Data.Y, Data.W, L.TitleH);
            bool onScreen = false;
            foreach (Screen s in Screen.AllScreens)
                if (s.WorkingArea.IntersectsWith(r)) { onScreen = true; break; }
            if (!onScreen)
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                Data.X = wa.X + L.R(80);
                Data.Y = wa.Y + L.R(80);
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!AttachToDesktop())
            {
                // 开机时桌面可能还没准备好：每 2 秒重试，最多 2 分钟
                int tries = 0;
                Timer t = new Timer();
                t.Interval = 2000;
                t.Tick += delegate
                {
                    tries++;
                    if (IsDisposed || !IsHandleCreated || AttachToDesktop() || tries > 60) { t.Stop(); t.Dispose(); }
                };
                t.Start();
            }
            Render();
        }

        bool AttachToDesktop()
        {
            try
            {
                // 挂到桌面图标所在的窗口下：按 Win+D 显示桌面时不会被隐藏，也不会被桌面盖住
                IntPtr host = Native.FindDesktopHost();
                if (host == IntPtr.Zero) return false;
                Native.SetWindowLongPtr(Handle, Native.GWL_HWNDPARENT, host);
                desktopHost = host;
                PlaceAboveDesktop();
                return true;
            }
            catch (Exception ex) { Log.Write("挂载桌面失败: " + ex.Message); return false; }
        }

        protected override void WndProc(ref Message m)
        {
            if (activeShellMenu != null && activeShellMenu.HandleMessage(ref m)) return;
            if (m.Msg == Native.WM_WINDOWPOSCHANGING)
            {
                try
                {
                    Native.WINDOWPOS wp = (Native.WINDOWPOS)Marshal.PtrToStructure(m.LParam, typeof(Native.WINDOWPOS));
                    if ((wp.flags & Native.SWP_NOZORDER) == 0)
                    {
                        // 永远待在普通窗口下面，但紧贴在桌面图标层的上面。
                        // （旧版用 HWND_BOTTOM，当桌面图标在 WorkerW 里时会把格子压到桌面下面 → 格子"消失"）
                        if (desktopHost == IntPtr.Zero) wp.hwndInsertAfter = Native.HWND_BOTTOM;
                        else
                        {
                            IntPtr prev = SlotAboveDesktop();
                            if (prev == Handle) wp.flags |= Native.SWP_NOZORDER;
                            else wp.hwndInsertAfter = prev == IntPtr.Zero ? IntPtr.Zero : prev;
                        }
                        Marshal.StructureToPtr(wp, m.LParam, false);
                    }
                }
                catch { }
            }
            else if (m.Msg == 0x0112 /*WM_SYSCOMMAND*/)
            {
                int cmd = m.WParam.ToInt32() & 0xFFF0;
                if (cmd == 0xF020 /*SC_MINIMIZE*/ || cmd == 0xF030 /*SC_MAXIMIZE*/) return; // 不允许被最小化/最大化
            }
            else if (m.Msg == 0x0011 /*WM_QUERYENDSESSION*/ || (m.Msg == 0x0016 /*WM_ENDSESSION*/ && m.WParam != IntPtr.Zero))
            {
                app.OnSessionEnding(); // 关机/注销：文件放回桌面
            }
            else if (m.Msg == 0x0002 /*WM_DESTROY*/)
            {
                try { if (!ClosingByApp) Log.Write("格子「" + Data.Title + "」窗口被系统销毁（可能是资源管理器重启），将自动重建"); }
                catch { }
            }
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (titleFont != null) titleFont.Dispose();
                if (itemFont != null) itemFont.Dispose();
            }
            base.Dispose(disposing);
        }

        // ================= 绘制 =================

        void SyncLayout()
        {
            L.Width = Width;
            L.Height = Height;
            L.ListMode = Data.ListMode;
            L.Count = Data.Collapsed ? 0 : Data.Items.Count;
            L.ClampScroll();
        }

        public void Render()
        {
            if (!IsHandleCreated || IsDisposed) return;
            SyncLayout();
            int w = Math.Max(1, Width), h = Math.Max(1, Height);
            try
            {
                using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp)) DrawAll(g, w, h);
                    Apply(bmp);
                }
            }
            catch (Exception ex) { Log.Write("绘制失败: " + ex); }
        }

        void Apply(Bitmap bmp)
        {
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            IntPtr memDc = Native.CreateCompatibleDC(screenDc);
            IntPtr hBmp = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                hBmp = bmp.GetHbitmap(Color.FromArgb(0));
                old = Native.SelectObject(memDc, hBmp);
                Native.SIZE size = new Native.SIZE(bmp.Width, bmp.Height);
                Native.POINT src = new Native.POINT(0, 0);
                Native.POINT top = new Native.POINT(Left, Top);
                Native.BLENDFUNCTION blend = new Native.BLENDFUNCTION();
                blend.BlendOp = 0;
                blend.BlendFlags = 0;
                blend.SourceConstantAlpha = (byte)Math.Max(60, Math.Min(255, app.Config.AllAlphaPercent * 255 / 100));
                blend.AlphaFormat = Native.AC_SRC_ALPHA;
                Native.UpdateLayeredWindow(Handle, screenDc, ref top, ref size, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
            }
            finally
            {
                if (old != IntPtr.Zero) Native.SelectObject(memDc, old);
                if (hBmp != IntPtr.Zero) Native.DeleteObject(hBmp);
                Native.DeleteDC(memDc);
                Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        static GraphicsPath Round(Rectangle r, int rad)
        {
            GraphicsPath p = new GraphicsPath();
            int d = rad * 2;
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        Rectangle ButtonRect(int i)
        {
            int sz = L.R(22);
            int y = (L.TitleH - sz) / 2;
            switch (i)
            {
                case 0: return new Rectangle(L.R(6), y, sz, sz);
                case 1: return new Rectangle(L.R(6) + sz, y, sz, sz);
                case 2: return new Rectangle(Width - L.R(6) - sz * 2, y, sz, sz);
                default: return new Rectangle(Width - L.R(6) - sz, y, sz, sz);
            }
        }

        int HitButton(Point p)
        {
            for (int i = 0; i < 4; i++) if (ButtonRect(i).Contains(p)) return i;
            return -1;
        }

        void DrawAll(Graphics g, int w, int h)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            int a = Math.Max(12, Math.Min(250, app.Config.BgAlphaPercent * 255 / 100));
            Rectangle all = new Rectangle(0, 0, w - 1, h - 1);
            using (GraphicsPath path = Round(all, L.R(5)))
            {
                using (Brush bg = new SolidBrush(Color.FromArgb(a, 18, 20, 24))) g.FillPath(bg, path);
                Color border = dropActive ? Color.FromArgb(200, 90, 170, 255) : Color.FromArgb(Math.Min(255, a / 2 + 30), 255, 255, 255);
                using (Pen pen = new Pen(border, dropActive ? Math.Max(2, L.R(2)) : 1)) g.DrawPath(pen, path);
            }

            // 标题栏
            if (!Data.Collapsed)
            {
                using (Pen sep = new Pen(Color.FromArgb(Math.Min(255, a / 3 + 20), 255, 255, 255)))
                    g.DrawLine(sep, L.R(8), L.TitleH - 1, w - L.R(8), L.TitleH - 1);
            }
            Rectangle titleRect = new Rectangle(ButtonRect(1).Right + L.R(2), 0, ButtonRect(2).Left - ButtonRect(1).Right - L.R(4), L.TitleH);
            DrawText(g, string.IsNullOrEmpty(Data.Title) ? " " : Data.Title, titleFont, titleRect, StringAlignment.Center, StringAlignment.Center, Color.White, false);

            DrawButtons(g);

            if (Data.Collapsed) return;

            // 项目
            Rectangle content = L.Content;
            g.SetClip(content);
            if (Data.Items.Count == 0)
            {
                DrawText(g, T.S("dropHint"), itemFont, content, StringAlignment.Center, StringAlignment.Center, Color.FromArgb(170, 255, 255, 255), true);
            }
            for (int i = 0; i < Data.Items.Count; i++)
            {
                Rectangle r = L.ItemRect(i);
                if (r.Bottom < content.Top || r.Top > content.Bottom) continue;
                DrawItem(g, i, r);
            }
            if (dropActive && dropIndex >= 0)
            {
                using (Brush b = new SolidBrush(Color.FromArgb(230, 90, 170, 255))) g.FillRectangle(b, L.InsertMarker(dropIndex));
            }
            g.ResetClip();

            // 滚动条
            if (L.MaxScroll > 0)
            {
                int trackH = content.Height;
                int thumbH = Math.Max(L.R(20), trackH * content.Height / L.ContentHeight);
                int thumbY = content.Top + (trackH - thumbH) * L.Scroll / L.MaxScroll;
                using (Brush b = new SolidBrush(Color.FromArgb(110, 255, 255, 255)))
                using (GraphicsPath p = Round(new Rectangle(w - L.R(5), thumbY, L.R(3), thumbH), L.R(1)))
                    g.FillPath(b, p);
            }

            // 右下角缩放提示
            if (!Data.Locked)
            {
                using (Pen p = new Pen(Color.FromArgb(90, 255, 255, 255)))
                {
                    int s = L.R(4);
                    g.DrawLine(p, w - s * 3, h - s, w - s, h - s * 3);
                    g.DrawLine(p, w - s * 2, h - s, w - s, h - s * 2);
                }
            }
        }

        void DrawButtons(Graphics g)
        {
            for (int i = 0; i < 4; i++)
            {
                Rectangle r = ButtonRect(i);
                bool active = (i == 1 && Data.Locked);
                if (hoverButton == i)
                    using (Brush hb = new SolidBrush(Color.FromArgb(60, 255, 255, 255)))
                    using (GraphicsPath gp = Round(r, L.R(3))) g.FillPath(hb, gp);
                Color c = active ? Color.FromArgb(245, 255, 255, 255) : Color.FromArgb(150, 255, 255, 255);
                using (Pen p = new Pen(c, Math.Max(1f, 1.4f * L.S)))
                using (Brush b = new SolidBrush(c))
                {
                    float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, u = L.S;
                    switch (i)
                    {
                        case 0: // 折叠 ^ / 展开 v
                            if (Data.Collapsed)
                                g.DrawLines(p, new PointF[] { new PointF(cx - 4 * u, cy - 2 * u), new PointF(cx, cy + 2 * u), new PointF(cx + 4 * u, cy - 2 * u) });
                            else
                                g.DrawLines(p, new PointF[] { new PointF(cx - 4 * u, cy + 2 * u), new PointF(cx, cy - 2 * u), new PointF(cx + 4 * u, cy + 2 * u) });
                            break;
                        case 1: // 锁
                            g.FillRectangle(b, cx - 4.5f * u, cy - 0.5f * u, 9 * u, 6.5f * u);
                            if (Data.Locked)
                                g.DrawArc(p, cx - 3 * u, cy - 6 * u, 6 * u, 8 * u, 180, 180);
                            else
                                g.DrawArc(p, cx - 1 * u, cy - 6.5f * u, 6 * u, 8 * u, 180, 160);
                            break;
                        case 2: // 显示模式切换：图标模式时画 田，列表模式时画 ☰
                            if (!Data.ListMode)
                            {
                                float s = 3.2f * u, gap = 1.4f * u;
                                g.FillRectangle(b, cx - s - gap / 2, cy - s - gap / 2, s, s);
                                g.FillRectangle(b, cx + gap / 2, cy - s - gap / 2, s, s);
                                g.FillRectangle(b, cx - s - gap / 2, cy + gap / 2, s, s);
                                g.FillRectangle(b, cx + gap / 2, cy + gap / 2, s, s);
                            }
                            else
                            {
                                for (int k = -1; k <= 1; k++)
                                {
                                    g.FillRectangle(b, cx - 5 * u, cy + k * 3.2f * u - 0.8f * u, 1.6f * u, 1.6f * u);
                                    g.FillRectangle(b, cx - 2.5f * u, cy + k * 3.2f * u - 0.7f * u, 7.5f * u, 1.4f * u);
                                }
                            }
                            break;
                        default: // 设置 齿轮
                            {
                                float ro = 5.6f * u, ri = 3.6f * u;
                                using (GraphicsPath gear = new GraphicsPath())
                                {
                                    PointF[] pts = new PointF[32];
                                    for (int k = 0; k < 32; k++)
                                    {
                                        // 8 个齿：每个齿占 4 个点（外、外、内、内）
                                        double ang = (k / 32.0) * Math.PI * 2 - Math.PI / 32;
                                        float rad = (k % 4 < 2) ? ro : ri + 0.6f * u;
                                        pts[k] = new PointF(cx + (float)(Math.Cos(ang) * rad), cy + (float)(Math.Sin(ang) * rad));
                                    }
                                    gear.AddPolygon(pts);
                                    float hr = 1.9f * u;
                                    gear.AddEllipse(cx - hr, cy - hr, hr * 2, hr * 2); // 中间的孔（交替填充规则会挖空）
                                    g.FillPath(b, gear);
                                }
                            }
                            break;
                    }
                }
            }
        }

        void DrawItem(Graphics g, int i, Rectangle r)
        {
            ItemData it = Data.Items[i];
            string cur = it.CurrentPath();
            bool exists = DesktopHelper.Exists(cur);
            Rectangle hl = Rectangle.Inflate(r, -L.R(2), -L.R(1));
            if (i == selected || i == hover)
            {
                using (Brush b = new SolidBrush(Color.FromArgb(i == selected ? 70 : 38, 255, 255, 255)))
                using (GraphicsPath p = Round(hl, L.R(3))) g.FillPath(b, p);
            }
            string name = DesktopHelper.DisplayName(it.Path);
            Color tc = exists ? Color.White : Color.FromArgb(150, 255, 150, 150);
            if (!exists) name += T.S("missing");

            ImageAttributes ia = null;
            if (!exists)
            {
                ColorMatrix cm = new ColorMatrix();
                cm.Matrix33 = 0.4f;
                ia = new ImageAttributes();
                ia.SetColorMatrix(cm);
            }
            try
            {
                if (Data.ListMode)
                {
                    Bitmap ic = IconCache.Get(cur, false);
                    int sz = L.R(16);
                    Rectangle ir = new Rectangle(r.X + L.R(8), r.Y + (r.Height - sz) / 2, sz, sz);
                    DrawImage(g, ic, ir, ia);
                    Rectangle tr = new Rectangle(ir.Right + L.R(7), r.Y, r.Right - ir.Right - L.R(12), r.Height);
                    DrawText(g, name, itemFont, tr, StringAlignment.Near, StringAlignment.Center, tc, true);
                    MarkTruncated(i, g.MeasureString(name, itemFont).Width > tr.Width);
                }
                else
                {
                    Bitmap ic = IconCache.Get(cur, true);
                    int sz = Math.Min(L.R(40), Math.Max(ic.Width, L.R(32)));
                    Rectangle ir = new Rectangle(r.X + (r.Width - sz) / 2, r.Y + L.R(5), sz, sz);
                    DrawImage(g, ic, ir, ia);
                    Rectangle tr = new Rectangle(r.X + L.R(3), ir.Bottom + L.R(3), r.Width - L.R(6), r.Bottom - ir.Bottom - L.R(4));
                    DrawText(g, name, itemFont, tr, StringAlignment.Center, StringAlignment.Near, tc, true);
                    SizeF need = g.MeasureString(name, itemFont, tr.Width);
                    MarkTruncated(i, need.Height > tr.Height + 1 || need.Width > tr.Width + 1);
                }
            }
            finally { if (ia != null) ia.Dispose(); }
        }

        // ================= 文件名太长时：鼠标停留显示全名 =================

        bool[] truncated = new bool[0];
        ToolTip nameTip;
        Timer tipTimer;
        int tipShownFor = -1;

        public bool[] TruncatedFlags() { return (bool[])truncated.Clone(); }

        void MarkTruncated(int i, bool t)
        {
            if (truncated.Length != Data.Items.Count) truncated = new bool[Data.Items.Count];
            if (i >= 0 && i < truncated.Length) truncated[i] = t;
        }

        void HideNameTip()
        {
            if (tipTimer != null) tipTimer.Stop();
            if (nameTip != null && tipShownFor >= 0) { try { nameTip.Hide(this); } catch { } }
            tipShownFor = -1;
        }

        void ScheduleNameTip()
        {
            HideNameTip();
            if (hover < 0) return;
            if (tipTimer == null)
            {
                tipTimer = new Timer();
                tipTimer.Interval = 450;
                tipTimer.Tick += delegate
                {
                    tipTimer.Stop();
                    int i = hover;
                    if (i < 0 || i >= Data.Items.Count || i >= truncated.Length || !truncated[i] || mode != Mode.None) return;
                    if (nameTip == null) { nameTip = new ToolTip(); nameTip.ShowAlways = true; }
                    string full = DesktopHelper.DisplayName(Data.Items[i].Path);
                    if (!DesktopHelper.Exists(Data.Items[i].CurrentPath())) full += T.S("missing");
                    Point p = PointToClient(Cursor.Position);
                    try { nameTip.Show(full, this, p.X + L.R(14), p.Y + L.R(18), 10000); tipShownFor = i; } catch { }
                };
            }
            tipTimer.Start();
        }

        static void DrawImage(Graphics g, Bitmap b, Rectangle r, ImageAttributes ia)
        {
            if (b == null) return;
            if (ia == null) g.DrawImage(b, r);
            else g.DrawImage(b, r, 0, 0, b.Width, b.Height, GraphicsUnit.Pixel, ia);
        }

        static void DrawText(Graphics g, string s, Font f, Rectangle r, StringAlignment h, StringAlignment v, Color c, bool wrapEllipsis)
        {
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = h;
                sf.LineAlignment = v;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                if (!wrapEllipsis || v == StringAlignment.Center) sf.FormatFlags |= StringFormatFlags.NoWrap;
                sf.FormatFlags |= StringFormatFlags.LineLimit;
                RectangleF rf = r;
                using (Brush sh = new SolidBrush(Color.FromArgb(Math.Min(200, (int)c.A), 0, 0, 0)))
                {
                    RectangleF rs = rf; rs.Offset(1, 1);
                    g.DrawString(s, f, sh, rs, sf);
                }
                using (Brush b = new SolidBrush(c)) g.DrawString(s, f, b, rf, sf);
            }
        }

        // ================= 鼠标 =================

        bool InTitle(Point p) { return p.Y < L.TitleH; }

        void ResizeZone(Point p, out bool right, out bool bottom)
        {
            right = bottom = false;
            if (Data.Locked || Data.Collapsed) return;
            int gr = L.Grip;
            bool corner = p.X >= Width - gr * 2 && p.Y >= Height - gr * 2;
            right = corner || p.X >= Width - gr;
            bottom = corner || (p.Y >= Height - gr && p.Y > L.TitleH);
            if (right && p.Y < L.TitleH && !corner) right = false; // 标题栏右边留给按钮
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            HideNameTip();
            if (e.Button != MouseButtons.Left) return;
            downScreen = Cursor.Position;
            downBounds = Bounds;
            int btn = HitButton(e.Location);
            if (btn >= 0) { ClickButton(btn); return; }

            bool rr, rb;
            ResizeZone(e.Location, out rr, out rb);
            if (rr || rb) { mode = Mode.Resize; resizeRight = rr; resizeBottom = rb; return; }

            if (InTitle(e.Location))
            {
                if (!Data.Locked) mode = Mode.Move;
                return;
            }
            int idx = Data.Collapsed ? -1 : L.HitItem(e.Location);
            if (selected != idx) { selected = idx; Render(); }
            if (idx >= 0) { mode = Mode.ItemPending; downItem = idx; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point cur = Cursor.Position;
            int dx = cur.X - downScreen.X, dy = cur.Y - downScreen.Y;
            switch (mode)
            {
                case Mode.Move:
                    Location = new Point(downBounds.X + dx, downBounds.Y + dy);
                    return;
                case Mode.Resize:
                    {
                        int nw = resizeRight ? Math.Max(MinW, downBounds.Width + dx) : Width;
                        int nh = resizeBottom ? Math.Max(MinH, downBounds.Height + dy) : Height;
                        if (nw != Width || nh != Height)
                        {
                            Size = new Size(nw, nh);
                            Render();
                        }
                        return;
                    }
                case Mode.ItemPending:
                    {
                        Size ds = SystemInformation.DragSize;
                        if (Math.Abs(dx) > ds.Width || Math.Abs(dy) > ds.Height)
                        {
                            mode = Mode.None;
                            StartItemDrag(downItem);
                        }
                        return;
                    }
            }

            // 悬停
            bool rr, rb;
            ResizeZone(e.Location, out rr, out rb);
            if (rr && rb) Cursor = Cursors.SizeNWSE;
            else if (rr) Cursor = Cursors.SizeWE;
            else if (rb) Cursor = Cursors.SizeNS;
            else Cursor = Cursors.Default;

            int hb = HitButton(e.Location);
            int hi = Data.Collapsed ? -1 : L.HitItem(e.Location);
            if (hb != hoverButton || hi != hover)
            {
                bool itemChanged = hi != hover;
                hoverButton = hb;
                hover = hi;
                Render();
                if (itemChanged) ScheduleNameTip();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Mode m = mode;
            mode = Mode.None;
            if (m == Mode.Move || m == Mode.Resize)
            {
                Data.X = Left; Data.Y = Top;
                if (!Data.Collapsed) { Data.W = Width; Data.H = Height; }
                else Data.W = Width;
                app.Save();
                return;
            }
            if (e.Button == MouseButtons.Right)
            {
                int idx = Data.Collapsed ? -1 : L.HitItem(e.Location);
                if (idx >= 0)
                {
                    selected = idx; Render();
                    ShowItemMenu(idx);
                }
                else ShowFenceMenu();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            HideNameTip();
            if (hover != -1 || hoverButton != -1) { hover = -1; hoverButton = -1; Render(); }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left) return;
            if (HitButton(e.Location) >= 0) return;
            if (InTitle(e.Location)) { Rename(); return; }
            int idx = Data.Collapsed ? -1 : L.HitItem(e.Location);
            if (idx >= 0) OpenItem(idx);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            HideNameTip();
            if (Data.Collapsed) return;
            SyncLayout();
            int old = L.Scroll;
            L.Scroll -= e.Delta / 120 * L.ItemH;
            L.ClampScroll();
            if (L.Scroll != old) Render();
        }

        void ClickButton(int btn)
        {
            switch (btn)
            {
                case 0: ToggleCollapse(); break;
                case 1: Data.Locked = !Data.Locked; app.Save(); Render(); break;
                case 2: Data.ListMode = !Data.ListMode; L.Scroll = 0; app.Save(); Render(); break;
                case 3:
                    {
                        Rectangle r = ButtonRect(3);
                        app.ShowSettingsMenu(PointToScreen(new Point(r.Left, r.Bottom + L.R(2))));
                        hoverButton = -1; Render();
                        break;
                    }
            }
        }

        void ToggleCollapse()
        {
            Data.Collapsed = !Data.Collapsed;
            hover = -1; selected = -1;
            Size = new Size(Width, Data.Collapsed ? L.TitleH : Math.Max(MinH, Data.H));
            app.Save();
            Render();
        }

        // ================= 菜单与操作 =================

        void ShowFenceMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Items.Add(T.S("rename"), null, delegate { Rename(); });
            m.Items.Add(Data.ListMode ? T.S("toIcon") : T.S("toList"), null, delegate { ClickButton(2); });
            m.Items.Add(Data.Locked ? T.S("unlock") : T.S("lock"), null, delegate { ClickButton(1); });
            m.Items.Add(Data.Collapsed ? T.S("expand") : T.S("collapse"), null, delegate { ToggleCollapse(); });
            m.Items.Add(T.S("opacity"), null, delegate { app.ShowOpacityDialog(); });
            m.Items.Add(T.S("sortName"), null, delegate
            {
                Data.Items.Sort(delegate(ItemData x, ItemData y)
                {
                    return string.Compare(DesktopHelper.DisplayName(x.Path), DesktopHelper.DisplayName(y.Path), StringComparison.CurrentCultureIgnoreCase);
                });
                app.Save(); Render();
            });
            m.Items.Add(T.S("cleanMissing"), null, delegate
            {
                Data.Items.RemoveAll(delegate(ItemData x) { return !DesktopHelper.Exists(x.CurrentPath()); });
                selected = hover = -1;
                app.Save(); Render();
            });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(T.S("newFence"), null, delegate { app.CreateFence(new Point(Right + L.R(16), Top)); });
            m.Items.Add(T.S("deleteFence"), null, delegate { app.DeleteFence(this); });
            ShowMenu(m);
        }

        ShellMenu activeShellMenu;

        void ShowItemMenu(int idx)
        {
            ItemData it = Data.Items[idx];
            HideNameTip();
            string cur = it.CurrentPath();
            if (DesktopHelper.Exists(cur))
            {
                try { ShowShellMenu(it, cur); return; }
                catch (Exception ex) { Log.Write("系统右键菜单失败，改用简单菜单: " + ex.Message); }
            }
            ShowSimpleItemMenu(it);
        }

        /// <summary>和资源管理器一样的文件右键菜单，最上面加本程序的几项</summary>
        void ShowShellMenu(ItemData it, string cur)
        {
            const uint ID_REMOVE = 1, ID_LOCATION = 2;
            bool shift = (ModifierKeys & Keys.Shift) != 0; // 按住 Shift 右键 = 扩展菜单，和资源管理器一样
            using (ShellMenu sm = new ShellMenu(cur, Handle, shift))
            {
                ShellMenu.AppendMenu(sm.HMenu, ShellMenu.MF_STRING, new UIntPtr(ID_REMOVE), T.S("removeItem"));
                if (!it.IsStored())
                    ShellMenu.AppendMenu(sm.HMenu, ShellMenu.MF_STRING, new UIntPtr(ID_LOCATION), T.S("openLocation"));
                ShellMenu.AppendMenu(sm.HMenu, ShellMenu.MF_SEPARATOR, UIntPtr.Zero, null);
                sm.AddShellItems();

                Point pt = Cursor.Position;
                uint cmd;
                activeShellMenu = sm;
                try { cmd = sm.Track(Handle, pt.X, pt.Y); }
                finally { activeShellMenu = null; }
                if (cmd == 0) return;

                if (cmd == ID_REMOVE) { selected = hover = -1; app.RemoveItem(this, it); return; }
                if (cmd == ID_LOCATION)
                {
                    try { Process.Start("explorer.exe", "/select,\"" + cur + "\""); }
                    catch (Exception ex) { MessageBox.Show(ex.Message, "DeskFence"); }
                    return;
                }
                if (cmd < ShellMenu.FirstShellId) return;

                string verb = sm.VerbOf(cmd).ToLowerInvariant();
                if (verb == "rename") { RenameItem(it); return; } // 系统的"重命名"只在资源管理器窗口里有效，这里自己做
                try { sm.Invoke(cmd, Handle, System.IO.Path.GetDirectoryName(cur)); }
                catch (Exception ex) { Log.Write("执行菜单命令失败: " + ex.Message); }
            }
            // 删除、剪切等命令执行后文件可能不在了：稍后检查，不在了就从格子里去掉
            Timer t = new Timer();
            t.Interval = 1500;
            t.Tick += delegate
            {
                t.Stop(); t.Dispose();
                if (!Data.Items.Contains(it)) return;
                if (!DesktopHelper.Exists(it.CurrentPath()))
                {
                    Data.Items.Remove(it);
                    selected = hover = -1;
                    app.Save();
                }
                IconCache.Invalidate(it.CurrentPath());
                Render();
            };
            t.Start();
        }

        /// <summary>重命名格子里的文件（收纳中的文件，放回桌面时也用新名字）</summary>
        void RenameItem(ItemData it)
        {
            string cur = it.CurrentPath();
            string oldName = System.IO.Path.GetFileName(cur.TrimEnd('\\', '/'));
            string ext = System.IO.Path.GetExtension(oldName);
            bool hideExt = ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || ext.Equals(".url", StringComparison.OrdinalIgnoreCase);
            string shown = hideExt ? System.IO.Path.GetFileNameWithoutExtension(oldName) : oldName;
            string s = InputDialog.Ask(T.S("renamePrompt"), shown, Cursor.Position);
            if (s == null) return;
            s = s.Trim();
            if (s.Length == 0 || s == shown) return;
            if (s.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0) { MessageBox.Show(T.S("renameBadName"), "DeskFence"); return; }
            if (hideExt && !s.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) s += ext;
            string target = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(cur), s);
            try
            {
                if (DesktopHelper.Exists(target) && !DesktopHelper.SamePath(target, cur)) { MessageBox.Show(T.S("renameExists"), "DeskFence"); return; }
                if (System.IO.Directory.Exists(cur)) System.IO.Directory.Move(cur, target);
                else System.IO.File.Move(cur, target);
                IconCache.Invalidate(cur);
                if (it.IsStoredPathOf(cur))
                {
                    it.StoredPath = target;
                    it.Path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(it.Path), s);
                }
                else it.Path = target;
                app.Save();
                Render();
            }
            catch (Exception ex) { MessageBox.Show(T.S("renameFailed") + ex.Message, "DeskFence"); }
        }

        void ShowSimpleItemMenu(ItemData it)
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Items.Add(T.S("open"), null, delegate { OpenItem(Data.Items.IndexOf(it)); });
            if (DesktopHelper.Exists(it.CurrentPath())) m.Items.Add(T.S("renameFile"), null, delegate { RenameItem(it); });
            if (!it.IsStored())
            {
                m.Items.Add(T.S("openLocation"), null, delegate
                {
                    try { Process.Start("explorer.exe", "/select,\"" + it.CurrentPath() + "\""); }
                    catch (Exception ex) { MessageBox.Show(ex.Message, "DeskFence"); }
                });
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(T.S("removeItem"), null, delegate
            {
                selected = hover = -1;
                app.RemoveItem(this, it);
            });
            ShowMenu(m);
        }

        void ShowMenu(ContextMenuStrip m)
        {
            m.Closed += delegate { BeginInvoke((MethodInvoker)delegate { m.Dispose(); }); };
            m.Show(Cursor.Position);
        }

        void Rename()
        {
            string s = InputDialog.Ask(T.S("namePrompt"), Data.Title, new Point(Left + L.R(20), Top + L.TitleH));
            if (s == null) return;
            Data.Title = s.Trim();
            Text = "DeskFence - " + Data.Title;
            app.Save();
            Render();
        }

        void OpenItem(int idx)
        {
            if (idx < 0 || idx >= Data.Items.Count) return;
            string p = Data.Items[idx].CurrentPath();
            if (!DesktopHelper.Exists(p)) { MessageBox.Show(T.S("fileMissing") + "\n" + p, "DeskFence"); return; }
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(p);
                psi.UseShellExecute = true;
                try { psi.WorkingDirectory = System.IO.Path.GetDirectoryName(p); } catch { }
                Process.Start(psi);
            }
            catch (Exception ex) { MessageBox.Show(T.S("openFailed") + ex.Message, "DeskFence"); }
        }

        // ================= 拖放 =================

        void StartItemDrag(int idx)
        {
            HideNameTip();
            if (idx < 0 || idx >= Data.Items.Count) return;
            ItemData it = Data.Items[idx];
            DataObject d = new DataObject();
            d.SetData(DragFormat, Data.Id + "|" + it.Path);
            string cur = it.CurrentPath();
            if (DesktopHelper.Exists(cur))
                d.SetData(DataFormats.FileDrop, new string[] { cur });
            droppedOnDesktop = false;
            try
            {
                // 只允许 复制/链接：拖到别的程序（比如微信）可以发送，拖到文件夹是复制，不会把原文件搬走
                DoDragDrop(d, DragDropEffects.Copy | DragDropEffects.Link);
            }
            catch (Exception ex) { Log.Write("拖动失败: " + ex.Message); return; }
            // 在桌面空白处松手 → 移出格子、在桌面显示（这次松手已经拦下，资源管理器不会再复制出一份）
            if (droppedOnDesktop) app.RemoveItem(this, it);
        }

        bool droppedOnDesktop;

        protected override void OnQueryContinueDrag(QueryContinueDragEventArgs e)
        {
            base.OnQueryContinueDrag(e);
            if (e.EscapePressed) return;
            bool leftUp = (e.KeyState & 1) == 0;
            if (leftUp && e.Action != DragAction.Cancel)
            {
                Point pt = Cursor.Position;
                if (!app.IsOverFence(pt) && Controller.IsDesktopAt(pt))
                {
                    // 不让桌面接收这次放下（否则桌面会把同名文件再复制一份：xxx (1).xlsx）
                    droppedOnDesktop = true;
                    e.Action = DragAction.Cancel;
                }
            }
        }

        static bool IsInternal(IDataObject d) { return d != null && d.GetDataPresent(DragFormat); }

        DragDropEffects PickEffect(DragEventArgs e)
        {
            if (IsInternal(e.Data) || e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                // 返回 链接/复制，资源管理器不会做“移动后删除原文件”的动作
                if ((e.AllowedEffect & DragDropEffects.Link) != 0) return DragDropEffects.Link;
                if ((e.AllowedEffect & DragDropEffects.Copy) != 0) return DragDropEffects.Copy;
                return e.AllowedEffect & DragDropEffects.Move;
            }
            return DragDropEffects.None;
        }

        bool renderedDropState;

        protected override void OnDragEnter(DragEventArgs e)
        {
            base.OnDragEnter(e);
            renderedDropState = false;
            e.Effect = PickEffect(e);
            dropActive = e.Effect != DragDropEffects.None;
            UpdateDrop(e);
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            base.OnDragOver(e);
            e.Effect = PickEffect(e);
            UpdateDrop(e);
        }

        void UpdateDrop(DragEventArgs e)
        {
            int idx = -1;
            int oldScroll = L.Scroll;
            if (dropActive && !Data.Collapsed)
            {
                SyncLayout();
                Point p = PointToClient(new Point(e.X, e.Y));
                // 拖到上下边缘时自动滚动
                Rectangle c = L.Content;
                if (p.Y < c.Top + L.R(14) && L.Scroll > 0) { L.Scroll -= L.R(8); L.ClampScroll(); }
                else if (p.Y > c.Bottom - L.R(14) && L.Scroll < L.MaxScroll) { L.Scroll += L.R(8); L.ClampScroll(); }
                idx = L.InsertIndex(p);
            }
            if (idx != dropIndex || L.Scroll != oldScroll || !renderedDropState) { dropIndex = idx; renderedDropState = true; Render(); }
        }

        protected override void OnDragLeave(EventArgs e)
        {
            base.OnDragLeave(e);
            dropActive = false; dropIndex = -1;
            Render();
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            int idx = Data.Collapsed ? Data.Items.Count : dropIndex;
            if (idx < 0) idx = Data.Items.Count;
            dropActive = false; dropIndex = -1;
            try
            {
                if (IsInternal(e.Data))
                {
                    string s = e.Data.GetData(DragFormat) as string;
                    if (s != null)
                    {
                        int bar = s.IndexOf('|');
                        if (bar > 0) app.DropItem(s.Substring(0, bar), s.Substring(bar + 1), this, idx);
                    }
                }
                else
                {
                    string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
                    if (files != null) app.DropFiles(files, this, idx);
                }
            }
            catch (Exception ex) { Log.Write("放入失败: " + ex); }
            Render();
        }

        /// <summary>仅用于生成预览图</summary>
        public Bitmap Preview(int hoverIdx, int dropIdx)
        {
            hover = hoverIdx; selected = -1;
            if (dropIdx >= 0) { dropActive = true; dropIndex = dropIdx; }
            Width = Data.W; Height = Data.Collapsed ? L.TitleH : Data.H;
            SyncLayout();
            Bitmap bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp)) DrawAll(g, Width, Height);
            return bmp;
        }

        public void ResetHover()
        {
            hover = selected = -1;
        }
    }
}
