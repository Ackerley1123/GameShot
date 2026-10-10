using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Microsoft.Win32;

namespace GameShot
{
    [DataContract] internal sealed class GameEntry
    {
        [DataMember] public string Id = Guid.NewGuid().ToString("N");
        [DataMember] public string Name = "";
        [DataMember] public List<string> Processes = new List<string>();
        [DataMember] public List<string> ScreenshotFolders = new List<string>();
        [DataMember] public string Executable = "";
        [DataMember] public string Cover = "";
        [DataMember] public double RunningSeconds;
        [DataMember] public double ForegroundSeconds;
        [DataMember] public DateTime? LastPlayedUtc;
        [DataMember] public bool TrackTime = true;
        // 本次会话是临时状态；重启软件后归零，累计时长仍保存在磁盘。
        internal double SessionSeconds;
        internal bool IsRunning;
        public override string ToString() { return Name; }
    }

    [DataContract] internal sealed class LibraryData
    {
        [DataMember] public List<GameEntry> Games = new List<GameEntry>();
    }

    internal sealed class LibraryStore
    {
        internal readonly string Folder;
        internal LibraryData Data;
        internal string Warning = "";
        private bool writable = true;
        internal LibraryStore(string folder)
        {
            Folder = folder;
            string file = Path.Combine(folder, "library.json");
            Data = new LibraryData();
            if (!File.Exists(file)) return;
            try
            {
                using (var stream = File.OpenRead(file)) Data = (LibraryData)new DataContractJsonSerializer(typeof(LibraryData)).ReadObject(stream);
                if (Data == null || Data.Games == null) throw new InvalidDataException("游戏库内容为空。");
                foreach (var game in Data.Games)
                {
                    if (game == null || string.IsNullOrWhiteSpace(game.Id) || game.Id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || string.IsNullOrWhiteSpace(game.Name))
                        throw new InvalidDataException("游戏库包含无效条目。");
                    game.Processes = game.Processes ?? new List<string>();
                    game.ScreenshotFolders = game.ScreenshotFolders ?? new List<string>();
                    game.RunningSeconds = double.IsFinite(game.RunningSeconds) ? Math.Max(0, game.RunningSeconds) : 0;
                    game.ForegroundSeconds = double.IsFinite(game.ForegroundSeconds) ? Math.Max(0, game.ForegroundSeconds) : 0;
                }
            }
            catch (Exception ex)
            {
                Data = new LibraryData();
                // 先备份损坏文件；备份失败时停止写入，绝不覆盖原来的游戏时长。
                try { File.Copy(file, file + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")); }
                catch { writable = false; }
                Warning = "游戏库读取失败，已保留原文件：" + ex.Message;
            }
        }

        internal void Save()
        {
            if (!writable) throw new IOException("游戏库原文件无法备份，已停止写入以保护历史数据。");
            Directory.CreateDirectory(Folder);
            string file = Path.Combine(Folder, "library.json"), temp = file + ".tmp";
            using (var stream = File.Create(temp)) new DataContractJsonSerializer(typeof(LibraryData)).WriteObject(stream, Data);
            if (File.Exists(file)) File.Replace(temp, file, file + ".bak");
            else File.Move(temp, file);
        }

        internal GameEntry Find(string name) { return Data.Games.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)); }
        internal GameEntry Ensure(string name, bool track)
        {
            var game = Find(name);
            if (game == null) { game = new GameEntry { Name = name, TrackTime = track }; Data.Games.Add(game); }
            string folder = Archive.SafeName(name);
            if (!game.ScreenshotFolders.Contains(folder, StringComparer.OrdinalIgnoreCase)) game.ScreenshotFolders.Add(folder);
            return game;
        }

        internal void Synchronize(Settings settings)
        {
            foreach (var group in settings.Aliases.GroupBy(a => a.Value, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(group.Key)) continue;
                GameEntry game = Find(group.Key);
                if (game == null) game = Data.Games.FirstOrDefault(g => g.Processes.Any(p => group.Any(a => string.Equals(a.Key, p, StringComparison.OrdinalIgnoreCase))));
                if (game == null) game = Ensure(group.Key, true);
                else { game.Name = group.Key; if (!game.ScreenshotFolders.Contains(Archive.SafeName(group.Key), StringComparer.OrdinalIgnoreCase)) game.ScreenshotFolders.Add(Archive.SafeName(group.Key)); }
                game.TrackTime = true;
                game.Processes = group.Select(a => a.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            // 删除分类映射后保留历史条目，但不再用旧进程重复计时。
            foreach (var game in Data.Games)
                game.Processes = game.Processes.Where(p => settings.Aliases.TryGetValue(p, out string name) && string.Equals(name, game.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (Directory.Exists(settings.OutputFolder))
            {
                foreach (var path in Directory.EnumerateDirectories(settings.OutputFolder))
                {
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                    string name = Path.GetFileName(path);
                    if (!Data.Games.Any(g => g.ScreenshotFolders.Contains(name, StringComparer.OrdinalIgnoreCase))) Ensure(name, false);
                }
            }
        }

        internal List<string> ScreenshotFiles(GameEntry game, string root)
        {
            string absolute = Path.GetFullPath(root);
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in game.ScreenshotFolders.ToArray())
            {
                // 存储的文件夹是单段名称；历史配置也不能使图库扫描到截图目录之外。
                if (name != Archive.SafeName(name)) continue;
                string path = Path.Combine(absolute, name);
                if (!Directory.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                foreach (string file in Directory.EnumerateFiles(path, "*.png", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })) files.Add(file);
            }
            return files.OrderByDescending(File.GetLastWriteTimeUtc).ToList();
        }

        internal static string Duration(double seconds)
        {
            if (seconds < 60) return seconds >= 1 ? ((int)seconds) + " 秒" : "尚未记录";
            if (seconds < 3600) return ((int)(seconds / 60)) + " 分钟";
            return (seconds / 3600).ToString("0.0") + " 小时";
        }
    }

    internal sealed class PlaytimeTracker : IDisposable
    {
        private readonly LibraryStore store;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 2000 };
        private double lastSample, sinceSave;
        private HashSet<string> previous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string previousForeground;
        private bool initialized;
        private volatile bool locked;
        private volatile bool resetClock;
        internal event Action Changed;
        internal event Action<string> Failed;
        internal PlaytimeTracker(LibraryStore store, bool start = true)
        {
            this.store = store;
            if (!start) return;
            timer.Tick += delegate { Sample(); };
            SystemEvents.SessionSwitch += SessionChanged;
            SystemEvents.PowerModeChanged += PowerChanged;
            Sample();
            timer.Start();
        }
        private void SessionChanged(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock) { locked = true; resetClock = true; }
            if (e.Reason == SessionSwitchReason.SessionUnlock) { locked = false; resetClock = true; }
        }
        private void PowerChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Suspend || e.Mode == PowerModes.Resume) resetClock = true; }

        private void Sample()
        {
            double now = clock.Elapsed.TotalSeconds;
            double elapsed = now - lastSample;
            lastSample = now;
            try
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int session = Process.GetCurrentProcess().SessionId;
                foreach (var process in Process.GetProcesses())
                    using (process) { try { if (process.SessionId == session) names.Add(process.ProcessName); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } }
                Target foreground = Target.Read(Native.GetForegroundWindow());
                bool active = !locked && !resetClock;
                resetClock = false;
                Account(elapsed, names, foreground == null ? null : foreground.ProcessName, DateTime.UtcNow, active);
                sinceSave += elapsed;
                if (sinceSave >= 15) { store.Save(); sinceSave = 0; }
                if (Changed != null) Changed();
            }
            catch (Exception ex) { if (Failed != null) Failed("时长记录错误：" + ex.Message); }
        }

        internal void Account(double elapsed, HashSet<string> running, string foreground, DateTime utc, bool active)
        {
            // 使用单调时钟而非电脑日期计算间隔；休眠、锁屏及 UI 长时间阻塞不累加。
            bool valid = initialized && active && elapsed > 0 && elapsed <= 5;
            foreach (var game in store.Data.Games)
            {
                bool current = game.TrackTime && game.Processes.Any(running.Contains);
                game.IsRunning = current;
                bool wasRunning = game.Processes.Any(previous.Contains);
                if (valid && current && wasRunning)
                {
                    // 同一游戏的多个进程只累计一次；后台运行也算累计游戏时长，与 Steam 的习惯一致。
                    game.RunningSeconds += elapsed;
                    game.SessionSeconds += elapsed;
                    game.LastPlayedUtc = utc;
                    if (game.Processes.Contains(foreground, StringComparer.OrdinalIgnoreCase) && game.Processes.Contains(previousForeground, StringComparer.OrdinalIgnoreCase)) game.ForegroundSeconds += elapsed;
                }
            }
            previous = new HashSet<string>(running, StringComparer.OrdinalIgnoreCase);
            previousForeground = foreground;
            initialized = active;
        }

        public void Dispose()
        {
            timer.Stop(); timer.Dispose();
            SystemEvents.SessionSwitch -= SessionChanged;
            SystemEvents.PowerModeChanged -= PowerChanged;
            try { store.Save(); } catch (Exception ex) { if (Failed != null) Failed("游戏时长保存失败：" + ex.Message); }
        }
    }
}
