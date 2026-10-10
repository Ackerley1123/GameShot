using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace GameShot
{
    internal sealed class RegionSelector : Form
    {
        private readonly Bitmap snapshot;
        private Point start;
        private Rectangle selection;
        private bool dragging;
        internal Rectangle Selection { get { return selection; } }

        internal RegionSelector(Bitmap snapshot, Rectangle desktop)
        {
            this.snapshot = snapshot;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = desktop;
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            KeyPreview = true;
            Cursor = Cursors.Cross;
            Text = "GameShot — 选择区域";
            AccessibleName = "截图区域选择，拖动鼠标选择，Esc 取消，方向键调整，Enter 确认";
        }

        internal static Rectangle Normalize(Point first, Point last)
        {
            return Rectangle.FromLTRB(Math.Min(first.X, last.X), Math.Min(first.Y, last.Y), Math.Max(first.X, last.X), Math.Max(first.Y, last.Y));
        }
        private Point Clamp(Point p) { return new Point(Math.Max(0, Math.Min(ClientSize.Width, p.X)), Math.Max(0, Math.Min(ClientSize.Height, p.Y))); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right) { DialogResult = DialogResult.Cancel; Close(); return; }
            if (e.Button != MouseButtons.Left) return;
            start = Clamp(e.Location);
            selection = new Rectangle(start, Size.Empty);
            dragging = true;
            Capture = true;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging) return;
            selection = Normalize(start, Clamp(e.Location));
            Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging || e.Button != MouseButtons.Left) return;
            selection = Normalize(start, Clamp(e.Location));
            dragging = false;
            Capture = false;
            if (selection.Width >= 2 && selection.Height >= 2) { DialogResult = DialogResult.OK; Close(); }
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
            else if (e.KeyCode == Keys.Enter && selection.Width >= 2 && selection.Height >= 2) { DialogResult = DialogResult.OK; Close(); }
            else if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                // 键盘也能完成区域截图；Shift + 方向键调整尺寸，普通方向键移动。
                if (selection.IsEmpty) selection = new Rectangle(Math.Max(0, (Width - 320) / 2), Math.Max(0, (Height - 240) / 2), Math.Min(320, Width), Math.Min(240, Height));
                int step = e.Control ? 10 : 1;
                int dx = e.KeyCode == Keys.Left ? -step : e.KeyCode == Keys.Right ? step : 0;
                int dy = e.KeyCode == Keys.Up ? -step : e.KeyCode == Keys.Down ? step : 0;
                if (e.Shift) selection.Size = new Size(Math.Max(2, Math.Min(Width - selection.X, selection.Width + dx)), Math.Max(2, Math.Min(Height - selection.Y, selection.Height + dy)));
                else selection.Location = new Point(Math.Max(0, Math.Min(Width - selection.Width, selection.X + dx)), Math.Max(0, Math.Min(Height - selection.Height, selection.Y + dy)));
                Invalidate();
                e.Handled = true;
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.DrawImageUnscaled(snapshot, Point.Empty);
            using (var dim = new SolidBrush(Color.FromArgb(125, 0, 0, 0))) e.Graphics.FillRectangle(dim, ClientRectangle);
            if (selection.Width > 0 && selection.Height > 0)
            {
                e.Graphics.DrawImage(snapshot, selection, selection, GraphicsUnit.Pixel);
                using (var outline = new Pen(Color.FromArgb(76, 164, 255), 2)) e.Graphics.DrawRectangle(outline, selection);
            }
            string hint = "拖动选择区域 · Esc / 右键取消 · 方向键移动 · Shift 调整尺寸 · Enter 确认";
            if (!selection.IsEmpty) hint += "    " + selection.Width + " × " + selection.Height;
            var screen = Screen.FromPoint(Cursor.Position).Bounds;
            Point anchor = new Point(Math.Max(16, screen.Left - Left + 24), Math.Max(16, screen.Top - Top + 24));
            Size textSize = TextRenderer.MeasureText(hint, Font);
            using (var brush = new SolidBrush(Color.FromArgb(230, 24, 28, 36))) e.Graphics.FillRectangle(brush, anchor.X - 8, anchor.Y - 6, textSize.Width + 16, textSize.Height + 12);
            TextRenderer.DrawText(e.Graphics, hint, Font, anchor, Color.White);
        }
    }
}
