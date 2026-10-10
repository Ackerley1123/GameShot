using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace GameShot
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Native.EnableDpi();
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0] == "--self-test") return SelfTests.Run(args.Length > 1 ? args[1] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-results"));
            if (args.Length > 0 && args[0] == "--library-test") return SelfTests.RunLibrary(args.Length > 1 ? args[1] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-results"));
            try
            {
                bool first;
                // 防止重复启动占用同一组快捷键；再次启动会唤回托盘里的窗口。
                using (var mutex = new Mutex(true, @"Local\GameShot.Desktop.1", out first))
                using (var wake = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\GameShot.Show.1"))
                {
                    if (!first) { wake.Set(); return 0; }
                    string warning;
                    Settings settings = SettingsStore.Load(out warning);
                    if (Array.IndexOf(args, "--dark") >= 0) settings.Theme = "Dark";
                    if (Array.IndexOf(args, "--light") >= 0) settings.Theme = "Light";
                    using (var form = new MainForm(settings, warning))
                    using (var timer = new System.Windows.Forms.Timer { Interval = 500 })
                    {
                        bool previewWritten = false;
                        bool previewPrepared = false;
                        int previewIndex = Array.IndexOf(args, "--preview");
                        timer.Tick += delegate {
                            if (wake.WaitOne(0)) form.ShowMain();
                            if (previewIndex >= 0 && previewIndex + 1 < args.Length && !previewWritten && form.Ready)
                            {
                                if (!previewPrepared)
                                {
                                    previewPrepared = true;
                                    int tabIndex = Array.IndexOf(args, "--tab");
                                    int selectedTab;
                                    if (tabIndex >= 0 && tabIndex + 1 < args.Length && int.TryParse(args[tabIndex + 1], out selectedTab)) form.SelectTab(selectedTab);
                                    int gameIndex = Array.IndexOf(args, "--game");
                                    if (gameIndex >= 0 && gameIndex + 1 < args.Length) form.PreviewGame(args[gameIndex + 1]);
                                    return;
                                }
                                previewWritten = true;
                                string path = Path.GetFullPath(args[previewIndex + 1]);
                                Directory.CreateDirectory(Path.GetDirectoryName(path));
                                form.SavePreview(path);
                                if (Array.IndexOf(args, "--exit-after-preview") >= 0) form.ExitApp();
                            }
                        };
                        timer.Start();
                        Application.Run(form);
                    }
                    mutex.ReleaseMutex();
                }
                return 0;
            }
            catch (Exception ex)
            {
                try { Directory.CreateDirectory(SettingsStore.Folder); File.AppendAllText(Path.Combine(SettingsStore.Folder, "startup-errors.log"), DateTime.Now.ToString("s") + " " + ex + Environment.NewLine); } catch { }
                MessageBox.Show("GameShot 无法启动：\n" + ex.Message, "GameShot", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
