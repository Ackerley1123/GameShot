using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GameShot
{
    internal static class LibraryTests
    {
        private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        internal static void Run(Action<string, Action> check, string output)
        {
            string root = Path.Combine(output, "library-tests", Guid.NewGuid().ToString("N"));
            check("游戏时长：多进程去重、后台区分、锁屏休眠不计时", delegate {
                var store = new LibraryStore(root);
                var game = store.Ensure("测试游戏", true);
                game.Processes.AddRange(new[] { "GameClient", "GameRender" });
                using (var tracker = new PlaytimeTracker(store, false))
                {
                    var running = new HashSet<string>(new[] { "gameclient", "GameRender" }, StringComparer.OrdinalIgnoreCase);
                    DateTime utc = DateTime.UtcNow;
                    tracker.Account(0, running, "GameClient", utc, true);
                    tracker.Account(2, running, "GameClient", utc, true);
                    Assert(game.RunningSeconds == 2 && game.ForegroundSeconds == 2, "游戏的多个进程被重复计时。");
                    tracker.Account(2, running, "browser", utc, true);
                    tracker.Account(2, running, "browser", utc, true);
                    Assert(game.RunningSeconds == 6 && game.ForegroundSeconds == 2, "后台与前台时长混淆。");
                    tracker.Account(2, running, "GameClient", utc, false);
                    tracker.Account(2, running, "GameClient", utc, true);
                    tracker.Account(3600, running, "GameClient", utc, true);
                    Assert(game.RunningSeconds == 6, "锁屏、恢复或休眠间隔被记入游戏时长。");
                    tracker.Account(1.5, running, "GameClient", utc, true);
                    Assert(game.RunningSeconds == 7.5 && game.ForegroundSeconds == 3.5, "恢复后的计时不正确。");
                    tracker.Account(2, new HashSet<string>(), null, utc, true);
                    Assert(!game.IsRunning && game.RunningSeconds == 7.5, "游戏退出后仍在计时。");
                }
                var restored = new LibraryStore(root).Find("测试游戏");
                Assert(restored.RunningSeconds == 7.5 && restored.ForegroundSeconds == 3.5 && restored.SessionSeconds == 0, "重启后累计时间丢失或本次会话未归零。");
            });
            check("真实进程扫描自动记录运行时长", delegate {
                var store = new LibraryStore(Path.Combine(root, "live"));
                var game = store.Ensure("运行检测测试", true);
                game.Processes.Add(System.Diagnostics.Process.GetCurrentProcess().ProcessName);
                using (var tracker = new PlaytimeTracker(store))
                {
                    var limit = System.Diagnostics.Stopwatch.StartNew();
                    while (limit.Elapsed.TotalSeconds < 2.4) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(20); }
                    Assert(game.IsRunning && game.RunningSeconds >= 1.5 && game.RunningSeconds < 4, "真实进程扫描未按一个游戏累计时间。");
                }
            });
            check("游戏改名保留时长与旧目录截图，图库与缩略图可读取", delegate {
                var store = new LibraryStore(Path.Combine(root, "rename"));
                var settings = Settings.Defaults(root);
                settings.Aliases = new Dictionary<string, string> { { "client", "原名称" } };
                store.Synchronize(settings);
                var game = store.Find("原名称");
                game.RunningSeconds = 3600;
                string oldId = game.Id;
                using (var image = new Bitmap(80, 60))
                {
                    using (var canvas = Graphics.FromImage(image)) canvas.Clear(Color.Red);
                    Archive.Save(image, root, "原名称", CaptureMode.Monitor);
                    settings.Aliases["client"] = "新名称";
                    store.Synchronize(settings);
                    Archive.Save(image, root, "新名称", CaptureMode.Monitor);
                }
                Assert(game.Id == oldId && game.Name == "新名称" && game.RunningSeconds == 3600, "改名生成了新的游戏或丢失时长。");
                List<string> files = store.ScreenshotFiles(game, root);
                Assert(files.Count == 2, "改名后丢失旧目录的图片。");
                using (var thumb = Artwork.Thumbnail(files[0], 40, 40)) Assert(thumb != null && thumb.Width == 40 && thumb.Height == 30 && thumb.GetPixel(10, 10).R == 255, "缩略图无法读取或像素错误。");
                game.ScreenshotFolders.Add("..\\outside");
                Assert(store.ScreenshotFiles(game, root).Count == 2, "图库没有拒绝越界目录。");
                store.Save();
                Assert(new LibraryStore(store.Folder).Find("新名称").RunningSeconds == 3600, "游戏库保存后时长丢失。");
            });
            check("拆分游戏进程分类后不重复计时", delegate {
                var store = new LibraryStore(Path.Combine(root, "split"));
                var settings = Settings.Defaults(root);
                settings.Aliases = new Dictionary<string, string> { { "clientA", "原游戏" }, { "clientB", "原游戏" } };
                store.Synchronize(settings);
                store.Find("原游戏").RunningSeconds = 100;
                settings.Aliases["clientA"] = "游戏A";
                settings.Aliases["clientB"] = "游戏B";
                store.Synchronize(settings);
                Assert(store.Find("游戏A").Processes.SequenceEqual(new[] { "clientA" }) && store.Find("游戏B").Processes.SequenceEqual(new[] { "clientB" }), "拆分后进程分类重叠。");
                Assert(store.Find("游戏A").RunningSeconds == 100 && store.Find("游戏B").RunningSeconds == 0, "历史时间被丢失或重复复制。");
                settings.Aliases.Remove("clientA");
                store.Synchronize(settings);
                Assert(store.Find("游戏A").Processes.Count == 0 && store.Find("游戏A").RunningSeconds == 100, "移除进程映射后仍用旧进程计时或丢失历史。");
            });
            check("损坏的时长文件先备份再恢复", delegate {
                string folder = Path.Combine(root, "corrupt"); Directory.CreateDirectory(folder);
                string file = Path.Combine(folder, "library.json"); File.WriteAllText(file, "{broken}");
                var store = new LibraryStore(folder);
                Assert(store.Warning != "" && Directory.GetFiles(folder, "library.json.corrupt-*").Length == 1, "损坏的原文件没有备份。");
                store.Ensure("恢复测试", true); store.Save();
                Assert(new LibraryStore(folder).Find("恢复测试") != null, "备份后恢复失败。");
            });
        }
    }
}
