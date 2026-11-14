; PluginShellWin · 安装程序（Inno Setup 6）
; 页面结构与选项由 common.iss 统一提供；这里只声明应用身份与要安装的文件。
; 构建：installer\build-setup.cmd   产物：dist\PluginShellWin-Setup-1.2.0.exe
;
; 前置：先产出 dist\app\PluginShellWin.exe（dotnet publish）与
;       runtime\dist\PluginRuntime.exe（python runtime/build_runtime.py）。

#define AppName "PluginShellWin"
#define AppSlug "PluginShellWin"
#define DirName "PluginShellWin"
#define AppVersion "1.2.0"
#define AppId "{{5C2A9E14-7B3D-4A81-9F62-1D8E4C7B3A55}"
#define AppExe "PluginShellWin.exe"
#define AppRepo "PluginShellWin"
#define AppTagline "C#（.NET 8 / WinForms）原生插件宿主，内置 Python 插件运行时，无 Web 技术。"
#define IconPath "assets\app.ico"

; 可选内容：随包附带示例插件
#define FeatureSamples

#include "common.iss"

[Files]
Source: "{#AppRoot}\dist\app\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppRoot}\runtime\dist\PluginRuntime.exe"; DestDir: "{app}\runtime"; Flags: ignoreversion
Source: "{#AppRoot}\src\PluginShellWin.App\config\appsettings.json"; DestDir: "{app}\config"
Source: "{#AppRoot}\plugins\README.md"; DestDir: "{app}\plugins"
Source: "{#AppRoot}\README.md"; DestDir: "{app}"
Source: "{#AppRoot}\LICENSE"; DestDir: "{app}"
Source: "{#AppRoot}\installer\staging\INSTALL-CN.txt"; DestDir: "{app}"
Source: "{#AppRoot}\..\PluginShell\sample_plugins\*"; DestDir: "{app}\plugins"; Flags: recursesubdirs createallsubdirs; Tasks: samples; Excludes: ".win-state.json,.exe-state.json,.gitignore"

[Dirs]
; 即使不装示例插件，也先建好空插件目录，首次启动是「空壳」而不是报错
Name: "{app}\plugins"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: startmenuicon
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon
