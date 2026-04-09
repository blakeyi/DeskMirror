# DeskMirror

将主屏桌面图标镜像到副屏，实现多显示器环境下的桌面图标同步与交互。

## 许可与版权

**DeskMirror** · Copyright © 2026 blakeyi  

本软件以 [**GNU General Public License v3.0**](LICENSE)（GPL-3.0）发布，完整许可证文本见仓库根目录 `LICENSE`。

## 技术栈

- C# / WPF / .NET 10
- 目标平台：Windows 10 / 11
- DPI 感知：PerMonitorV2

## 功能列表

### 桌面图标镜像

- 读取主屏 Explorer 桌面的所有图标（名称、位置）
- 通过 `SysListView32` 跨进程读取图标坐标
- 使用 `IShellItemImageFactory` 获取 512×512 高清图标
- 按主副屏比例映射坐标，在副屏 Overlay 窗口上渲染

### 副屏 Overlay 窗口

- 无边框、透明背景，不出现在任务栏
- 通过 `WM_WINDOWPOSCHANGING` Hook 始终保持在桌面层（Z-order 最底）
- 支持 DPI 缩放适配

### 图标交互

- **双击打开**：等同于在真实桌面双击图标
  - 应用已有可见窗口 → 激活并置前
  - 应用隐藏在系统托盘 → 通过 UI Automation 模拟点击托盘图标唤出（先单击，无响应则自动双击，兼容微信/Everything 等不同行为的应用）
  - 应用未运行 → `ShellExecute` 启动
- **单击选中**：蓝色高亮选中效果
- **右键菜单**：打开、打开文件位置、属性、复制路径；可执行程序支持“以管理员身份运行”

### 拖放支持

- **从副屏图标拖出**：
  - 可将文件夹/应用图标拖到副屏其他图标上
  - `.lnk` 快捷方式自动解析为真实路径（文件夹快捷方式 → 实际文件夹路径）
  - 使用内部数据格式，拖到副屏外部不会触发意外的复制/移动操作
- **从外部拖入**：
  - 从 Explorer 拖文件到应用图标 → 用该应用打开文件
  - 从 Explorer 拖文件到文件夹图标 → 自动解析 `.lnk` 为真实路径，调用 `SHFileOperation` 复制文件（带进度条、重名确认、支持 Ctrl+Z 撤销）
  - 拖入时图标蓝色高亮反馈

### 托盘图标激活（Win11 适配）

- 通过 UI Automation 在任务栏搜索目标图标
- 自动点击"显示隐藏的图标"打开溢出面板搜索
- 过滤任务栏应用按钮，只匹配托盘图标
- 先尝试单击，400ms 内无窗口出现则自动改为双击
- 模拟点击后自动还原鼠标位置
- Win10 兼容：支持 ToolbarWindow32 传统方式

### 实时同步

- `SHChangeNotifyRegister` 监听桌面文件变更（新增、删除、重命名）
- `DispatcherTimer` 定时轮询图标坐标变化

### 系统托盘

- 使用 `Hardcodet.NotifyIcon.Wpf` 实现托盘图标
- 托盘菜单：显示/隐藏、刷新图标、设置、开机自启、退出

### 设置与配置

- 目标显示器选择
- 图标大小调整
- 刷新间隔设置
- 镜像主屏布局 / 自动网格排列切换
- 开机自启（注册表）
- 配置持久化到 `%AppData%` JSON 文件

## 项目结构

```
DeskMirror/
├── DeskMirror.csproj / DeskMirror.slnx  # 项目文件与解决方案
├── App.xaml / App.xaml.cs          # 应用入口、托盘图标、全局异常处理
├── GlobalUsings.cs                 # WPF/WinForms 类型歧义解决
├── Models/
│   └── DesktopIcon.cs              # 桌面图标数据模型
├── ViewModels/
│   └── MirrorViewModel.cs          # 图标集合、打开/激活逻辑
├── Views/
│   ├── MirrorWindow.xaml/.cs       # 副屏 Overlay 窗口、拖放事件处理
│   └── SettingsWindow.xaml/.cs     # 设置界面
├── Shell/
│   ├── DesktopIconEnumerator.cs    # 桌面图标枚举（跨进程读取）
│   ├── DesktopIconWatcher.cs       # 桌面变更监听
│   └── TrayIconHelper.cs           # 托盘图标点击（UI Automation + Toolbar）
├── Monitor/
│   ├── MonitorInfo.cs              # 多显示器检测
│   └── DpiHelper.cs                # DPI 缩放工具
├── Services/
│   └── SettingsService.cs          # 设置持久化、开机自启
├── Native/
│   └── NativeMethods.cs            # Win32 P/Invoke 声明
├── DeskMirror.Tests/               # xUnit 测试项目
└── app.manifest                    # PerMonitorV2 DPI 声明
```

## 构建与运行

### 开发调试

用于本地开发、调试和查看 `debug.log`：

```powershell
dotnet build
dotnet run
```

调试输出位于：

```text
bin/Debug/net10.0-windows/
```

### Release 发布

用于生成发给其他人的单文件版本：

```powershell
dotnet publish -c Release
```

发布完成后，可分发的单文件产物位于：

```text
dist/release/DeskMirror.exe
```

### 运行环境

- 仅支持 Windows 10 / Windows 11 64 位
- `Release` 为单文件自包含发布，目标机器通常无需额外安装 .NET
- `Debug` 为开发构建，包含日志与多文件输出，不建议直接分发

### 发布说明

- 对外分发时，直接提供 `dist/release/DeskMirror.exe`
- `Release` 默认不输出 `debug.log`
- 若需排查问题，建议使用 `Debug` 版本复现并查看 `bin/Debug/net10.0-windows/debug.log`

## 依赖

| 包 | 用途 |
|---|------|
| CommunityToolkit.Mvvm | MVVM 框架（ObservableObject、RelayCommand） |
| Hardcodet.NotifyIcon.Wpf | 系统托盘图标 |
| Vanara.PInvoke.Shell32 | Shell API P/Invoke |
| Vanara.PInvoke.User32 | User32 API P/Invoke |
| Vanara.PInvoke.ComCtl32 | ComCtl32 API P/Invoke |
