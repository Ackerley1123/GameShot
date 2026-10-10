using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace GameShot
{
    internal static class Artwork
    {
        private static readonly Color[] Colors = { Color.FromArgb(221, 233, 248), Color.FromArgb(230, 224, 247), Color.FromArgb(219, 237, 231), Color.FromArgb(246, 231, 217), Color.FromArgb(225, 231, 240), Color.FromArgb(246, 224, 232) };
        internal static Color ColorFor(string name) { int hash = 0; foreach (char c in name) hash = unchecked(hash * 31 + c); return Colors[(hash & int.MaxValue) % Colors.Length]; }
        internal static Bitmap Thumbnail(string path, int maxWidth, int maxHeight)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var original = Image.FromStream(stream))
                {
                    if (original.Width > 20000 || original.Height > 20000) return null;
                    double ratio = Math.Min(1, Math.Min((double)maxWidth / original.Width, (double)maxHeight / original.Height));
                    var bitmap = new Bitmap(Math.Max(1, (int)(original.Width * ratio)), Math.Max(1, (int)(original.Height * ratio)));
                    using (var g = Graphics.FromImage(bitmap)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(original, new Rectangle(Point.Empty, bitmap.Size)); }
                    // 返回独立副本，关闭源文件，外部移动截图不会被图库占用。
                    return bitmap;
                }
            }
            catch (Exception) { return null; }
        }
        internal static void Cover(Graphics g, Rectangle bounds, Image image, string name)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            var clip = g.Save(); g.SetClip(bounds, CombineMode.Intersect);
            using (var fill = new SolidBrush(ColorFor(name))) g.FillRectangle(fill, bounds);
            if (image != null)
            {
                double ratio = Math.Max((double)bounds.Width / image.Width, (double)bounds.Height / image.Height);
                float sourceW = (float)(bounds.Width / ratio), sourceH = (float)(bounds.Height / ratio);
                g.DrawImage(image, bounds, new RectangleF((image.Width - sourceW) / 2, (image.Height - sourceH) / 2, sourceW, sourceH), GraphicsUnit.Pixel);
            }
            else
            {
                // 无封面时只用柔和色块与几何图案，卡片标题承担识别信息。
                using (var wash = new SolidBrush(Color.FromArgb(78, Color.White))) g.FillEllipse(wash, bounds.X + bounds.Width * .52F, bounds.Y - bounds.Height * .45F, bounds.Width * .8F, bounds.Width * .8F);
                using (var ring = new Pen(Color.FromArgb(80, Color.White), Math.Max(1, bounds.Width / 18F))) g.DrawEllipse(ring, bounds.X - bounds.Width * .15F, bounds.Y + bounds.Height * .35F, bounds.Width * .5F, bounds.Width * .5F);
            }
            g.Restore(clip);
        }
    }

    internal sealed class GameCard : Button
    {
        internal readonly GameEntry Game;
        internal bool Dark;
        private readonly Bitmap image;
        private bool hover;
        internal GameCard(GameEntry game, string cover)
        {
            Game = game; image = Artwork.Thumbnail(cover, 500, 350);
            Text = game.Name; AccessibleName = game.Name + "，打开游戏详情";
            FlatStyle = FlatStyle.Flat; Cursor = Cursors.Hand; DoubleBuffered = true;
            MouseEnter += delegate { hover = true; Invalidate(); }; MouseLeave += delegate { hover = false; Invalidate(); };
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            float p = DeviceDpi / 96F;
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Appearance.Background(Dark));
            var frame = new RectangleF(1, 1, Width - 3, Height - 4);
            using (var path = Appearance.Round(frame, 12 * p))
            {
                using (var fill = new SolidBrush(Appearance.Surface(Dark))) g.FillPath(fill, path);
                var state = g.Save(); g.SetClip(path);
                int posterHeight = (int)(Height * .59);
                var poster = new Rectangle(1, 1, Width - 3, posterHeight);
                Artwork.Cover(g, poster, image, Game.Name);
                if (image == null)
                {
                    using (var font = new Font("Microsoft YaHei UI", 21, FontStyle.Bold))
                        TextRenderer.DrawText(g, Game.Name.Substring(0, Math.Min(2, Game.Name.Length)), font, poster, Color.FromArgb(69, 80, 102), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                using (var title = new Font("Microsoft YaHei UI", 10, FontStyle.Bold))
                    TextRenderer.DrawText(g, Game.Name, title, new Rectangle((int)(13 * p), posterHeight + (int)(11 * p), Width - (int)(26 * p), (int)(24 * p)), Appearance.Text(Dark), TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                using (var small = new Font("Microsoft YaHei UI", 8.5F))
                    TextRenderer.DrawText(g, !Game.TrackTime ? "截图归档" : Game.IsRunning ? "运行中 · " + LibraryStore.Duration(Game.RunningSeconds) : Game.RunningSeconds < 1 ? "尚未游玩" : LibraryStore.Duration(Game.RunningSeconds), small,
                        new Rectangle((int)(13 * p), Height - (int)(31 * p), Width - (int)(26 * p), (int)(22 * p)), Game.IsRunning ? Dark ? Color.FromArgb(125, 220, 159) : Color.FromArgb(32, 121, 75) : Appearance.Muted(Dark), TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                g.Restore(state);
                using (var border = new Pen(hover || Focused ? Appearance.Blue : Appearance.Border(Dark), hover || Focused ? 1.5F * p : 1)) g.DrawPath(border, path);
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) image?.Dispose(); base.Dispose(disposing); }
    }

    internal sealed class GameHero : Control
    {
        private GameEntry game;
        private Bitmap image;
        internal bool Dark;
        internal GameHero() { DoubleBuffered = true; AccessibleName = "游戏详情"; }
        internal void SetGame(GameEntry game, string cover) { this.game = game; image?.Dispose(); image = Artwork.Thumbnail(cover, 600, 600); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (game == null) return;
            float p = DeviceDpi / 96F;
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            var poster = new Rectangle((int)(8 * p), (int)(14 * p), (int)(76 * p), (int)(76 * p));
            using (var path = Appearance.Round(poster, 16 * p))
            {
                var state = g.Save(); g.SetClip(path); Artwork.Cover(g, poster, image, game.Name);
                if (image == null) using (var font = new Font("Microsoft YaHei UI", 19, FontStyle.Bold)) TextRenderer.DrawText(g, game.Name.Substring(0, Math.Min(2, game.Name.Length)), font, poster, Color.FromArgb(69, 80, 102), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                g.Restore(state);
            }
            using (var title = new Font("Microsoft YaHei UI", 22, FontStyle.Bold))
                TextRenderer.DrawText(g, game.Name, title, new Rectangle((int)(104 * p), (int)(19 * p), Width - (int)(120 * p), (int)(44 * p)), Appearance.Text(Dark), TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            using (var small = new Font("Microsoft YaHei UI", 9))
                TextRenderer.DrawText(g, game.IsRunning ? "正在运行 · 自动记录时长" : game.TrackTime ? "时长自动记录 · 截图按日期保存" : "本地截图 · 按日期归档", small, new Rectangle((int)(106 * p), (int)(68 * p), Width - (int)(120 * p), (int)(24 * p)), Appearance.Muted(Dark), TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
        protected override void Dispose(bool disposing) { if (disposing) image?.Dispose(); base.Dispose(disposing); }
    }

    internal sealed class ScreenshotTile : Button
    {
        private readonly string file;
        private readonly string caption;
        private readonly Bitmap image;
        internal bool Dark;
        private bool hover;
        internal ScreenshotTile(string file, Bitmap image)
        {
            this.file = file; this.image = image; caption = File.GetLastWriteTime(file).ToString("MM月dd日  HH:mm"); AccessibleName = "打开截图 " + Path.GetFileName(file); Cursor = Cursors.Hand; DoubleBuffered = true;
            MouseEnter += delegate { hover = true; Invalidate(); }; MouseLeave += delegate { hover = false; Invalidate(); };
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            float p = DeviceDpi / 96F;
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Appearance.Background(Dark));
            using (var path = Appearance.Round(new RectangleF(1, 1, Width - 3, Height - 3), 10 * p))
            {
                var state = g.Save(); g.SetClip(path);
                using (var fill = new SolidBrush(Appearance.Surface(Dark))) g.FillPath(fill, path);
                var target = new Rectangle(1, 1, Width - 3, Height - (int)(32 * p));
                if (image != null) { using (var fill = new SolidBrush(Dark ? Color.FromArgb(29, 29, 33) : Color.FromArgb(234, 236, 240))) g.FillRectangle(fill, target); double ratio = Math.Min((double)target.Width / image.Width, (double)target.Height / image.Height); int w = (int)(image.Width * ratio), h = (int)(image.Height * ratio); g.DrawImage(image, new Rectangle((Width - w) / 2, (target.Height - h) / 2, w, h)); }
                else TextRenderer.DrawText(g, "图片无法读取", Font, target, Appearance.Muted(Dark), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                using (var font = new Font("Microsoft YaHei UI", 8.5F)) TextRenderer.DrawText(g, caption, font, new Rectangle((int)(10 * p), target.Bottom + (int)(5 * p), Width - (int)(20 * p), (int)(24 * p)), Appearance.Muted(Dark), TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                g.Restore(state);
                using (var border = new Pen(hover || Focused ? Appearance.Blue : Appearance.Border(Dark))) g.DrawPath(border, path);
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) { image?.Dispose(); ContextMenuStrip?.Dispose(); } base.Dispose(disposing); }
    }
}
