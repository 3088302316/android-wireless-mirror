# 安卓无线投屏

在 Windows 上通过 Wi-Fi 查看并操作安卓手机。同一台电脑可以同时打开多台手机，每台设备对应一个独立窗口。

**[下载 Windows 安装包或便携版](https://github.com/3088302316/android-wireless-mirror/releases/latest)** · Windows 10 22H2 x64 / Windows 11 x64

| 文件 | 用法 |
| --- | --- |
| `android-wireless-mirror-setup-x64.exe` | 双击安装。可以自选目录，声音输出和低延迟模式都在程序内切换。 |
| `android-wireless-mirror-portable-x64.zip` | 完整解压后双击启动。保留同目录中的 DLL 和其他依赖。 |

## 首次连接

1. 电脑和手机连接同一个 Wi-Fi，在手机的开发者选项里开启「无线调试」。
2. 在电脑端选「扫码配对」或「六位码配对」。手动配对时，地址、端口和配对码均取自手机的配对弹窗。
3. 在设备列表里选中手机，点击「打开投屏」。需要同时显示第二台手机时，回到设备列表再打开它。

声音输出可以在设备页、连接设置或投屏时的托盘菜单里切换：电脑、手机、电脑+手机、都不输出；切换声音会重连当前窗口。窗口菜单提供熄屏、快捷键和画质设置，分辨率、帧率上限和码率可以独立调整。设备中心主内容区使用内置固定壁纸，按 cover 居中裁切，不提供壁纸选择入口。投屏时按 F11 或右键托盘图标的「旋转屏幕」可手动切换手机横竖屏。设备页的「低延迟」会套用 0ms 视频缓冲和 90fps；鼠标使用普通桌面指针，不会被投屏窗口捕获。标题栏默认不显示实时 FPS 和网络往返统计；可在「连接设置」打开「实时统计」，保存并重连后显示。

## 设备支持

- 电脑端：Windows 10 22H2 x64 / Windows 11 x64。
- 手机和平板：需要 Android 11 或更高版本，并在开发者选项中提供「无线调试」；电脑与设备连接同一个 Wi-Fi。
- 大多数 Android 品牌（小米/Redmi、OPPO/一加、vivo/iQOO、三星、Pixel、摩托罗拉等）可用。
- 华为/荣耀的 EMUI 或基于 Android 的 HarmonyOS，只有在系统提供 ADB 无线调试时可用；纯血 HarmonyOS NEXT 只提供 hdc，不适用。
- iPhone / iPad 不适用，iOS 屏幕镜像使用 AirPlay，与本程序无关。
- Android 10 及以下没有系统级二维码/六位码无线配对，不在正式支持范围内。
- 声音转发依赖 Android 11+ 的音频采集；部分厂商 ROM、DRM 应用或受保护内容可能限制采集。
- 锁屏密码界面属于 Android 安全图层，无法投屏；解锁手机后画面会恢复。
- 扫码配对在部分 ROM 上兼容性一般，优先使用「六位码配对」。
- 高分辨率平板建议把分辨率上限降到 1600 或 1280，并使用 5GHz Wi-Fi。

输入控制和声音转发受具体机型、系统版本和厂商限制。更多操作见 [使用说明](使用说明.txt)。

## 从源码构建

在 Windows PowerShell 运行以下命令。新检出时，先从对应的 Release 下载便携 ZIP，脚本会校验并提取构建所需的依赖。已有有效 `runtime/` 可直接构建。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\restore-runtime.ps1 -Archive "C:\Downloads\android-wireless-mirror-portable-x64.zip"
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

主程序使用 .NET Framework x64 C# 编译器，输出在 `dist/`。生成安装包及便携包需要 Inno Setup 6.7.4：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-installer.ps1 -IsccPath "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
```

发布包输出到 `发布/`。可用 `-LanguageFile` 指定安装器语言文件。设备历史、设置及日志保存在 `%LOCALAPPDATA%\AndroidWirelessMirror`。

设备中心基于 C# WinForms、AntdUI 2.4.18 和 QRCoder 1.8.1，图标来自 Lucide 0.468.0。投屏内核基于 scrcpy v5.0.1，基线提交为 `a60891aea193d92e7e5c3942700eca63f9d19a5f`。重建内核时，在该基线应用 [Windows 标题栏补丁](scrcpy-5.0.1-windows-caption.patch)。第三方许可随发布包附带。
