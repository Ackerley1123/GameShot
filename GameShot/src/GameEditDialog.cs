using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace GameShot
{
    internal sealed class GameEditDialog : Form
    {
        private readonly TextBox name = new TextBox(), executable = new TextBox(), processes = new TextBox(), cover = new TextBox();
        internal string GameName { get { return name.Text.Trim(); } }
        internal string Executable { get { return executable.Text.Trim(); } }
        internal string CoverFile { get { return cover.Text.Trim(); } }
        internal List<string> ProcessNames { get { return processes.Text.Split(new[] { ',', ';', '，', '；' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Select(p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p.Substring(0, p.Length - 4) : p).Where(p => p != "").Distinct(StringComparer.OrdinalIgnoreCase).ToList(); } }
        internal GameEditDialog(GameEntry game, bool dark)
        {
            Text = game == null ? "添加游戏" : "编辑游戏 · " + game.Name;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 10);
            Size = new Size(640, 430);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            name.Text = game == null ? "" : game.Name;
            executable.Text = game == null ? "" : game.Executable;
            processes.Text = game == null ? "" : string.Join(", ", game.Processes);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 2, RowCount = 6 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var controls = new Control[] { new FieldFrame(name), BrowseRow(executable, false), new FieldFrame(processes), BrowseRow(cover, true) };
            string[] captions = { "游戏名称", "启动文件", "游戏进程名", "封面图片" };
            for (int i = 0; i < 4; i++)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
                layout.Controls.Add(new Label { Text = captions[i], Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, i);
                controls[i].Dock = DockStyle.Fill; controls[i].Margin = new Padding(0, 8, 0, 10); controls[i].AccessibleName = captions[i]; layout.Controls.Add(controls[i], 1, i);
            }
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var note = new Label { Text = "进程名不含 .exe，多个进程用逗号分隔。\n未关联启动文件也能记录时长；旧截图和历史时长会保留。", Dock = DockStyle.Fill, AutoEllipsis = true, Font = new Font(Font.FontFamily, 9) };
            layout.Controls.Add(note, 0, 4); layout.SetColumnSpan(note, 2);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var save = new ModernButton { Text = "保存游戏", Size = new Size(112, 36), Primary = true, Dark = dark };
            var cancel = new ModernButton { Text = "取消", Size = new Size(100, 36), DialogResult = DialogResult.Cancel, Dark = dark };
            save.Click += delegate {
                if (GameName == "" || ProcessNames.Count == 0) { MessageBox.Show(this, "请填写游戏名称和至少一个游戏进程名。", "检查游戏信息"); return; }
                if (Executable != "" && !File.Exists(Executable)) { MessageBox.Show(this, "启动文件不存在，请重新选择。", "检查启动文件"); return; }
                if (CoverFile != "" && !File.Exists(CoverFile)) { MessageBox.Show(this, "封面图片不存在。", "检查封面"); return; }
                DialogResult = DialogResult.OK; Close();
            };
            buttons.Controls.Add(save); buttons.Controls.Add(cancel);
            layout.Controls.Add(buttons, 0, 5); layout.SetColumnSpan(buttons, 2);
            Controls.Add(layout); AcceptButton = save; CancelButton = cancel;
            Action<Control> paint = null;
            paint = c => { c.BackColor = c is TextBox ? Appearance.Surface(dark) : Appearance.Background(dark); c.ForeColor = Appearance.Text(dark); if (c is ModernButton b) b.Dark = dark; if (c is FieldFrame f) f.Dark = dark; foreach (Control child in c.Controls) paint(child); };
            paint(this);
            AutoScaleDimensions = new SizeF(96, 96); PerformAutoScale();
        }
        private Control BrowseRow(TextBox field, bool image)
        {
            var row = new TableLayoutPanel { ColumnCount = 2, RowCount = 1 };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74)); row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            field.Dock = DockStyle.Fill;
            var browse = new ModernButton { Text = "选择", Dock = DockStyle.Fill, Margin = new Padding(8, 0, 0, 0) };
            browse.Click += delegate {
                using (var dialog = new OpenFileDialog { Filter = image ? "封面图片|*.png;*.jpg;*.jpeg;*.bmp" : "游戏启动文件|*.exe;*.lnk;*.url", Title = image ? "选择游戏封面" : "选择游戏程序或快捷方式" })
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        field.Text = dialog.FileName;
                        if (!image && Path.GetExtension(dialog.FileName).Equals(".exe", StringComparison.OrdinalIgnoreCase)) { if (processes.Text.Trim() == "") processes.Text = Path.GetFileNameWithoutExtension(dialog.FileName); if (name.Text.Trim() == "") name.Text = Path.GetFileNameWithoutExtension(dialog.FileName); }
                    }
            };
            row.Controls.Add(new FieldFrame(field) { Dock = DockStyle.Fill }, 0, 0); row.Controls.Add(browse, 1, 0);
            return row;
        }
    }
}
