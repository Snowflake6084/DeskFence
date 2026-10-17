using System;
using System.Drawing;
using System.Windows.Forms;

namespace DeskFence
{
    /// <summary>透明度调整：拖动滑块实时生效，取消则恢复</summary>
    class OpacityDialog : Form
    {
        static OpacityDialog openOne;

        readonly Controller app;
        readonly int oldBg, oldAll;
        TrackBar bg, all;
        Label bgVal, allVal;

        internal OpacityDialog(Controller app)
        {
            this.app = app;
            oldBg = app.Config.BgAlphaPercent;
            oldAll = app.Config.AllAlphaPercent;

            Text = T.S("opacityTitle");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 9f);
            ClientSize = new Size(360, 200);

            int y = 12;
            bg = AddRow(T.S("bgOpacity"), 5, 95, oldBg, ref y, out bgVal);
            all = AddRow(T.S("allOpacity"), 30, 100, oldAll, ref y, out allVal);

            Button ok = new Button();
            ok.Text = T.S("ok"); ok.DialogResult = DialogResult.OK; ok.Location = new Point(192, 164); ok.Width = 75;
            Button cancel = new Button();
            cancel.Text = T.S("cancel"); cancel.DialogResult = DialogResult.Cancel; cancel.Location = new Point(273, 164); cancel.Width = 75;
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;

            bg.ValueChanged += delegate { Apply(); };
            all.ValueChanged += delegate { Apply(); };
        }

        TrackBar AddRow(string label, int min, int max, int value, ref int y, out Label valLabel)
        {
            Label lb = new Label();
            lb.Text = label; lb.AutoSize = true; lb.Location = new Point(12, y);
            Controls.Add(lb);
            TrackBar tb = new TrackBar();
            tb.Minimum = min; tb.Maximum = max;
            tb.TickFrequency = 10; tb.SmallChange = 1; tb.LargeChange = 5;
            tb.Value = Math.Max(min, Math.Min(max, value));
            tb.Location = new Point(8, y + 20); tb.Width = 290;
            Controls.Add(tb);
            Label v = new Label();
            v.AutoSize = true; v.Location = new Point(304, y + 26);
            v.Text = tb.Value + "%";
            Controls.Add(v);
            valLabel = v;
            y += 72;
            return tb;
        }

        void Apply()
        {
            bgVal.Text = bg.Value + "%";
            allVal.Text = all.Value + "%";
            app.Config.BgAlphaPercent = bg.Value;
            app.Config.AllAlphaPercent = all.Value;
            app.RenderAll();
        }

        public static void Open(Controller app)
        {
            if (openOne != null && !openOne.IsDisposed) { openOne.Activate(); return; }
            using (OpacityDialog d = new OpacityDialog(app))
            {
                openOne = d;
                if (d.ShowDialog() == DialogResult.OK)
                {
                    app.Save();
                }
                else
                {
                    app.Config.BgAlphaPercent = d.oldBg;
                    app.Config.AllAlphaPercent = d.oldAll;
                    app.RenderAll();
                }
                openOne = null;
            }
        }
    }
}
