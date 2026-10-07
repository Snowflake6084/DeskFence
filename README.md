# DeskFence 桌面格子

[中文](#中文) | [English](#english)

---

## 中文

一个轻量的 Windows 桌面整理工具：在桌面上放几个半透明的格子，把桌面文件拖进去分类收纳。

### 功能

- **格子**：新建、命名（双击标题）、拖动位置、拖边缘改大小、锁定位置、折叠成只剩标题栏
- **半透明**：背景不透明度、整体不透明度两个滑块，实时生效
- **两种显示模式**：图标模式 / 列表模式，标题栏一键切换
- **收纳文件**：把桌面文件、快捷方式、文件夹直接拖进格子，桌面上就不再显示
- **两种退出**："退出（文件放回桌面）"把收纳的文件放回桌面；"退出（文件不恢复到桌面）"让文件留在格子里。崩溃、关机、重启时文件不动，开机后照常显示
- **拖动**：格子之间互相拖、格子内拖动排序、拖到微信等程序发送、拖出到桌面空白处放回桌面
- **文件右键菜单**：和资源管理器一样（打开方式、发送到、复制、属性等），文件名太长时鼠标停留显示全名
- **自定义位置**：存放文件夹和配置文件位置都可以改（日志固定在 `%APPDATA%\DeskFence`）
- **界面语言**：中文 / English / 日本語
- **其他**：开机自动启动、隐藏托盘图标（从格子右上角齿轮打开设置）、Win+D 显示桌面时格子不会被隐藏、高 DPI 屏幕适配

### 下载使用

1. 到 [Releases](../../releases) 下载 `DeskFence.zip`，解压
2. 双击 `DeskFence.exe` 运行（Windows 10/11 自带 .NET Framework 4.x，不用装其他东西）
3. 程序没有数字签名，杀毒软件提示时选"允许"

详细操作见压缩包里的 `README.txt`。

### 工作原理

- 收纳的文件会从桌面移到 `%LOCALAPPDATA%\DeskFence\Store`（与桌面同盘，移动只是改名，不复制数据），退出时移回桌面原位置；放回时遇到同名文件会自动改名为"名字 (2)"，不会覆盖
- 正在被打开的文件（比如 Excel 开着的表）无法移动，会暂时留在桌面，关闭后自动收纳
- `DeskFence.exe` 本身不能放进格子（否则开机自启找不到它），可以放它的快捷方式
- 公共桌面（所有用户共用）里的快捷方式需要管理员权限才能移动，会继续留在桌面
- 设置保存在 `%APPDATA%\DeskFence\config.xml`，出错日志在同目录 `error.log`

### 从源码编译

不需要安装 Visual Studio，双击 `build.bat` 即可：它使用 Windows 自带的 C# 编译器（`csc.exe`，.NET Framework 4.x），生成 `DeskFence.exe`。

源码是纯 C#（WinForms），兼容 C# 5 语法：

| 文件 | 内容 |
|---|---|
| `src/Program.cs` | 入口、单实例、输入框、自测 |
| `src/Controller.cs` | 托盘、格子管理、收纳/放回 |
| `src/FenceForm.cs` | 格子窗口的绘制、鼠标、拖放 |
| `src/Model.cs` | 配置、文件收纳存放、布局计算 |
| `src/IconCache.cs` | 读取系统文件图标 |
| `src/Lang.cs` | 中英日界面文字 |
| `src/OpacityDialog.cs` | 透明度设置窗口 |
| `src/Native.cs` | Win32 API 声明 |

### 许可证

[MIT](LICENSE)：任何人都可以免费使用、修改、分发，包括商业用途。

---

## English

A lightweight desktop organizer for Windows: put a few translucent "fences" on your desktop and drag desktop files into them.

### Features

- **Fences**: create, rename (double-click the title), move, resize from the edges, lock position, collapse to the title bar
- **Translucency**: separate sliders for background opacity and overall opacity, applied live
- **Two views**: icon view / list view, toggled from the title bar
- **Store files**: drag desktop files, shortcuts or folders into a fence and they no longer appear on the desktop
- **Two ways to exit**: "Exit (stored files go back to desktop)" returns stored files to the desktop; "Exit (keep files in fences)" leaves them stored. On a crash, shutdown or restart files stay stored and show up again at next start
- **Drag and drop**: between fences, reorder inside a fence, drop onto other apps (e.g. a chat window) to send, or drag onto an empty desktop area to put it back on the desktop
- **File context menu**: the same right-click menu as File Explorer (Open with, Send to, Copy, Properties…); hover to see the full name when it is cut off
- **Custom locations**: choose where stored files and the settings file live (the log stays in `%APPDATA%\DeskFence`)
- **UI languages**: 中文 / English / 日本語
- **Also**: start with Windows, hide the tray icon (open settings from the gear button on any fence), fences stay visible on Win+D, high-DPI aware

### Download

1. Download `DeskFence.zip` from [Releases](../../releases) and unzip it
2. Run `DeskFence.exe` (Windows 10/11 ship with .NET Framework 4.x; nothing else to install)
3. The executable is not code-signed; allow it if your antivirus asks

See `README.txt` in the zip for detailed usage (in Chinese).

### How it works

- Stored files are moved from the desktop to `%LOCALAPPDATA%\DeskFence\Store` (same drive as the desktop, so it is a rename, not a copy) and moved back when you choose "Exit (stored files go back to desktop)". The exact folder is written in a comment at the top of `%APPDATA%\DeskFence\config.xml`. If a file with the same name already exists on the desktop, the returned file is renamed to "name (2)" instead of overwriting
- Files that are currently open (e.g. a workbook open in Excel) can't be moved; they stay on the desktop and are stored automatically once closed
- `DeskFence.exe` itself can't be stored in a fence (auto start would no longer find it); store a shortcut to it instead
- Shortcuts on the Public Desktop need administrator rights to move and therefore stay on the desktop
- Settings are saved in `%APPDATA%\DeskFence\config.xml`; errors are logged to `error.log` in the same folder

### Build from source

No Visual Studio required: run `build.bat`. It uses the C# compiler that ships with Windows (`csc.exe`, .NET Framework 4.x) to produce `DeskFence.exe`.

The source is plain C# (WinForms), compatible with C# 5:

| File | Contents |
|---|---|
| `src/Program.cs` | Entry point, single instance, input dialog, self-test |
| `src/Controller.cs` | Tray, fence management, store/release |
| `src/FenceForm.cs` | Fence window drawing, mouse handling, drag and drop |
| `src/Model.cs` | Settings, file storage, layout math |
| `src/IconCache.cs` | System file icons |
| `src/Lang.cs` | UI text in Chinese, English and Japanese |
| `src/OpacityDialog.cs` | Transparency dialog |
| `src/Native.cs` | Win32 API declarations |

### License

[MIT](LICENSE): free for anyone to use, modify and distribute, including commercial use.
