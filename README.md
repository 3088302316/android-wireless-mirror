# 安卓无线投屏

在 Windows 上通过 Wi-Fi 查看并操作安卓手机。同一台电脑可以同时打开多台手机，每台设备对应一个独立窗口。

**[下载 Windows 安装包或便携版](https://github.com/3088302316/android-wireless-mirror/releases/latest)** · Windows 10 22H2 x64 / Windows 11 x64

| 文件 | 用法 |
| --- | --- |
| `android-wireless-mirror-setup-x64.exe` | 双击安装。可以自选目录，开始菜单提供两种声音模式。 |
| `android-wireless-mirror-portable-x64.zip` | 完整解压后双击启动。保留同目录中的 DLL 和其他依赖。 |

## 首次连接

1. 电脑和手机连接同一个 Wi-Fi，在手机的开发者选项里开启「无线调试」。
2. 在电脑端选「扫码配对」或「六位码配对」。手动配对时，地址、端口和配对码均取自手机的配对弹窗。
3. 在设备列表里选中手机，点击「打开投屏」。需要同时显示第二台手机时，回到设备列表再打开它。

默认入口把媒体声音传到电脑。「手机出声」入口把声音留在手机上。窗口菜单提供熄屏、快捷键和画质设置，分辨率、帧率上限和码率可以独立调整。标题栏显示实际渲染帧率和网络往返时间，后者只衡量电脑与手机之间的网络往返。

手机需支持 Android 11 起提供的 ADB 无线调试。输入控制和声音转发受机型及系统限制。更多操作见 [使用说明](使用说明.txt)。

## 从源码构建

在 Windows PowerShell 运行以下命令。新检出时，先从对应的私有发布下载便携 ZIP，脚本会校验并提取构建所需的依赖。已有有效 `runtime/` 可直接构建。

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
