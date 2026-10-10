using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GameShot
{
    // 所有页面共用颜色与圆角，避免按钮、列表和图库各自形成一套视觉风格。
    internal static class Appearance
    {
        internal static Color Background(bool dark) => dark ? Color.FromArgb(24, 24, 27) : Color.FromArgb(245, 245, 247);
        internal static Color Surface(bool dark) => dark ? Color.FromArgb(35, 35, 39) : Color.White;
        internal static Color Text(bool dark) => dark ? Color.FromArgb(244, 244, 246) : Color.FromArgb(29, 29, 31);
        internal static Color Muted(bool dark) => dark ? Color.FromArgb(166, 166, 177) : Color.FromArgb(105, 105, 115);
        internal static Color Border(bool dark) => dark ? Color.FromArgb(62, 62, 69) : Color.FromArgb(224, 225, 230);
        internal static Color Blue = Color.FromArgb(0, 102, 214);
        internal static Color Selection(bool dark) => dark ? Color.FromArgb(39, 59, 84) : Color.FromArgb(227, 237, 251);
        internal static GraphicsPath Round(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            if (d <= 0) return path;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure(); return path;
        }
        internal static void Icon(Graphics g, RectangleF box, string kind, Color color)
        {
            var state = g.Save();
            g.TranslateTransform(box.X, box.Y); g.ScaleTransform(box.Width / 24, box.Height / 24);
            using (var pen = new Pen(color, 1.6F) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                if (kind == "folder") { g.DrawLines(pen, new[] { new PointF(3, 7), new PointF(3, 19), new PointF(21, 19), new PointF(21, 7), new PointF(11, 7), new PointF(9, 4), new PointF(3, 4), new PointF(3, 7) }); }
                else if (kind == "camera") { g.DrawLines(pen, new[] { new PointF(3, 7), new PointF(7, 7), new PointF(9, 4), new PointF(15, 4), new PointF(17, 7), new PointF(21, 7), new PointF(21, 19), new PointF(3, 19), new PointF(3, 7) }); g.DrawEllipse(pen, 8, 9, 8, 8); }
                else if (kind == "plus") { g.DrawLine(pen, 12, 5, 12, 19); g.DrawLine(pen, 5, 12, 19, 12); }
                else if (kind == "back") { g.DrawLines(pen, new[] { new PointF(14, 5), new PointF(7, 12), new PointF(14, 19) }); }
                else if (kind == "refresh") { g.DrawArc(pen, 5, 5, 14, 14, 35, 285); g.DrawLines(pen, new[] { new PointF(16, 3), new PointF(20, 7), new PointF(15, 8) }); }
                else if (kind == "play") { g.DrawPolygon(pen, new[] { new PointF(8, 5), new PointF(19, 12), new PointF(8, 19) }); }
                else if (kind == "grid") { for (int i = 0; i < 4; i++) g.DrawRectangle(pen, 4 + i % 2 * 10, 4 + i / 2 * 10, 6, 6); }
            }
            g.Restore(state);
        }
    }

    // 保留原生 Button 的键盘、焦点和辅助功能语义，只统一绘制外观。
    internal sealed class ModernButton : Button
    {
        internal bool Dark, Primary, Quiet, Selected;
        internal string Glyph = "";
        private bool hover;
        internal ModernButton()
        {
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand; DoubleBuffered = true;
            MouseEnter += delegate { hover = true; Invalidate(); };
            MouseLeave += delegate { hover = false; Invalidate(); };
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            float p = DeviceDpi / 96F;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent?.BackColor ?? Appearance.Background(Dark));
            Color fill = Primary ? Appearance.Blue : Selected ? Appearance.Selection(Dark) : Quiet ? Parent?.BackColor ?? Appearance.Background(Dark) : Appearance.Surface(Dark);
            if (hover && Enabled) fill = Primary ? Color.FromArgb(0, 89, 191) : Selected ? Appearance.Selection(Dark) : Dark ? Color.FromArgb(47, 47, 54) : Color.FromArgb(236, 237, 242);
            Color text = !Enabled ? Appearance.Muted(Dark) : Primary ? Color.White : Selected ? Dark ? Color.FromArgb(139, 193, 255) : Appearance.Blue : Appearance.Text(Dark);
            var bounds = new RectangleF(1, 1, Width - 3, Height - 3);
            using (var path = Appearance.Round(bounds, 8 * p))
            using (var brush = new SolidBrush(fill))
            {
                e.Graphics.FillPath(brush, path);
                if (!Quiet && !Primary && !Selected) using (var pen = new Pen(Appearance.Border(Dark))) e.Graphics.DrawPath(pen, path);
                if (Focused && ShowFocusCues) using (var pen = new Pen(Appearance.Blue, 2 * p)) e.Graphics.DrawPath(pen, path);
            }
            var label = new Rectangle((int)(8 * p), 0, Width - (int)(16 * p), Height);
            if (Glyph != "") { Appearance.Icon(e.Graphics, new RectangleF(12 * p, (Height - 16 * p) / 2, 16 * p, 16 * p), Glyph, text); label.X += (int)(22 * p); label.Width -= (int)(22 * p); }
            TextRenderer.DrawText(e.Graphics, Text, Font, label, text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    // 包住原生文本框，输入、选择、快捷键处理仍交给系统；外框负责圆角与焦点提示。
    internal sealed class FieldFrame : Panel
    {
        internal bool Dark;
        private readonly TextBox field;
        internal FieldFrame(TextBox field)
        {
            this.field = field; DoubleBuffered = true; Padding = Padding.Empty;
            field.BorderStyle = BorderStyle.None; field.Dock = DockStyle.None;
            Controls.Add(field);
            field.Enter += delegate { Invalidate(); }; field.Leave += delegate { Invalidate(); };
            Resize += delegate { PlaceField(); }; field.FontChanged += delegate { PlaceField(); };
        }
        private void PlaceField()
        {
            int inset = (int)(10 * DeviceDpi / 96F);
            field.SetBounds(inset, Math.Max(2, (Height - field.PreferredHeight) / 2), Math.Max(1, Width - inset * 2), field.PreferredHeight);
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            float p = DeviceDpi / 96F;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent?.BackColor ?? Appearance.Background(Dark));
            using (var path = Appearance.Round(new RectangleF(1, 1, Width - 3, Height - 3), 8 * p))
            {
                using (var fill = new SolidBrush(Appearance.Surface(Dark))) e.Graphics.FillPath(fill, path);
                using (var border = new Pen(field.Focused ? Appearance.Blue : Appearance.Border(Dark), field.Focused ? 1.5F * p : 1)) e.Graphics.DrawPath(border, path);
            }
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); PlaceField(); }
    }

    internal sealed class AppBrand : Control
    {
        internal bool Dark;
        internal AppBrand() { DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            float p = DeviceDpi / 96F;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Appearance.Round(new RectangleF(0, (Height - 32 * p) / 2, 32 * p, 32 * p), 9 * p))
            using (var brush = new SolidBrush(Appearance.Blue)) e.Graphics.FillPath(brush, path);
            Appearance.Icon(e.Graphics, new RectangleF(6 * p, (Height - 20 * p) / 2, 20 * p, 20 * p), "camera", Color.White);
            using (var font = new Font("Segoe UI", 14, FontStyle.Bold)) TextRenderer.DrawText(e.Graphics, "GameShot", font, new Rectangle((int)(43 * p), 0, Width - (int)(43 * p), Height), Appearance.Text(Dark), TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    // 页面仍由 TabControl 管理焦点与切换；导航栏由上方按钮绘制，隐藏旧式标签边框。
    internal sealed class PageTabs : TabControl
    {
        internal PageTabs() { Appearance = TabAppearance.FlatButtons; ItemSize = new Size(1, 1); SizeMode = TabSizeMode.Fixed; Multiline = true; }
        public override Rectangle DisplayRectangle => ClientRectangle;
        protected override void WndProc(ref Message m) { if (m.Msg == 0x1328) { m.Result = IntPtr.Zero; return; } base.WndProc(ref m); }
    }
}
