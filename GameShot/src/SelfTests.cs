using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GameShot
{
    // 集成测试使用自己绘制的色块窗口；测试图只保存测试窗口区域。
    internal sealed class SelfTests : Form
    {
        private readonly List<string> report = new List<string>();
        private readonly string output;
        private bool receivedHotkey;
        private int failures;
        private IntPtr originalForeground;
        private SelfTests(string output)
        {
            this.output = Path.GetFullPath(output);
            Text = "GameShot — 截图集成验证";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(480, 300);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            TopMost = true;
            Shown += async delegate { await Verify(); };
        }
        internal static int Run(string output)
        {
            using (var tests = new SelfTests(output)) { Application.Run(tests); return tests.failures == 0 ? 0 : 1; }
        }
        internal static int RunLibrary(string output)
        {
            // 游戏库测试不抢占前台；运行游戏时也能检查计时与数据保存。
            output = Path.GetFullPath(output);
            Directory.CreateDirectory(output);
            var lines = new List<string>();
            int failed = 0;
            LibraryTests.Run((name, action) => {
                try { action(); lines.Add("PASS " + name); }
                catch (Exception ex) { failed++; lines.Add("FAIL " + name + " — " + ex.Message); }
            }, output);
            lines.Add("RESULT " + (failed == 0 ? "PASS" : "FAIL") + "；失败数：" + failed);
            File.WriteAllLines(Path.Combine(output, "library-test-report.txt"), lines);
            return failed == 0 ? 0 : 1;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.FromArgb(40, 44, 54));
            e.Graphics.FillRectangle(Brushes.Red, 24, 24, 110, 110);
            e.Graphics.FillRectangle(Brushes.Lime, 174, 24, 110, 110);
            e.Graphics.FillRectangle(Brushes.Blue, 324, 24, 110, 110);
            TextRenderer.DrawText(e.Graphics, "GameShot capture verification", Font, new Point(24, 170), Color.White);
        }
        private void Check(string name, Action action)
        {
            try { action(); report.Add("PASS " + name); }
            catch (Exception ex) { failures++; report.Add("FAIL " + name + " — " + ex.Message); }
        }
        private static void Assert(bool success, string message) { if (!success) throw new InvalidOperationException(message); }
        private static void AssertColor(Color actual, Color expected)
        {
            Assert(Math.Abs(actual.R - expected.R) <= 12 && Math.Abs(actual.G - expected.G) <= 12 && Math.Abs(actual.B - expected.B) <= 12,
                "预期 " + expected + "，实际 " + actual);
        }
        private async Task Verify()
        {
            Directory.CreateDirectory(output);
            originalForeground = Native.GetForegroundWindow();
            // 命令行以隐藏启动方式运行时，显式显示测试窗口，才能验证真实桌面像素。
            Native.ShowWindow(Handle, 5);
            Native.SetWindowPos(Handle, new IntPtr(-1), Left, Top, Width, Height, 0x40);
            Native.keybd_event(0x12, 0, 0, UIntPtr.Zero);
            Native.keybd_event(0x12, 0, 2, UIntPtr.Zero);
            Native.SetForegroundWindow(Handle);
            Refresh();
            await Task.Delay(500);
            Native.ShowWindow(Handle, 5);
            Native.SetWindowPos(Handle, new IntPtr(-1), Left, Top, Width, Height, 0x40);
            Refresh();
            await Task.Delay(500);
            int cloaked;
            int cloakResult = Native.DwmGetWindowAttribute(Handle, 14, out cloaked, 4);
            report.Add("INFO 窗口 " + Bounds + " 客户区 " + RectangleToScreen(ClientRectangle) + " 桌面 " + SystemInformation.VirtualScreen + " DPI " + DeviceDpi + " Cloaked " + cloaked);
            report.Add("INFO 前台窗口 " + Native.GetForegroundWindow() + " 测试窗口 " + Handle + " Session " + System.Diagnostics.Process.GetCurrentProcess().SessionId);
            report.Add("INFO desktop " + Native.DesktopName() + " screen " + Screen.FromHandle(Handle).Bounds);
            report.Add("INFO SetWindowPos " + Native.SetWindowPos(Handle, new IntPtr(-1), Left, Top, Width, Height, 0x40));
            uint affinity;
            report.Add("INFO Affinity " + Native.GetWindowDisplayAffinity(Handle, out affinity) + " " + affinity + " cloak HRESULT " + cloakResult);
            var foreground = Target.Read(Native.GetForegroundWindow());
            if (foreground != null) report.Add("INFO 前台 " + foreground.ProcessName + " " + foreground.Title);
            report.Add("INFO DWM bounds " + Native.WindowBounds(Handle) + " minimized " + Native.IsIconic(Handle));
            using (var fixture = new Bitmap(ClientSize.Width, ClientSize.Height)) { DrawToBitmap(fixture, ClientRectangle); fixture.Save(Path.Combine(output, "render-fixture.png")); }
            Check("测试窗口已显示", delegate { Assert(Native.IsWindowVisible(Handle), "测试窗口不可见。"); });
            Check("按进程自动归档、中文别名、固定分类", delegate {
                Settings settings = Settings.Defaults(output);
                var game = new Target { ProcessName = "CS2", Title = "不同的关卡标题" };
                Assert(Archive.Category(game, settings) == "Counter-Strike 2", "中文别名或大小写匹配错误。");
                Assert(Archive.Category(new Target { ProcessName = "NewGame" }, settings) == "NewGame", "未知游戏分类错误。");
                settings.FixedCategory = "指定游戏";
                Assert(Archive.Category(game, settings) == "指定游戏", "固定分类未生效。");
            });
            Check("目录名称不能越出根目录或使用 Windows 保留设备名", delegate {
                foreach (string name in new[] { "../逃逸", @"..\逃逸", "CON", "NUL.png", "COM1", "LPT9", "COM¹", "<>:|*?", ".", "..", " ", new string('长', 120) })
                {
                    string safe = Archive.SafeName(name);
                    Assert(safe.Length > 0 && safe.Length <= 64 && safe.IndexOfAny(Path.GetInvalidFileNameChars()) < 0, "名称清洗错误：" + name);
                    string full = Path.GetFullPath(Path.Combine(output, safe));
                    Assert(full.StartsWith(output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "目录发生越界。");
                    Assert(safe != "CON" && safe != "NUL.png" && safe != "COM1" && safe != "LPT9" && safe != "COM¹", "保留名未处理。");
                }
            });
            Check("配置序列化保留中文、快捷键和分类", delegate {
                var original = Settings.Defaults(output);
                original.FixedCategory = "测试游戏";
                using (var memory = new MemoryStream())
                {
                    var serializer = new DataContractJsonSerializer(typeof(Settings));
                    serializer.WriteObject(memory, original);
                    memory.Position = 0;
                    var loaded = (Settings)serializer.ReadObject(memory);
                    Assert(loaded.FixedCategory == original.FixedCategory && loaded.Hotkeys.Length == 4 && loaded.Aliases["cs2"] == original.Aliases["cs2"], "配置恢复错误。");
                }
            });
            Rectangle bounds = RectangleToScreen(ClientRectangle);
            // 游戏可能在测试准备期间抢回前台；在读像素前再次激活色块窗口。
            // 若前台仍被占用，明确报告测试条件失败，避免保存其他应用画面。
            Native.keybd_event(0x12, 0, 0, UIntPtr.Zero);
            Native.keybd_event(0x12, 0, 2, UIntPtr.Zero);
            Native.SetForegroundWindow(Handle);
            Refresh();
            await Task.Delay(250);
            Check("DirectX 实际抓屏及 RGB 像素", delegate {
                Assert(Native.GetForegroundWindow() == Handle, "测试窗口未处于前台，请在桌面空闲时重试。");
                bool protectedContent;
                using (var display = DesktopDuplication.Capture(Screen.FromHandle(Handle).DeviceName, out protectedContent))
                {
                    report.Add("INFO DirectX " + DesktopDuplication.LastFrameInfo);
                    for (int y = 0; y < display.Height; y += 10)
                        for (int x = 0; x < display.Width - 310; x += 10)
                            if (display.GetPixel(x, y).ToArgb() == Color.Red.ToArgb() && display.GetPixel(x + 150, y).ToArgb() == Color.Lime.ToArgb() && display.GetPixel(x + 300, y).ToArgb() == Color.Blue.ToArgb())
                            { report.Add("INFO 找到测试色块 " + x + "," + y); y = display.Height; break; }
                    Rectangle monitor = Screen.FromHandle(Handle).Bounds;
                    using (var image = display.Clone(new Rectangle(bounds.X - monitor.X, bounds.Y - monitor.Y, bounds.Width, bounds.Height), System.Drawing.Imaging.PixelFormat.Format32bppRgb))
                    {
                        image.Save(Path.Combine(output, "directx-fixture.png"));
                        AssertColor(image.GetPixel(70, 70), Color.Red);
                        AssertColor(image.GetPixel(220, 70), Color.Lime);
                        AssertColor(image.GetPixel(370, 70), Color.Blue);
                    }
                    Assert(!protectedContent, "测试窗口不应被标记为受保护画面。");
                }
            });
            Check("普通捕获回退及物理像素裁切", delegate {
                Assert(Native.GetForegroundWindow() == Handle, "测试窗口未处于前台，请在桌面空闲时重试。");
                using (var image = CaptureEngine.Read(bounds, false))
                {
                    image.Bitmap.Save(Path.Combine(output, "gdi-fixture.png"));
                    AssertColor(image.Bitmap.GetPixel(70, 70), Color.Red);
                    AssertColor(image.Bitmap.GetPixel(220, 70), Color.Lime);
                    AssertColor(image.Bitmap.GetPixel(370, 70), Color.Blue);
                    Assert(image.Bitmap.Size == ClientSize, "截图物理尺寸错误。");
                }
            });
            Check("捕获引擎合成、区域裁切、PNG 归档且不覆盖", delegate {
                Assert(Native.GetForegroundWindow() == Handle, "测试窗口未处于前台，请在桌面空闲时重试。");
                using (var image = CaptureEngine.Read(bounds, true))
                using (var crop = image.Bitmap.Clone(new Rectangle(30, 30, 60, 60), System.Drawing.Imaging.PixelFormat.Format32bppRgb))
                {
                    AssertColor(crop.GetPixel(20, 20), Color.Red);
                    string first = Archive.Save(crop, output, "测试游戏", CaptureMode.Region);
                    string second = Archive.Save(crop, output, "测试游戏", CaptureMode.Region);
                    Assert(first != second && File.Exists(first) && File.Exists(second), "连续截图出现覆盖。");
                    Assert(Path.GetDirectoryName(first) == Path.Combine(output, "测试游戏", DateTime.Now.ToString("yyyy-MM-dd")), "未按游戏和日期归档。");
                    using (var restored = new Bitmap(first)) { Assert(restored.Size == crop.Size, "PNG 尺寸错误。"); AssertColor(restored.GetPixel(20, 20), Color.Red); }
                    report.Add("INFO 捕获后端：" + image.Backend + (image.Warning == "" ? "" : "；" + image.Warning));
                }
            });
            Check("反向拖动和负坐标框选", delegate {
                Rectangle rect = RegionSelector.Normalize(new Point(100, 70), new Point(-20, -30));
                Assert(rect == new Rectangle(-20, -30, 120, 100), "反向区域计算错误。");
            });
            Check("全黑画面提醒，不误判正常色块", delegate {
                using (var black = new Bitmap(20, 20))
                {
                    Assert(CaptureEngine.MostlyBlack(black), "未提示黑图。");
                    using (var g = Graphics.FromImage(black)) g.Clear(Color.White);
                    Assert(!CaptureEngine.MostlyBlack(black), "正常图片被判断为全黑。");
                }
            });
            uint modifiers = 1 | 2 | 4;
            bool hotkeyRegistered = Native.RegisterHotKey(Handle, 901, modifiers | Native.MOD_NOREPEAT, (uint)Keys.F11);
            Check("全局快捷键注册及冲突检测", delegate {
                Assert(hotkeyRegistered, "测试快捷键注册失败，系统错误 " + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                using (var duplicate = new Form()) Assert(!Native.RegisterHotKey(duplicate.Handle, 902, modifiers, (uint)Keys.F11), "重复快捷键没有报告冲突。");
            });
            if (hotkeyRegistered)
            {
                try
                {
                    // 发送真正的组合键，验证 Windows 把 WM_HOTKEY 交给应用，而非直接调用截图函数。
                    Native.keybd_event(0x11, 0, 0, UIntPtr.Zero); Native.keybd_event(0x12, 0, 0, UIntPtr.Zero); Native.keybd_event(0x10, 0, 0, UIntPtr.Zero);
                    Native.keybd_event((byte)Keys.F11, 0, 0, UIntPtr.Zero);
                }
                finally
                {
                    Native.keybd_event((byte)Keys.F11, 0, 2, UIntPtr.Zero);
                    Native.keybd_event(0x10, 0, 2, UIntPtr.Zero); Native.keybd_event(0x12, 0, 2, UIntPtr.Zero); Native.keybd_event(0x11, 0, 2, UIntPtr.Zero);
                }
                await Task.Delay(250);
                Native.UnregisterHotKey(Handle, 901);
                Check("真实键盘组合收到 WM_HOTKEY", delegate { Assert(receivedHotkey, "没有收到全局快捷键消息。"); });
            }
            LibraryTests.Run(Check, output);
            report.Add("RESULT " + (failures == 0 ? "PASS" : "FAIL") + "；失败数：" + failures);
            File.WriteAllLines(Path.Combine(output, "test-report.txt"), report);
            if (Native.IsWindow(originalForeground)) Native.SetForegroundWindow(originalForeground);
            Close();
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == Native.WM_HOTKEY && message.WParam.ToInt32() == 901) receivedHotkey = true;
            base.WndProc(ref message);
        }
    }
}
