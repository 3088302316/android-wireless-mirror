# 安卓无线投屏

面向 Windows 10 22H2 x64 和 Windows 11 x64 的安卓投屏工具。支持扫码和六位码无线配对、多设备独立投屏、手机熄屏、断线重连，以及实时渲染帧率和网络往返时间。

## 构建与运行

使用 Windows 自带的 .NET Framework 4.x x64 C# 编译器。用 PowerShell 执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

运行文件生成在 `dist/`，有电脑出声和手机出声两个入口。运行方法见 [使用说明](使用说明.txt)。

构建脚本默认从本地 `runtime/` 读取依赖，并按 `runtime.sha256` 校验。移到新电脑时，可解压与本源码对应的便携版，再向 `build.ps1` 传入 `-RuntimeDirectory` 指定解压目录。

## 源码和依赖

- C# WinForms 启动器和设备管理界面使用 AntdUI 2.4.18，二维码使用 QRCoder 1.8.1，图标使用 Lucide 0.468.0。
- 投屏内核基于 scrcpy v5.0.1，基线提交为 `a60891aea193d92e7e5c3942700eca63f9d19a5f`。标题栏菜单和实时帧率的改动见 [scrcpy 补丁](scrcpy-5.0.1-windows-caption.patch)。编译投屏内核前，在该基线的干净源码树应用此补丁。
- 依赖许可见本目录的 `*-LICENSE.txt`。 `LICENSE.txt` 为 scrcpy 上游许可。
- 设备历史、连接设置和日志存于 `%LOCALAPPDATA%\AndroidWirelessMirror`。本地运行依赖和构建产物由 Git 忽略。

## 本机入口

在 `runtime` 目录可直接打开投屏，当前便携版在 `发布` 目录。

## Windows 安装包

双击 `发布\安卓无线投屏-安装程序-Windows-x64.exe`，选择安装位置。开始菜单提供电脑出声和手机出声两个入口，桌面入口可在安装时选择。可从 Windows 的「已安装的应用」卸载。

维护者可以使用 Inno Setup 6.7.4 或兼容版本重新生成安装包：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-installer.ps1 -IsccPath "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
```

构建需要 `runtime.sha256` 对应的运行依赖。可用 `-LanguageFile` 指定 Inno Setup 的中文语言文件。
