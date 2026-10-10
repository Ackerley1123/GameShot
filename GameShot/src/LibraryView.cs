using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GameShot
{
    internal sealed class LibraryView : UserControl
    {
        private readonly LibraryStore store;
        private readonly Func<Settings> settings;
        private readonly SplitContainer split = new SplitContainer();
        private readonly ListBox navigation = new ListBox();
        private readonly TextBox search = new TextBox();
        private readonly Label heading = new Label(), libraryCount = new Label(), galleryHeading = new Label(), galleryHint = new Label();
        private readonly FlowLayoutPanel cards = new FlowLayoutPanel(), gallery = new FlowLayoutPanel();
        private readonly TableLayoutPanel detail = new TableLayoutPanel();
        private readonly GameHero hero = new GameHero();
        private readonly Label runningTime = new Label(), activeTime = new Label(), sessionTime = new Label(), lastPlayed = new Label();
        private readonly ModernButton play = new ModernButton { Primary = true, Glyph = "play" };
        private Button back;
        private readonly List<Button> commands = new List<Button>();
        private GameEntry selected;
        private bool rebuilding, dark = true;
        private int galleryGeneration;
        internal event Action<GameEntry> GameConfigured;
        internal event Action<string> Status;
        private Color background, surface, foreground, muted, accent;

        internal LibraryView(LibraryStore store, Func<Settings> settings)
        {
            this.store = store;
            this.settings = settings;
            Dock = DockStyle.Fill;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            split.Dock = DockStyle.Fill;
            split.FixedPanel = FixedPanel.Panel1;
            split.SplitterWidth = 1;
            split.Size = new Size(1050, 580);
            split.SplitterDistance = 205;
            split.Panel1MinSize = 175;
            split.Panel2MinSize = 400;
            Controls.Add(split);
            BuildSidebar();
            BuildContent();
            Load += delegate { PerformAutoScale(); split.SplitterDistance = P(204); Rebuild(); SetTheme(dark); };
        }
        private int P(int value) { return (int)Math.Round(value * DeviceDpi / 96.0); }
        private static Label TextLabel(string text, float size, FontStyle style = FontStyle.Regular)
        {
            return new Label { Text = text, Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei UI", size, style), AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        }
        private Button Command(string text, Action action, int width = 106)
        {
            var button = new ModernButton { Text = text, Width = width, Height = 34, Margin = new Padding(0, 4, 8, 4), Font = new Font("Microsoft YaHei UI", 9) };
            button.Click += delegate { action(); };
            commands.Add(button);
            return button;
        }
        private void BuildSidebar()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(6, 16, 18, 8) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            libraryCount.Text = "我的游戏";
            libraryCount.Dock = DockStyle.Fill;
            libraryCount.Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold);
            libraryCount.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(libraryCount, 0, 0);
            search.PlaceholderText = "搜索游戏名称";
            search.BorderStyle = BorderStyle.FixedSingle;
            search.Font = new Font("Microsoft YaHei UI", 9);
            search.Dock = DockStyle.Fill;
            search.AccessibleName = "搜索游戏库";
            search.TextChanged += delegate { Rebuild(); };
            var searchRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            searchRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            searchRow.Controls.Add(TextLabel("搜索", 9), 0, 0);
            searchRow.Controls.Add(new FieldFrame(search) { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 7) }, 1, 0);
            layout.Controls.Add(searchRow, 0, 1);
            var all = Command("全部游戏与归档", delegate { selected = null; navigation.SelectedIndex = -1; ShowCards(); });
            ((ModernButton)all).Glyph = "grid";
            ((ModernButton)all).Quiet = true;
            all.Dock = DockStyle.Fill;
            layout.Controls.Add(all, 0, 2);
            navigation.Dock = DockStyle.Fill;
            navigation.BorderStyle = BorderStyle.None;
            navigation.DrawMode = DrawMode.OwnerDrawFixed;
            navigation.IntegralHeight = false;
            navigation.ItemHeight = 50;
            navigation.AccessibleName = "游戏列表";
            navigation.DrawItem += DrawNavigation;
            navigation.SelectedIndexChanged += delegate { if (!rebuilding && navigation.SelectedItem is GameEntry) ShowGame((GameEntry)navigation.SelectedItem); };
            layout.Controls.Add(navigation, 0, 3);
            var note = TextLabel("时长自动记录\n关闭窗口后仍在托盘运行", 8F);
            layout.Controls.Add(note, 0, 4);
            split.Panel1.Controls.Add(layout);
        }
        private void BuildContent()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(24, 12, 8, 8) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 324));
            toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            heading.Dock = DockStyle.Fill;
            heading.Font = new Font("Microsoft YaHei UI", 18, FontStyle.Bold);
            heading.TextAlign = ContentAlignment.MiddleLeft;
            toolbar.Controls.Add(heading, 0, 0);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.RightToLeft };
            actions.Padding = new Padding(0, 16, 0, 0);
            var addGame = (ModernButton)Command("添加游戏", delegate { EditGame(null); }, 110); addGame.Glyph = "plus"; addGame.Primary = true;
            actions.Controls.Add(addGame);
            var refresh = (ModernButton)Command("刷新", delegate { if (selected != null) _ = LoadGallery(selected); else Rebuild(); }, 82); refresh.Glyph = "refresh"; refresh.Quiet = true;
            actions.Controls.Add(refresh);
            back = Command("返回", delegate { selected = null; ShowCards(); }, 82); ((ModernButton)back).Glyph = "back"; ((ModernButton)back).Quiet = true;
            actions.Controls.Add(back);
            toolbar.Controls.Add(actions, 1, 0);
            layout.Controls.Add(toolbar, 0, 0);
            var body = new Panel { Dock = DockStyle.Fill };
            cards.Dock = DockStyle.Fill;
            cards.AutoScroll = true;
            cards.Padding = new Padding(4, 8, 0, 8);
            cards.WrapContents = true;
            cards.AccessibleName = "游戏封面卡片";
            body.Controls.Add(cards);
            detail.Dock = DockStyle.Fill;
            detail.ColumnCount = 1;
            detail.RowCount = 6;
            detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
            detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
            detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            detail.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            hero.Dock = DockStyle.Fill;
            detail.Controls.Add(hero, 0, 0);
            var stats = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Padding = new Padding(8, 10, 8, 8), Tag = "surface" };
            foreach (string caption in new[] { "累计时长", "前台时间", "本次会话", "最近游玩" })
            {
                int index = stats.ColumnStyles.Count;
                stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
                var block = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
                block.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
                block.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
                block.Controls.Add(TextLabel(caption, 8.5F), 0, 0);
                Label value = new[] { runningTime, activeTime, sessionTime, lastPlayed }[index];
                value.Dock = DockStyle.Fill;
                value.Font = new Font("Microsoft YaHei UI", index == 3 ? 11 : 15, FontStyle.Bold);
                value.AutoEllipsis = true;
                block.Controls.Add(value, 0, 1);
                stats.Controls.Add(block, index, 0);
            }
            detail.Controls.Add(stats, 0, 1);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(8, 4, 0, 0) };
            play.Text = "启动游戏";
            play.Size = new Size(144, 36);
            play.Margin = new Padding(0, 4, 12, 4);
            play.FlatStyle = FlatStyle.Flat;
            play.Cursor = Cursors.Hand;
            play.Click += delegate { LaunchGame(); };
            buttons.Controls.Add(play);
            var openFolder = (ModernButton)Command("截图文件夹", delegate { OpenGameFolder(); }, 136); openFolder.Glyph = "folder";
            buttons.Controls.Add(openFolder);
            buttons.Controls.Add(Command("编辑游戏", delegate { if (selected != null) EditGame(selected); }, 104));
            detail.Controls.Add(buttons, 0, 2);
            galleryHeading.Dock = DockStyle.Fill;
            galleryHeading.TextAlign = ContentAlignment.MiddleLeft;
            galleryHeading.Font = new Font("Microsoft YaHei UI", 11, FontStyle.Bold);
            galleryHeading.Padding = new Padding(10, 0, 0, 0);
            detail.Controls.Add(galleryHeading, 0, 3);
            gallery.Dock = DockStyle.Fill;
            gallery.AutoScroll = true;
            gallery.Padding = new Padding(8, 4, 0, 0);
            gallery.WrapContents = true;
            gallery.AccessibleName = "游戏截图缩略图，点击打开图片";
            detail.Controls.Add(gallery, 0, 4);
            galleryHint.Dock = DockStyle.Fill;
            galleryHint.Font = new Font("Microsoft YaHei UI", 8.5F);
            galleryHint.Padding = new Padding(10, 0, 0, 0);
            galleryHint.AutoEllipsis = true;
            detail.Controls.Add(galleryHint, 0, 5);
            detail.Visible = false;
            body.Controls.Add(detail);
            layout.Controls.Add(body, 0, 1);
            split.Panel2.Controls.Add(layout);
        }

        internal void SetTheme(bool dark)
        {
            this.dark = dark;
            background = Appearance.Background(dark);
            surface = Appearance.Surface(dark);
            foreground = Appearance.Text(dark);
            muted = Appearance.Muted(dark);
            accent = Appearance.Selection(dark);
            Action<Control> paint = null;
            paint = control => {
                control.BackColor = control is TextBox || Equals(control.Tag, "surface") || Equals(control.Parent?.Tag, "surface") || Equals(control.Parent?.Parent?.Tag, "surface") ? surface : background;
                control.ForeColor = foreground;
                if (control is FieldFrame field) { field.Dark = dark; field.Invalidate(); }
                foreach (Control child in control.Controls) paint(child);
            };
            paint(this);
            foreach (ModernButton command in commands) { command.Dark = dark; command.Invalidate(); }
            play.Dark = dark; play.Invalidate();
            hero.Dark = dark; hero.Invalidate();
            navigation.BackColor = background;
            navigation.ItemHeight = P(48);
            libraryCount.ForeColor = muted;
            galleryHint.ForeColor = muted;
            foreach (GameCard card in cards.Controls.OfType<GameCard>()) card.Dark = dark;
            foreach (ScreenshotTile tile in gallery.Controls.OfType<ScreenshotTile>()) tile.Dark = dark;
            navigation.Invalidate();
            cards.Invalidate(true);
        }

        private void DrawNavigation(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= navigation.Items.Count) return;
            var game = (GameEntry)navigation.Items[e.Index];
            bool current = (e.State & DrawItemState.Selected) != 0;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var fill = new SolidBrush(background)) e.Graphics.FillRectangle(fill, e.Bounds);
            if (current) using (var path = Appearance.Round(new RectangleF(e.Bounds.X, e.Bounds.Y + P(2), e.Bounds.Width - P(4), e.Bounds.Height - P(4)), P(8))) using (var fill = new SolidBrush(accent)) e.Graphics.FillPath(fill, path);
            if (game.IsRunning) using (var marker = new SolidBrush(Color.FromArgb(49, 149, 86))) e.Graphics.FillEllipse(marker, e.Bounds.Right - P(16), e.Bounds.Top + P(12), P(5), P(5));
            var title = new Rectangle(e.Bounds.X + P(10), e.Bounds.Y + P(6), e.Bounds.Width - P(18), P(22));
            using (var font = new Font("Microsoft YaHei UI", 9)) TextRenderer.DrawText(e.Graphics, game.Name, font, title, current ? dark ? Color.FromArgb(155, 198, 255) : Appearance.Blue : foreground, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            using (var small = new Font(Font.FontFamily, 8F))
                TextRenderer.DrawText(e.Graphics, !game.TrackTime ? "截图归档" : game.IsRunning ? "运行中 · " + LibraryStore.Duration(game.RunningSeconds) : game.RunningSeconds < 1 ? "尚未游玩" : LibraryStore.Duration(game.RunningSeconds), small, new Rectangle(title.X, e.Bounds.Y + P(27), title.Width, P(17)), muted, TextFormatFlags.EndEllipsis);
            if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
        }

        internal void Rebuild()
        {
            if (IsDisposed) return;
            rebuilding = true;
            string selectedId = selected == null ? null : selected.Id;
            navigation.BeginUpdate(); navigation.Items.Clear();
            foreach (var game in store.Data.Games.Where(g => g.Name.IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(g => g.Name)) navigation.Items.Add(game);
            if (selectedId != null) for (int i = 0; i < navigation.Items.Count; i++) if (((GameEntry)navigation.Items[i]).Id == selectedId) navigation.SelectedIndex = i;
            navigation.EndUpdate(); rebuilding = false;
            libraryCount.Text = "游戏与归档  /  " + store.Data.Games.Count;
            ClearControls(cards);
            foreach (GameEntry game in navigation.Items)
            {
                var card = new GameCard(game, CoverPath(game)) { Size = new Size(P(174), P(160)), Margin = new Padding(P(4), P(4), P(14), P(8)), Dark = dark };
                card.Click += delegate { SelectGame(game); };
                cards.Controls.Add(card);
            }
            if (cards.Controls.Count == 0) cards.Controls.Add(TextLabel("没有匹配的游戏。点击“添加游戏”创建条目。", 11));
            if (selected == null) ShowCards(); else { heading.Text = "游戏详情"; RefreshStats(); }
        }
        private string CoverPath(GameEntry game)
        {
            if (string.IsNullOrEmpty(game.Cover) || game.Cover != Path.GetFileName(game.Cover)) return null;
            return Path.Combine(store.Folder, "covers", game.Cover);
        }
        private void SelectGame(GameEntry game)
        {
            int index = navigation.Items.IndexOf(game);
            rebuilding = true;
            if (index >= 0) navigation.SelectedIndex = index;
            rebuilding = false;
            ShowGame(game);
        }
        private void ShowCards()
        {
            selected = null;
            rebuilding = true; navigation.SelectedIndex = -1; rebuilding = false;
            galleryGeneration++;
            heading.Text = "游戏库";
            back.Visible = false;
            detail.Visible = false;
            cards.Visible = true;
            cards.BringToFront();
        }
        internal void ShowGame(GameEntry game)
        {
            selected = game;
            heading.Text = "游戏详情";
            back.Visible = true;
            cards.Visible = false;
            detail.Visible = true;
            detail.BringToFront();
            hero.SetGame(game, CoverPath(game));
            RefreshStats();
            _ = LoadGallery(game);
        }
        internal void PreviewGame(string name)
        {
            GameEntry game = store.Find(name);
            if (game != null) ShowGame(game);
        }
        internal void RefreshStats()
        {
            navigation.Invalidate();
            foreach (GameCard card in cards.Controls.OfType<GameCard>()) card.Invalidate();
            if (selected == null) return;
            runningTime.Text = selected.TrackTime ? LibraryStore.Duration(selected.RunningSeconds) : "不计时";
            activeTime.Text = selected.TrackTime ? LibraryStore.Duration(selected.ForegroundSeconds) : "不计时";
            sessionTime.Text = selected.TrackTime ? LibraryStore.Duration(selected.SessionSeconds) : "不计时";
            lastPlayed.Text = selected.LastPlayedUtc.HasValue ? selected.LastPlayedUtc.Value.ToLocalTime().ToString("MM-dd HH:mm") : selected.TrackTime ? "尚未游玩" : "截图归档";
            play.Text = string.IsNullOrEmpty(selected.Executable) ? "关联启动文件" : selected.IsRunning ? "游戏已在运行" : "启动游戏";
            play.Enabled = !selected.IsRunning;
            hero.Invalidate();
        }

        private async Task LoadGallery(GameEntry game)
        {
            int generation = ++galleryGeneration;
            string root = settings().OutputFolder;
            galleryHeading.Text = "截图 · 正在读取…";
            galleryHint.Text = "点击图片打开；右键可定位文件。";
            ClearControls(gallery);
            List<KeyValuePair<string, Bitmap>> thumbnails = null;
            try
            {
                var result = await Task.Run(() => {
                    List<string> files = store.ScreenshotFiles(game, root);
                    var images = new List<KeyValuePair<string, Bitmap>>();
                    foreach (string path in files.Take(120)) images.Add(new KeyValuePair<string, Bitmap>(path, Artwork.Thumbnail(path, 320, 200)));
                    return new KeyValuePair<int, List<KeyValuePair<string, Bitmap>>>(files.Count, images);
                });
                thumbnails = result.Value;
                if (IsDisposed || generation != galleryGeneration || selected != game) return;
                galleryHeading.Text = "截图  ·  " + result.Key + " 张";
                if (result.Key == 0)
                {
                    var empty = TextLabel("还没有截图\n进入游戏后按 " + settings().Hotkeys[0] + "，画面会自动出现在这里。", 11);
                    empty.Size = new Size(P(460), P(72)); empty.Dock = DockStyle.None;
                    gallery.Controls.Add(empty);
                }
                foreach (var item in thumbnails)
                {
                    var tile = new ScreenshotTile(item.Key, item.Value) { Size = new Size(P(164), P(120)), Margin = new Padding(0, 0, P(12), P(12)), Dark = dark };
                    tile.Click += delegate { Shell(item.Key); };
                    var context = new ContextMenuStrip();
                    context.Items.Add("打开图片", null, delegate { Shell(item.Key); });
                    context.Items.Add("在文件夹中查看", null, delegate { Reveal(item.Key); });
                    context.Items.Add("复制文件路径", null, delegate { try { Clipboard.SetText(item.Key); } catch (Exception ex) { Notify(ex.Message); } });
                    tile.ContextMenuStrip = context;
                    gallery.Controls.Add(tile);
                }
                // 缩略图的所有权移交给控件；切换游戏或关闭窗口时统一释放。
                thumbnails = null;
                galleryHint.Text = result.Key > 120 ? "显示最新 120 张；在文件夹中查看全部。" : "点击查看原图，右键定位文件。";
                SetTheme(dark);
            }
            catch (Exception ex) { if (!IsDisposed && generation == galleryGeneration) { galleryHeading.Text = "截图读取失败"; galleryHint.Text = ex.Message; Notify(ex.Message); } }
            finally { if (thumbnails != null) foreach (var item in thumbnails) if (item.Value != null) item.Value.Dispose(); }
        }
        internal void ScreenshotSaved(string category)
        {
            GameEntry game = store.Ensure(category, false);
            Rebuild();
            if (selected == game) _ = LoadGallery(game);
        }
        private void LaunchGame()
        {
            if (selected == null) return;
            if (string.IsNullOrEmpty(selected.Executable)) { EditGame(selected); return; }
            try
            {
                if (!File.Exists(selected.Executable)) throw new FileNotFoundException("启动文件已移动或不存在，请重新关联。", selected.Executable);
                Process.Start(new ProcessStartInfo(selected.Executable) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(selected.Executable) });
                Notify("已启动 " + selected.Name + "，检测到游戏进程后自动计时。");
            }
            catch (Exception ex) { Notify("无法启动游戏：" + ex.Message); }
        }
        private void EditGame(GameEntry game)
        {
            using (var dialog = new GameEditDialog(game, dark))
            {
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
                if (store.Data.Games.Any(g => g != game && string.Equals(g.Name, dialog.GameName, StringComparison.OrdinalIgnoreCase))) { Notify("已有同名游戏，请在侧栏选择它后编辑。"); return; }
                if (store.Data.Games.Any(g => g != game && g.Processes.Intersect(dialog.ProcessNames, StringComparer.OrdinalIgnoreCase).Any())) { Notify("这个进程已关联其他游戏，请先编辑原条目。"); return; }
                try
                {
                    if (game == null) game = store.Ensure(dialog.GameName, true);
                    game.Name = dialog.GameName;
                    game.Executable = dialog.Executable;
                    game.Processes = dialog.ProcessNames;
                    game.TrackTime = true;
                    if (!game.ScreenshotFolders.Contains(Archive.SafeName(game.Name), StringComparer.OrdinalIgnoreCase)) game.ScreenshotFolders.Add(Archive.SafeName(game.Name));
                    if (!string.IsNullOrEmpty(dialog.CoverFile))
                    {
                        Bitmap image = Artwork.Thumbnail(dialog.CoverFile, 1600, 1000);
                        if (image == null) throw new InvalidDataException("封面图片无法读取。");
                        Directory.CreateDirectory(Path.Combine(store.Folder, "covers"));
                        using (image) image.Save(Path.Combine(store.Folder, "covers", game.Id + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                        game.Cover = game.Id + ".png";
                    }
                    if (GameConfigured != null) GameConfigured(game);
                    store.Save();
                    selected = game;
                    Rebuild();
                    ShowGame(game);
                    Notify("游戏已保存。时长从 GameShot 开始识别进程后记录。");
                }
                catch (Exception ex) { Notify("游戏保存失败：" + ex.Message); }
            }
        }
        private void OpenGameFolder()
        {
            if (selected == null) return;
            try
            {
                string root = settings().OutputFolder;
                string path = Path.Combine(root, Archive.SafeName(selected.Name));
                if (!Directory.Exists(path)) path = selected.ScreenshotFolders.Select(f => Path.Combine(root, Archive.SafeName(f))).FirstOrDefault(Directory.Exists) ?? path;
                Directory.CreateDirectory(path); Shell(path);
            }
            catch (Exception ex) { Notify(ex.Message); }
        }
        private void Shell(string path) { try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception ex) { Notify(ex.Message); } }
        private void Reveal(string path) { try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true }); } catch (Exception ex) { Notify(ex.Message); } }
        private void Notify(string text) { if (Status != null) Status(text); }
        private static void ClearControls(Control parent) { foreach (Control child in parent.Controls.Cast<Control>().ToArray()) { parent.Controls.Remove(child); child.Dispose(); } }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { galleryGeneration++; ClearControls(cards); ClearControls(gallery); }
            base.Dispose(disposing);
        }
    }
}
