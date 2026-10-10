using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GameShot
{
    internal sealed class MainForm : Form
    {
        private Settings settings;
        private readonly NotifyIcon tray;
        private readonly System.Windows.Forms.Timer tracking = new System.Windows.Forms.Timer { Interval = 300 };
        private readonly Label category = new Label(), processLabel = new Label(), status = new Label();
        private readonly PageTabs tabs = new PageTabs();
        private readonly AppBrand brand = new AppBrand();
        private readonly List<ModernButton> sections = new List<ModernButton>();
        private readonly ListView recent = new ListView();
        private readonly DataGridView aliases = new DataGridView();
        private readonly TextBox folder = new TextBox(), fixedCategory = new TextBox();
        private readonly CheckBox pin = new CheckBox(), clipboard = new CheckBox(), notifications = new CheckBox();
        private int selectedTheme;
        private readonly ModernButton saveButton = new ModernButton { Primary = true };
        private readonly List<Button> captureButtons = new List<Button>();
        private readonly HotkeyBox[] hotkeyBoxes = new HotkeyBox[4];
        private readonly string[] modeNames = { "当前屏幕", "当前窗口", "框选区域", "全部显示器" };
        private readonly bool[] registered = new bool[4];
        private readonly LibraryStore gameStore;
        private readonly LibraryView library;
        private readonly PlaytimeTracker playtime;
        private Target lastTarget;
        private bool capturing, exiting, trayHintShown, editingHotkeys;
        private string lastPath;
        internal bool Ready { get; private set; }

        internal MainForm(Settings settings, string startupWarning)
        {
            this.settings = settings;
            Text = "GameShot · 游戏库与截图";
            Font = new Font("Microsoft YaHei UI", 10F);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1120, 800);
            MinimumSize = new Size(900, 640);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(20, 8, 20, 8) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 204));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            brand.Dock = DockStyle.Fill; header.Controls.Add(brand, 0, 0);
            var navigation = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(16, 12, 0, 0) };
            foreach (string name in new[] { "游戏库", "截图", "游戏分类", "设置" })
            {
                int index = sections.Count;
                var button = new ModernButton { Text = name, Size = new Size(index == 2 ? 92 : 78, 34), Margin = new Padding(0, 0, 6, 0), Quiet = true, Selected = index == 0, Font = new Font("Microsoft YaHei UI", 9) };
                button.Click += delegate { tabs.SelectedIndex = index; };
                sections.Add(button); navigation.Controls.Add(button);
            }
            header.Controls.Add(navigation, 1, 0);
            var openDirectory = (ModernButton)ButtonOf("截图目录", delegate { OpenFolder(this.settings.OutputFolder); });
            openDirectory.Glyph = "folder"; openDirectory.Quiet = true;
            openDirectory.Dock = DockStyle.Top; openDirectory.Margin = new Padding(0, 12, 0, 0);
            header.Controls.Add(openDirectory, 2, 0);
            root.Controls.Add(header, 0, 0);
            tabs.Dock = DockStyle.Fill;
            tabs.Margin = Padding.Empty;
            var home = new TabPage("截图") { Padding = new Padding(18) };
            var games = new TabPage("游戏分类") { Padding = new Padding(18) };
            var preferences = new TabPage("设置") { Padding = new Padding(18) };
            var libraryPage = new TabPage("游戏库") { Padding = new Padding(0) };
            gameStore = new LibraryStore(SettingsStore.Folder);
            gameStore.Synchronize(settings);
            library = new LibraryView(gameStore, () => this.settings);
            library.Status += SetStatus;
            library.GameConfigured += ConfigureLibraryGame;
            libraryPage.Controls.Add(library);
            tabs.TabPages.AddRange(new[] { libraryPage, home, games, preferences });
            root.Controls.Add(tabs, 0, 1);
            status.Dock = DockStyle.Fill;
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.AutoEllipsis = true;
            status.AccessibleName = "截图状态";
            status.Font = new Font("Microsoft YaHei UI", 8.5F);
            root.Controls.Add(status, 0, 2);
            Controls.Add(root);
            BuildHome(home);
            BuildGames(games);
            BuildPreferences(preferences);
            playtime = new PlaytimeTracker(gameStore);
            playtime.Changed += delegate { library.RefreshStats(); };
            playtime.Failed += SetStatus;

            var menu = new ContextMenuStrip();
            menu.Items.Add("打开 GameShot", null, delegate { ShowMain(); });
            menu.Items.Add(new ToolStripSeparator());
            for (int i = 0; i < 4; i++)
            {
                CaptureMode mode = (CaptureMode)i;
                menu.Items.Add(modeNames[i], null, async delegate { await TakeScreenshot(mode, false); });
            }
            menu.Items.Add("打开截图目录", null, delegate { OpenFolder(this.settings.OutputFolder); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { ExitApp(); });
            tray = new NotifyIcon { Icon = Icon, Text = "GameShot · 按游戏保存截图", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { ShowMain(); };
            tray.BalloonTipClicked += delegate { if (!string.IsNullOrEmpty(lastPath)) Reveal(lastPath); else ShowMain(); };
            tracking.Tick += delegate { TrackTarget(); };
            tracking.Start();
            ApplyTheme();
            Shown += delegate {
                Ready = true;
                string conflicts = Register(settings.Hotkeys);
                SetStatus(string.IsNullOrEmpty(conflicts) ? "已就绪 · " + settings.Hotkeys[0] + " 截屏，" + settings.Hotkeys[2] + " 框选，" + settings.Hotkeys[1] + " 截窗口" : conflicts);
                if (!string.IsNullOrEmpty(startupWarning)) SetStatus(startupWarning);
                if (!string.IsNullOrEmpty(gameStore.Warning)) SetStatus(gameStore.Warning);
                TrackTarget();
            };
            Resize += delegate { if (WindowState == FormWindowState.Minimized) Hide(); };
            tabs.SelectedIndexChanged += delegate { UpdateHotkeyEditing(); for (int i = 0; i < sections.Count; i++) { sections[i].Selected = tabs.SelectedIndex == i; sections[i].Invalidate(); } };
            FormClosing += OnClosing;
            // 控件尺寸按 96 DPI 设计，统一缩放布局，避免 150% 缩放时大字被固定行高裁掉。
            AutoScaleDimensions = new SizeF(96, 96);
            PerformAutoScale();
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            MinimumSize = new Size(Math.Min(MinimumSize.Width, work.Width - 32), Math.Min(MinimumSize.Height, work.Height - 32));
            Size = new Size(Math.Min(Width, work.Width - 32), Math.Min(Height, work.Height - 32));
        }

        private static Label LabelOf(string text, float size, FontStyle style = FontStyle.Regular)
        {
            return new Label { Text = text, AutoSize = true, Font = new Font("Microsoft YaHei UI", size, style), Margin = new Padding(0, 0, 0, 6) };
        }
        private static Button ButtonOf(string text, EventHandler handler)
        {
            var button = new ModernButton { Text = text, Width = 140, Height = 38, AutoSize = false, MinimumSize = new Size(0, 38), Dock = DockStyle.Fill, Margin = new Padding(4), Font = new Font("Microsoft YaHei UI", 9) };
            button.Click += handler;
            return button;
        }
        private void BuildHome(TabPage page)
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            var targetPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Fill, WrapContents = false };
            targetPanel.Controls.Add(LabelOf("当前分类", 10));
            category.AutoSize = false;
            category.Width = 660;
            category.Height = 36;
            category.Font = new Font(Font.FontFamily, 20, FontStyle.Bold);
            category.AutoEllipsis = true;
            category.AccessibleName = "当前游戏分类";
            targetPanel.Controls.Add(category);
            processLabel.AutoSize = false;
            processLabel.Width = 660;
            processLabel.Height = 25;
            processLabel.AutoEllipsis = true;
            targetPanel.Controls.Add(processLabel);
            targetPanel.Resize += delegate { category.Width = processLabel.Width = Math.Max(150, targetPanel.ClientSize.Width - 8); };
            layout.Controls.Add(targetPanel, 0, 0);
            var commands = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            commands.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            commands.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            commands.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            commands.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            for (int i = 0; i < 4; i++)
            {
                CaptureMode mode = (CaptureMode)i;
                var button = ButtonOf(modeNames[i] + "    " + settings.Hotkeys[i], async delegate { await TakeScreenshot(mode, true); });
                captureButtons.Add(button);
                commands.Controls.Add(button, i % 2, i / 2);
            }
            layout.Controls.Add(commands, 0, 1);
            var help = LabelOf("按快捷键即可截图。关闭窗口后仍在托盘运行，退出请右键托盘图标。", 9);
            help.Dock = DockStyle.Fill;
            help.AutoSize = false;
            layout.Controls.Add(help, 0, 2);
            layout.Controls.Add(LabelOf("本次运行的截图", 11, FontStyle.Bold), 0, 3);
            recent.Dock = DockStyle.Fill;
            recent.View = View.Details;
            recent.FullRowSelect = true;
            recent.MultiSelect = false;
            recent.HideSelection = false;
            recent.Columns.Add("时间", 90);
            recent.Columns.Add("游戏 / 应用", 150);
            recent.Columns.Add("方式", 92);
            recent.Columns.Add("文件", 300);
            recent.AccessibleName = "最近截图，双击打开图片";
            recent.DoubleClick += delegate { if (recent.SelectedItems.Count > 0) Launch((string)recent.SelectedItems[0].Tag); };
            recent.Resize += delegate { recent.Columns[3].Width = Math.Max(130, recent.ClientSize.Width - 344); };
            layout.Controls.Add(recent, 0, 4);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            actions.Controls.Add(ButtonOf("打开图片", delegate { if (recent.SelectedItems.Count > 0) Launch((string)recent.SelectedItems[0].Tag); else SetStatus("先选择一张截图。"); }));
            actions.Controls.Add(ButtonOf("在文件夹中查看", delegate { if (recent.SelectedItems.Count > 0) Reveal((string)recent.SelectedItems[0].Tag); else OpenFolder(settings.OutputFolder); }));
            layout.Controls.Add(actions, 0, 5);
            page.Controls.Add(layout);
        }

        private void BuildGames(TabPage page)
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            var intro = LabelOf("为游戏进程设置目录名\n未配置的游戏按进程名归档；窗口标题变化不会产生新目录。", 10);
            intro.Dock = DockStyle.Fill;
            layout.Controls.Add(intro, 0, 0);
            aliases.Dock = DockStyle.Fill;
            aliases.AllowUserToAddRows = true;
            aliases.AllowUserToDeleteRows = true;
            aliases.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            aliases.RowHeadersVisible = false;
            aliases.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            aliases.MultiSelect = false;
            aliases.Columns.Add("Process", "进程名（不含 .exe）");
            aliases.Columns.Add("Category", "游戏 / 分类名称");
            aliases.RowTemplate.Height = 32;
            foreach (var item in settings.Aliases.OrderBy(a => a.Value)) aliases.Rows.Add(item.Key, item.Value);
            aliases.AccessibleName = "游戏进程与截图目录对应表";
            layout.Controls.Add(aliases, 0, 1);
            var commands = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            commands.Controls.Add(ButtonOf("添加当前进程", delegate {
                if (lastTarget == null) { SetStatus("先切换到游戏，再回来添加当前进程。"); return; }
                foreach (DataGridViewRow row in aliases.Rows)
                    if (string.Equals(Convert.ToString(row.Cells[0].Value), lastTarget.ProcessName, StringComparison.OrdinalIgnoreCase)) {
                        aliases.CurrentCell = row.Cells[1]; aliases.BeginEdit(true); return;
                    }
                int index = aliases.Rows.Add(lastTarget.ProcessName, Archive.Category(lastTarget, settings));
                aliases.CurrentCell = aliases.Rows[index].Cells[1];
                aliases.BeginEdit(true);
            }));
            commands.Controls.Add(ButtonOf("删除选中项", delegate { if (aliases.CurrentRow != null && !aliases.CurrentRow.IsNewRow) aliases.Rows.Remove(aliases.CurrentRow); }));
            commands.Controls.Add(ButtonOf("保存分类", delegate { SaveSettings(); }));
            layout.Controls.Add(commands, 0, 2);
            layout.Controls.Add(LabelOf("可直接编辑表格末尾的空行。保存后，下次截图使用新的分类名称。", 9), 0, 3);
            page.Controls.Add(layout);
        }

        private void BuildPreferences(TabPage page)
        {
            page.AutoScroll = true;
            var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(850, 0), ColumnCount = 2, Padding = new Padding(0, 4, 0, 12) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            int row = 0;
            Action<string, Control, int> add = (label, control, height) => {
                layout.RowCount = row + 1;
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
                var caption = LabelOf(label, 10); caption.Padding = new Padding(0, 6, 0, 0);
                layout.Controls.Add(caption, 0, row);
                Control display = control is TextBox input ? new FieldFrame(input) : control;
                display.Dock = DockStyle.Fill;
                display.Margin = new Padding(0, 4, 0, 8);
                control.AccessibleName = label;
                layout.Controls.Add(display, 1, row++);
            };
            var folderRow = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill };
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            folderRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            folder.Text = settings.OutputFolder;
            folder.Dock = DockStyle.Fill;
            folder.AccessibleName = "截图保存目录";
            folderRow.Controls.Add(new FieldFrame(folder) { Dock = DockStyle.Fill }, 0, 0);
            folderRow.Controls.Add(ButtonOf("选择", delegate { using (var dialog = new FolderBrowserDialog { Description = "选择截图保存目录", SelectedPath = folder.Text }) if (dialog.ShowDialog(this) == DialogResult.OK) folder.Text = dialog.SelectedPath; }), 1, 0);
            add("保存目录", folderRow, 52);
            add("目录结构", LabelOf("游戏名 / 年-月-日 / 时间_截图方式.png", 9), 42);
            pin.Text = "固定归类到以下游戏（关闭后自动识别）";
            pin.Checked = !string.IsNullOrWhiteSpace(settings.FixedCategory);
            pin.CheckedChanged += delegate { fixedCategory.Enabled = pin.Checked; };
            add("分类方式", pin, 42);
            fixedCategory.Text = settings.FixedCategory;
            fixedCategory.Enabled = pin.Checked;
            add("固定游戏名称", fixedCategory, 46);
            for (int i = 0; i < 4; i++)
            {
                hotkeyBoxes[i] = new HotkeyBox(settings.Hotkeys[i]);
                hotkeyBoxes[i].Enter += delegate { UpdateHotkeyEditing(); };
                hotkeyBoxes[i].Leave += delegate { BeginInvoke((Action)UpdateHotkeyEditing); };
                add(modeNames[i] + "快捷键", hotkeyBoxes[i], 46);
            }
            add("更换快捷键", LabelOf("点击输入框后按新组合；Tab 切换输入框，Esc 取消输入。", 9), 40);
            clipboard.Text = "保存后同时复制图片到剪贴板";
            clipboard.Checked = settings.CopyClipboard;
            add("剪贴板", clipboard, 42);
            notifications.Text = "显示截图成功的托盘通知";
            notifications.Checked = settings.Notify;
            add("截图提醒", notifications, 42);
            selectedTheme = settings.Theme == "Light" ? 1 : settings.Theme == "Dark" ? 2 : 0;
            var themeChoices = new FlowLayoutPanel { WrapContents = false };
            var choices = new List<ModernButton>();
            foreach (string label in new[] { "跟随系统", "浅色", "深色" })
            {
                int index = choices.Count;
                var choice = new ModernButton { Text = label, Size = new Size(100, 34), Margin = new Padding(0, 0, 8, 0), Selected = index == selectedTheme, AccessibleName = "界面主题：" + label };
                choice.Click += delegate { selectedTheme = index; foreach (var item in choices) { item.Selected = choices.IndexOf(item) == index; item.Invalidate(); } };
                choices.Add(choice); themeChoices.Controls.Add(choice);
            }
            add("界面主题", themeChoices, 46);
            saveButton.Text = "保存设置";
            saveButton.Click += delegate { SaveSettings(); };
            var saveActions = new FlowLayoutPanel { WrapContents = false };
            saveButton.Size = new Size(130, 36); saveButton.Dock = DockStyle.None;
            saveActions.Controls.Add(saveButton);
            add("", saveActions, 48);
            page.Controls.Add(layout);
        }

        private void TrackTarget()
        {
            if (capturing) return;
            Target current = Target.Read(Native.GetForegroundWindow());
            if (current != null) lastTarget = current;
            category.Text = Archive.Category(lastTarget, settings);
            processLabel.Text = lastTarget == null ? "切换到游戏后自动识别；也可以在设置中固定游戏名称。" : lastTarget.ProcessName + ".exe  ·  " + lastTarget.Title;
        }

        private string Register(Hotkey[] keys)
        {
            var failed = new List<string>();
            for (int i = 0; i < 4; i++)
            {
                registered[i] = Native.RegisterHotKey(Handle, 100 + i, keys[i].Modifiers | Native.MOD_NOREPEAT, (uint)keys[i].Key);
                if (!registered[i]) failed.Add(modeNames[i] + " " + keys[i] + "（错误 " + Marshal.GetLastWin32Error() + "）");
            }
            return failed.Count == 0 ? "" : "快捷键不可用（占用或权限限制）：" + string.Join("、", failed);
        }
        private void Unregister()
        {
            for (int i = 0; i < 4; i++) { if (registered[i]) Native.UnregisterHotKey(Handle, 100 + i); registered[i] = false; }
        }
        private void UpdateHotkeyEditing()
        {
            // 编辑快捷键时暂时释放自身的注册，否则按旧快捷键会触发截图而无法录入。
            bool editing = tabs.SelectedIndex == 3 && hotkeyBoxes.Any(box => box != null && box.Focused);
            if (editing == editingHotkeys) return;
            editingHotkeys = editing;
            if (editing) { Unregister(); SetStatus("正在编辑快捷键，截图快捷键暂时停用。"); }
            else { string error = Register(settings.Hotkeys); SetStatus(string.IsNullOrEmpty(error) ? "截图快捷键已恢复。" : error); }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == Native.WM_HOTKEY)
            {
                int mode = message.WParam.ToInt32() - 100;
                if (mode >= 0 && mode < 4 && !capturing && !editingHotkeys) BeginInvoke((Action)(async () => await TakeScreenshot((CaptureMode)mode, false)));
            }
            base.WndProc(ref message);
        }

        private async Task TakeScreenshot(CaptureMode mode, bool fromButton)
        {
            if (capturing) return;
            Target target = Target.Read(Native.GetForegroundWindow()) ?? lastTarget;
            if (mode == CaptureMode.Window && (target == null || !Native.IsWindow(target.Window) || Native.IsIconic(target.Window)))
            { SetStatus("没有可截图的前台窗口。先切换到游戏，再按窗口快捷键。"); return; }
            string game = Archive.Category(target, settings);
            string output = settings.OutputFolder;
            bool restore = Visible && (fromButton || Target.Read(Native.GetForegroundWindow()) == null);
            capturing = true;
            foreach (var button in captureButtons) button.Enabled = false;
            saveButton.Enabled = false;
            SetStatus("正在截图…");
            try
            {
                if (restore)
                {
                    Hide();
                    if (target != null && Native.IsWindow(target.Window)) Native.SetForegroundWindow(target.Window);
                    await Task.Delay(180);
                }
                Rectangle bounds;
                if (mode == CaptureMode.AllMonitors || mode == CaptureMode.Region) bounds = SystemInformation.VirtualScreen;
                else if (mode == CaptureMode.Window) bounds = Rectangle.Intersect(Native.WindowBounds(target.Window), SystemInformation.VirtualScreen);
                else bounds = target != null && Native.IsWindow(target.Window) ? Screen.FromHandle(target.Window).Bounds : Screen.FromPoint(Cursor.Position).Bounds;

                using (CapturedImage image = await Task.Run(() => CaptureEngine.Read(bounds, true)))
                {
                    if (mode == CaptureMode.Region)
                    {
                        // 在显示框选遮罩之前冻结画面，保存的图不会包含选框或半透明遮罩。
                        using (var selector = new RegionSelector(image.Bitmap, bounds))
                        {
                            if (selector.ShowDialog() != DialogResult.OK) { SetStatus("已取消截图。"); return; }
                            Bitmap cropped = image.Bitmap.Clone(selector.Selection, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                            image.Bitmap.Dispose();
                            image.Bitmap = cropped;
                        }
                    }
                    bool black = CaptureEngine.MostlyBlack(image.Bitmap);
                    string path = await Task.Run(() => Archive.Save(image.Bitmap, output, game, mode));
                    lastPath = path;
                    library.ScreenshotSaved(game);
                    string extra = image.Warning;
                    if (black) extra += " 画面接近全黑；若与游戏画面不符，请尝试无边框窗口模式。";
                    if (settings.CopyClipboard)
                    {
                        try { Clipboard.SetImage(image.Bitmap); }
                        catch (Exception ex) { extra += " 图片已保存，剪贴板复制失败：" + ex.Message; }
                    }
                    var entry = new ListViewItem(new[] { DateTime.Now.ToString("HH:mm:ss"), game, modeNames[(int)mode], Path.GetFileName(path) });
                    entry.Tag = path;
                    entry.ToolTipText = path + "\n" + image.Backend;
                    recent.ShowItemToolTips = true;
                    recent.Items.Insert(0, entry);
                    if (recent.Items.Count > 200) recent.Items.RemoveAt(200);
                    SetStatus("已保存到 " + Archive.SafeName(game) + " · " + image.Bitmap.Width + " × " + image.Bitmap.Height + " · " + image.Backend + (string.IsNullOrWhiteSpace(extra) ? "" : " · " + extra));
                    if (settings.Notify || !string.IsNullOrWhiteSpace(extra))
                    {
                        tray.BalloonTipTitle = string.IsNullOrWhiteSpace(extra) ? "截图已保存 · " + game : "截图已保存，请检查画面";
                        tray.BalloonTipText = string.IsNullOrWhiteSpace(extra) ? Path.GetFileName(path) + "\n点击通知查看文件。" : extra;
                        tray.BalloonTipIcon = string.IsNullOrWhiteSpace(extra) ? ToolTipIcon.Info : ToolTipIcon.Warning;
                        tray.ShowBalloonTip(2200);
                    }
                }
            }
            catch (Exception ex)
            {
                SetStatus("截图失败：" + ex.Message);
                tray.BalloonTipTitle = "截图失败";
                tray.BalloonTipText = ex.Message;
                tray.BalloonTipIcon = ToolTipIcon.Error;
                tray.ShowBalloonTip(4000);
                Log(ex);
            }
            finally
            {
                capturing = false;
                foreach (var button in captureButtons) button.Enabled = true;
                saveButton.Enabled = true;
                if (restore && !exiting) ShowMain();
            }
        }

        private void SaveSettings()
        {
            if (capturing) { SetStatus("截图完成后再保存设置。"); return; }
            try
            {
                aliases.EndEdit();
                if (!Path.IsPathFullyQualified(folder.Text.Trim())) throw new InvalidOperationException("请填写完整的保存目录路径。");
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (DataGridViewRow row in aliases.Rows)
                {
                    if (row.IsNewRow) continue;
                    string process = Convert.ToString(row.Cells[0].Value).Trim();
                    string name = Convert.ToString(row.Cells[1].Value).Trim();
                    if (process == "" && name == "") continue;
                    if (process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) process = process.Substring(0, process.Length - 4);
                    if (process == "" || name == "") throw new InvalidOperationException("每条游戏分类都需要进程名和分类名称。");
                    if (map.ContainsKey(process)) throw new InvalidOperationException("进程重复：" + process);
                    map.Add(process, name);
                }
                if (pin.Checked && string.IsNullOrWhiteSpace(fixedCategory.Text)) throw new InvalidOperationException("请填写固定游戏名称，或关闭固定分类。");
                var next = new Settings {
                    OutputFolder = Path.GetFullPath(folder.Text.Trim()), Aliases = map,
                    FixedCategory = pin.Checked ? fixedCategory.Text.Trim() : "", Notify = notifications.Checked,
                    CopyClipboard = clipboard.Checked, Theme = new[] { "System", "Light", "Dark" }[selectedTheme],
                    Hotkeys = hotkeyBoxes.Select(box => new Hotkey((Keys)box.Value.Key, box.Value.Modifiers)).ToArray()
                };
                if (next.Hotkeys.Select(h => h.Modifiers + ":" + h.Key).Distinct().Count() != 4) throw new InvalidOperationException("四种截图方式不能使用相同快捷键。");
                // 设置保存是一个整体：快捷键冲突或写入失败时恢复原快捷键，不留下半更新状态。
                Unregister();
                string conflict = Register(next.Hotkeys);
                if (!string.IsNullOrEmpty(conflict)) { Unregister(); Register(settings.Hotkeys); throw new InvalidOperationException(conflict); }
                try { SettingsStore.Save(next); }
                catch { Unregister(); Register(settings.Hotkeys); throw; }
                settings = next;
                gameStore.Synchronize(settings);
                gameStore.Save();
                library.Rebuild();
                editingHotkeys = false;
                for (int i = 0; i < 4; i++) captureButtons[i].Text = modeNames[i] + "    " + settings.Hotkeys[i];
                ApplyTheme();
                TrackTarget();
                SetStatus("设置已保存。截图目录：" + settings.OutputFolder);
            }
            catch (Exception ex) { SetStatus("设置未保存：" + ex.Message); MessageBox.Show(this, ex.Message, "检查设置", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void ApplyTheme()
        {
            bool dark = settings.Theme == "Dark";
            if (settings.Theme == "System")
            {
                try { dark = Convert.ToInt32(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1)) == 0; }
                catch { dark = false; }
            }
            Color background = Appearance.Background(dark);
            Color surface = Appearance.Surface(dark);
            Color foreground = Appearance.Text(dark);
            Action<Control> paint = null;
            paint = control => {
                control.ForeColor = foreground;
                control.BackColor = control is TextBox || control is ListView || control is DataGridView || control is ComboBox ? surface : background;
                if (control is ModernButton button) { button.Dark = dark; button.Invalidate(); }
                if (control is FieldFrame field) { field.Dark = dark; field.Invalidate(); }
                foreach (Control child in control.Controls) paint(child);
            };
            paint(this);
            aliases.EnableHeadersVisualStyles = !dark;
            aliases.BackgroundColor = surface;
            aliases.DefaultCellStyle.BackColor = surface;
            aliases.DefaultCellStyle.ForeColor = foreground;
            aliases.BorderStyle = BorderStyle.None;
            aliases.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            aliases.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            aliases.DefaultCellStyle.SelectionBackColor = Appearance.Selection(dark);
            aliases.DefaultCellStyle.SelectionForeColor = foreground;
            aliases.DefaultCellStyle.Padding = new Padding(10, 3, 4, 3);
            aliases.ColumnHeadersDefaultCellStyle.BackColor = background;
            aliases.ColumnHeadersDefaultCellStyle.ForeColor = foreground;
            aliases.ColumnHeadersDefaultCellStyle.SelectionBackColor = background;
            aliases.ColumnHeadersDefaultCellStyle.SelectionForeColor = foreground;
            aliases.GridColor = Appearance.Border(dark);
            aliases.EnableHeadersVisualStyles = false;
            status.ForeColor = Appearance.Muted(dark);
            brand.Dark = dark; brand.Invalidate();
            library.SetTheme(dark);
        }

        private void SetStatus(string text) { status.Text = text; status.AccessibleDescription = text; }
        internal void ShowMain() { Show(); WindowState = FormWindowState.Normal; Activate(); }
        internal void ExitApp()
        {
            if (capturing) { SetStatus("截图处理中，请完成或按 Esc 取消框选后退出。"); return; }
            exiting = true;
            Close();
        }
        private void OpenFolder(string path)
        {
            try { Directory.CreateDirectory(path); Launch(path); }
            catch (Exception ex) { SetStatus("无法打开目录：" + ex.Message); }
        }
        private void Launch(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { SetStatus("无法打开文件：" + ex.Message); }
        }
        private void Reveal(string path)
        {
            try {
                var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
                start.ArgumentList.Add("/select,"); start.ArgumentList.Add(path); Process.Start(start);
            }
            catch (Exception ex) { SetStatus("无法定位文件：" + ex.Message); }
        }
        private static void Log(Exception ex)
        {
            try { Directory.CreateDirectory(SettingsStore.Folder); File.AppendAllText(Path.Combine(SettingsStore.Folder, "errors.log"), DateTime.Now.ToString("s") + " " + ex + Environment.NewLine); } catch { }
        }
        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                if (!trayHintShown) { trayHintShown = true; tray.ShowBalloonTip(2000, "GameShot 正在后台运行", "按截图快捷键继续使用；右键托盘图标可退出。", ToolTipIcon.Info); }
                return;
            }
            Unregister();
            playtime.Dispose();
            tracking.Stop();
            tracking.Dispose();
            tray.Visible = false;
            tray.Dispose();
        }

        // 仅用于验证界面布局，生成的是工具自身窗口，不会截取其他应用内容。
        internal void SavePreview(string path)
        {
            using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height)); bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png); }
        }
        internal void SelectTab(int index) { if (index >= 0 && index < tabs.TabCount) tabs.SelectedIndex = index; }
        internal void PreviewGame(string name) { tabs.SelectedIndex = 0; library.PreviewGame(name); }

        private void ConfigureLibraryGame(GameEntry game)
        {
            // 游戏库和截图分类共用进程映射；在库中改名会保留旧目录供图库读取。
            var next = new Dictionary<string, string>(settings.Aliases, StringComparer.OrdinalIgnoreCase);
            foreach (string key in next.Where(a => game.ScreenshotFolders.Contains(Archive.SafeName(a.Value), StringComparer.OrdinalIgnoreCase)).Select(a => a.Key).ToArray()) next.Remove(key);
            foreach (string process in game.Processes) next[process] = game.Name;
            var old = settings.Aliases;
            settings.Aliases = next;
            try { SettingsStore.Save(settings); }
            catch { settings.Aliases = old; throw; }
            aliases.Rows.Clear();
            foreach (var item in settings.Aliases.OrderBy(a => a.Value)) aliases.Rows.Add(item.Key, item.Value);
        }
    }
}
