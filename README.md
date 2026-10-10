# GameShot

Windows 游戏库与截图工具。用全局快捷键截图，按游戏和日期保存 PNG；在游戏库中浏览截图、记录游玩时长并启动游戏。

- 简洁的苹果风界面，支持浅色、深色与跟随系统主题。
- 游戏卡片、搜索、自定义封面，以及本地截图图库。
- F8 截取当前屏幕、F9 框选区域、F10 截取窗口、Ctrl + Shift + F8 截取所有显示器。
- 自动记录游戏运行与前台时间；同一游戏多个进程不重复计时。
- 截图优先使用 DirectX Desktop Duplication，失败时回退到普通桌面捕获。
- C# / .NET 10 / Windows Forms，关键逻辑有中文注释，无第三方运行依赖。

## 运行

需要 Windows x64 和 .NET 10 Desktop Runtime。从 [最新版本](https://github.com/Ackerley1123/GameShot/releases/latest) 下载程序包并解压，或按下方步骤编译源码。

打开 `GameShot/GameShot.exe`，点击“添加游戏”，填写名称和实际进程名；选择启动文件后可以直接启动游戏。

关闭窗口后工具仍在系统托盘运行。时长仅记录 GameShot 开启期间，不导入 Steam 历史时长。截图和配置均保存在本地。

## 编译

安装 .NET 10 SDK，然后在 PowerShell 中运行：

```powershell
cd GameShot
.\build.ps1

# 只检查游戏库、进程计时、图库和数据保存：
.\build.ps1 -TestLibrary

# 在桌面空闲时验证实际截图和全局快捷键：
.\build.ps1 -Test
```

输出为 `GameShot/release/GameShot.exe`，并复制到 `GameShot/GameShot.exe`。运行编译脚本前请从托盘退出已运行的 GameShot，以便替换程序文件。

## 使用说明

完整功能、目录结构、源码文件说明及捕获兼容性见 [使用说明](GameShot/README.md)。受捕获保护的内容、安全桌面及部分特殊全屏模式可能无法截图。

仓库只包含源码、图标和说明；个人截图、运行配置、游戏时长、构建缓存及测试图片不纳入版本管理。
