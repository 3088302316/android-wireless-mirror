#ifndef PackageId
  #define PackageId "AndroidWirelessMirror.Personal"
#endif
#ifndef PayloadDir
  #define PayloadDir "dist"
#endif
#ifndef LanguageFile
  #define LanguageFile "compiler:Default.isl"
#endif

[Setup]
AppId={#PackageId}
AppName=手机投屏
AppVersion=1.1.6
DefaultDirName={localappdata}\Programs\AndroidWirelessMirror
DefaultGroupName=手机投屏
DisableDirPage=no
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19045
UninstallDisplayIcon={app}\手机投屏.exe
SetupIconFile=投屏.ico
OutputBaseFilename=手机投屏-安装程序-Windows-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "setup"; MessagesFile: "{#LanguageFile}"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[InstallDelete]
Type: files; Name: "{app}\手机投屏-手机出声.exe"
Type: files; Name: "{group}\手机投屏（手机出声）"
Type: files; Name: "{userdesktop}\手机投屏（手机出声）"
Type: files; Name: "{app}\安卓无线投屏.exe"
Type: files; Name: "{group}\安卓无线投屏.lnk"
Type: files; Name: "{userdesktop}\安卓无线投屏.lnk"

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\手机投屏"; Filename: "{app}\手机投屏.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\手机投屏"; Filename: "{app}\手机投屏.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\手机投屏.exe"; Description: "{cm:LaunchProgram,手机投屏}"; Flags: nowait postinstall skipifsilent
