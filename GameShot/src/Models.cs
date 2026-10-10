using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows.Forms;

namespace GameShot
{
    internal enum CaptureMode { Monitor, Window, Region, AllMonitors }

    [DataContract] internal sealed class Hotkey
    {
        [DataMember] public int Key = (int)Keys.F8;
        [DataMember] public uint Modifiers = 0;
        public Hotkey() { }
        public Hotkey(Keys key, uint modifiers) { Key = (int)key; Modifiers = modifiers; }
        public override string ToString()
        {
            return ((Modifiers & 2) != 0 ? "Ctrl + " : "") + ((Modifiers & 1) != 0 ? "Alt + " : "") +
                   ((Modifiers & 4) != 0 ? "Shift + " : "") + ((Keys)Key).ToString();
        }
    }

    [DataContract] internal sealed class Settings
    {
        [DataMember] public string OutputFolder;
        [DataMember] public string FixedCategory = "";
        [DataMember] public bool Notify = true;
        [DataMember] public bool CopyClipboard = false;
        [DataMember] public string Theme = "System";
        [DataMember] public Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        [DataMember] public Hotkey[] Hotkeys = DefaultHotkeys();

        internal static Hotkey[] DefaultHotkeys()
        {
            return new[] { new Hotkey(Keys.F8, 0), new Hotkey(Keys.F10, 0), new Hotkey(Keys.F9, 0), new Hotkey(Keys.F8, 2 | 4) };
        }
        internal static Settings Defaults(string folder)
        {
            return new Settings {
                OutputFolder = Path.Combine(folder, "Screenshots"), Theme = "Light",
                Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                    { "GenshinImpact", "原神" }, { "YuanShen", "原神" }, { "StarRail", "崩坏：星穹铁道" },
                    { "ZenlessZoneZero", "绝区零" }, { "League of Legends", "英雄联盟" }, { "cs2", "Counter-Strike 2" },
                    { "VALORANT-Win64-Shipping", "无畏契约" }, { "Overwatch", "守望先锋" }, { "Minecraft.Windows", "Minecraft" }
                }
            };
        }
    }

    internal static class SettingsStore
    {
        internal static readonly string Folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
        internal static readonly string FilePath = Path.Combine(Folder, "settings.json");
        internal static Settings Load(out string warning)
        {
            warning = "";
            if (!File.Exists(FilePath)) return Settings.Defaults(AppDomain.CurrentDomain.BaseDirectory);
            try
            {
                using (var stream = File.OpenRead(FilePath))
                {
                    var settings = (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(stream);
                    if (settings == null || string.IsNullOrWhiteSpace(settings.OutputFolder)) throw new InvalidDataException("保存目录为空。");
                    settings.OutputFolder = Path.GetFullPath(settings.OutputFolder);
                    settings.Aliases = new Dictionary<string, string>(settings.Aliases ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                    if (settings.Hotkeys == null || settings.Hotkeys.Length != 4 || settings.Hotkeys.Any(h => h == null || h.Key < 1 || h.Key > 254 || h.Modifiers > 7))
                        throw new InvalidDataException("快捷键配置无效。");
                    settings.FixedCategory = settings.FixedCategory ?? "";
                    return settings;
                }
            }
            catch (Exception ex)
            {
                // 保留损坏的配置，用户还可以手动恢复自定义的游戏名称。
                warning = "配置读取失败，已使用默认设置。原文件仍保留。" + ex.Message;
                return Settings.Defaults(AppDomain.CurrentDomain.BaseDirectory);
            }
        }

        internal static void Save(Settings settings)
        {
            Directory.CreateDirectory(Folder);
            string temporary = FilePath + ".tmp";
            using (var stream = File.Create(temporary)) new DataContractJsonSerializer(typeof(Settings)).WriteObject(stream, settings);
            // 先完整写入临时文件，再替换旧文件，避免程序意外退出时写出半份配置。
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
            else File.Move(temporary, FilePath);
        }
    }

    internal sealed class Target
    {
        internal IntPtr Window;
        internal string ProcessName;
        internal string Title;
        internal static Target Read(IntPtr window)
        {
            if (window == IntPtr.Zero || !Native.IsWindow(window)) return null;
            uint id;
            Native.GetWindowThreadProcessId(window, out id);
            if (id == (uint)Process.GetCurrentProcess().Id) return null;
            string name;
            try { using (var process = Process.GetProcessById((int)id)) name = process.ProcessName; }
            catch (ArgumentException) { return null; }
            catch (System.ComponentModel.Win32Exception) { name = "未知应用-" + id; }
            var text = new StringBuilder(512);
            Native.GetWindowText(window, text, text.Capacity);
            return new Target { Window = window, ProcessName = name, Title = text.ToString() };
        }
    }

    internal static class Archive
    {
        internal static string Category(Target target, Settings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.FixedCategory)) return settings.FixedCategory.Trim();
            if (target == null) return "桌面";
            string alias;
            if (settings.Aliases.TryGetValue(target.ProcessName, out alias) && !string.IsNullOrWhiteSpace(alias)) return alias.Trim();
            if (target.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return "桌面与文件管理器";
            // 进程名比窗口标题稳定：标题里的关卡、帧率或文件名不会导致重复分类。
            return target.ProcessName;
        }

        internal static string SafeName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            string safe = new string((name ?? "").Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim().TrimEnd('.', ' ');
            if (safe.Length > 64) safe = safe.Substring(0, 64).TrimEnd('.', ' ');
            if (string.IsNullOrWhiteSpace(safe) || safe == "." || safe == "..") safe = "未分类";
            string stem = safe.Split('.')[0].ToUpperInvariant();
            if (new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(stem) ||
                (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && "123456789¹²³".Contains(stem[3]))) safe = "_" + safe;
            return safe;
        }

        internal static string Save(Bitmap bitmap, string root, string category, CaptureMode mode)
        {
            DateTime now = DateTime.Now;
            string folder = Path.Combine(Path.GetFullPath(root), SafeName(category), now.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(folder);
            string suffix = new[] { "屏幕", "窗口", "区域", "全部屏幕" }[(int)mode];
            string path = Path.Combine(folder, now.ToString("HH-mm-ss-fff") + "_" + suffix + "_" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".png");
            // CreateNew 保证任何已有截图都不会被覆盖；PNG 不会损失文字和游戏画面细节。
            bool created = false;
            try
            {
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                {
                    created = true;
                    bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            catch { if (created && File.Exists(path)) File.Delete(path); throw; }
            return path;
        }
    }
}
