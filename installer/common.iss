; ============================================================================
;  common.iss — 统一安装程序定义（ml-308 全体独立应用共用）
;
;  这个文件在所有仓库里必须保持逐字节一致；升级时从任一仓库整份复制，
;  不要按应用分叉修改。应用差异只允许通过下面的 #define 与 [Files]/[Icons]
;  注入，页面顺序、任务项、文案、安装/卸载行为都由本文件统一决定。
;
;  由 setup.iss 提供的定义：
;    AppName        显示名称（可中文）
;    AppSlug        ASCII 短名，用于产物文件名
;    AppVersion     版本号，如 1.1.0
;    AppId          稳定的 AppId GUID，含花括号，如 {{1111-...}
;    AppExe         主程序文件名
;    AppTagline     一句话说明，显示在欢迎页
;    AppPublisher   发布者（默认 ml-308）
;    AppRepo        GitHub 仓库名（不含 owner）
;
;  可选特性开关：
;    #define FeatureAutostart          增加「开机自动启动」任务
;    #define FeatureFileAssoc          增加「在打开方式中注册」任务
;    #define FeatureSamples            增加「安装示例内容」任务
;    #define FeatureDirPage            强制显示安装位置页（默认就显示）
;
;  可选覆盖：
;    AppRoot        仓库根目录相对 installer 的路径（默认 ..）
;    IconPath       安装包图标，相对 AppRoot（默认 icon.ico）
;    LicensePath    许可协议，相对 AppRoot（默认 LICENSE）
;    OutDir         产物目录（默认 ..\dist）
;    RunValue       开机自启注册表值名（默认 AppSlug）
;    LegacyUninstallKey  旧安装器留下的 HKCU 卸载项，安装后清理
;    LegacyRunValue      旧安装器留下的自启动值名，安装后清理
; ============================================================================

#ifndef AppName
  #error setup.iss 必须先 #define AppName
#endif
#ifndef AppSlug
  #error setup.iss 必须先 #define AppSlug
#endif
#ifndef AppId
  #error setup.iss 必须先 #define AppId
#endif
#ifndef AppExe
  #error setup.iss 必须先 #define AppExe
#endif

#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif
#ifndef AppPublisher
  #define AppPublisher "ml-308"
#endif
#ifndef AppRepo
  #define AppRepo "ml-308"
#endif
#ifndef AppTagline
  #define AppTagline ""
#endif
#ifndef AppRoot
  #define AppRoot ".."
#endif
#ifndef IconPath
  #define IconPath "icon.ico"
#endif
#ifndef LicensePath
  #define LicensePath "LICENSE"
#endif
#ifndef OutDir
  #define OutDir "..\dist"
#endif
#ifndef RunValue
  #define RunValue AppSlug
#endif
; 安装目录名：默认与 AppSlug 一致；沿用旧目录时（升级不换位置）单独覆盖
#ifndef DirName
  #define DirName AppSlug
#endif

#define AppUrl "https://github.com/" + AppPublisher + "/" + AppRepo

[Setup]
; ---- 身份 ----------------------------------------------------------------
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
AppCopyright=Copyright (C) 2026 {#AppPublisher} · MIT License

; ---- 安装位置：每用户安装，不需要管理员，但允许用户改路径 ----------------
DefaultDirName={localappdata}\Programs\{#DirName}
DefaultGroupName={#AppName}
DisableDirPage=no
UsePreviousAppDir=yes
AlwaysShowDirOnReadyPage=yes
DirExistsWarning=auto
AllowNoIcons=yes
; 快捷方式用任务复选框控制，不另开「开始菜单文件夹」页
DisableProgramGroupPage=yes

; ---- 外观：Inno 6 现代向导，与主流开源安装器一致 -------------------------
WizardStyle=modern
WizardSizePercent=110
WizardSmallImageFile=assets\wizard-small.png
ShowLanguageDialog=auto
UsePreviousLanguage=no
SetupIconFile={#AppRoot}\{#IconPath}
UninstallDisplayName={#AppName} {#AppVersion}
UninstallDisplayIcon={app}\{#AppExe}
LicenseFile={#AppRoot}\{#LicensePath}

; ---- 权限与运行时 --------------------------------------------------------
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
MinVersion=6.1sp1

; ---- 产物 ----------------------------------------------------------------
OutputDir={#OutDir}
OutputBaseFilename={#AppSlug}-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
SetupLogging=yes
CloseApplications=yes
RestartApplications=no
RestartIfNeededByRun=no
OutputManifestFile={#OutDir}\{#AppSlug}-Setup-{#AppVersion}-manifest.txt

; ---- 版本资源 ------------------------------------------------------------
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
VersionInfoCopyright=Copyright (C) 2026 {#AppPublisher}

#ifdef FeatureFileAssoc
ChangesAssociations=yes
#endif

[Languages]
; 中文在前：系统语言匹配不上时它就是默认语言
Name: "zhcn"; MessagesFile: "ChineseSimplified.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
; ---- 任务分组与选项（全部应用同一套说法）--------------------------------
zhcn.GroupShortcuts=快捷方式
zhcn.GroupStartup=启动
zhcn.GroupIntegration=系统集成
zhcn.GroupContent=附加内容
zhcn.TaskDesktopIcon=创建桌面快捷方式(&D)
zhcn.TaskStartMenuIcon=创建开始菜单快捷方式(&S)
zhcn.TaskAutostart=登录时自动启动(&A)
zhcn.TaskAutostartHint=建议开启：本程序是常驻型工具，随系统启动后待在后台
zhcn.TaskFileAssoc=在「打开方式」中注册(&O)
zhcn.TaskFileAssocHint=不会抢占现有默认程序，仅在右键菜单里多一个入口
zhcn.TaskSamples=安装示例插件(&P)
zhcn.DirNotEmpty=目标文件夹中已有文件：%n%1%n%n继续安装会覆盖同名文件，其他文件不受影响。要继续吗？
zhcn.DirCannotCreate=无法创建安装目录：%n%1%n%n常见原因：该位置需要管理员权限、磁盘只读，或被安全软件拦截。%n建议改到「文档」等用户目录，或以管理员身份重新运行。
zhcn.DirEmpty=请填写安装位置。
zhcn.LaunchApp=立即运行 {#AppName}(&R)
zhcn.LegacyNotice=检测到旧版安装记录，安装完成后会自动清理旧注册项与旧卸载程序。

en.GroupShortcuts=Shortcuts
en.GroupStartup=Startup
en.GroupIntegration=System integration
en.GroupContent=Additional content
en.TaskDesktopIcon=Create a &desktop shortcut
en.TaskStartMenuIcon=Create a &Start Menu shortcut
en.TaskAutostart=Start automatically at sign-in (&A)
en.TaskAutostartHint=Recommended for this always-on tool
en.TaskFileAssoc=Register in "Open with" (&O)
en.TaskFileAssocHint=Does not take over the current default handler
en.TaskSamples=Install sample plugins (&P)
en.DirNotEmpty=The target folder already contains files:%n%1%n%nSetup will overwrite files with the same name; other files are left untouched. Continue?
en.DirCannotCreate=Cannot create the installation folder:%n%1%n%nCommon causes: the location requires administrator rights, the disk is read-only, or security software blocked the write.%nChoose a folder under your user profile, or run Setup as administrator.
en.DirEmpty=Please enter an installation folder.
en.LaunchApp=Launch {#AppName} now (&R)
en.LegacyNotice=An older installation was found; its registry entries and uninstaller will be removed after setup.

[Messages]
; 欢迎页：项目名 + 版本 + 一句话定位，和主流开源安装器一致
zhcn.WelcomeLabel1=欢迎使用 [name] 安装向导
zhcn.WelcomeLabel2=即将在你的计算机上安装 [name/ver]。%n%n{#AppTagline}%n%n点击「下一步」继续，或点击「取消」退出。
zhcn.FinishedHeadingLabel=安装完成
zhcn.FinishedLabel=已成功安装 [name/ver]。%n%n卸载入口在「设置 → 应用 → 已安装的应用」中，也可使用安装目录里的 unins000.exe。
en.WelcomeLabel1=Welcome to the [name] Setup Wizard
en.WelcomeLabel2=This will install [name/ver] on your computer.%n%n{#AppTagline}%n%nClick Next to continue, or Cancel to exit.
en.FinishedHeadingLabel=Installation complete
en.FinishedLabel=[name/ver] was installed successfully.%n%nTo uninstall later, use Settings > Apps > Installed apps, or unins000.exe in the installation folder.

[Tasks]
Name: "desktopicon"; Description: "{cm:TaskDesktopIcon}"; GroupDescription: "{cm:GroupShortcuts}"
Name: "startmenuicon"; Description: "{cm:TaskStartMenuIcon}"; GroupDescription: "{cm:GroupShortcuts}"
#ifdef FeatureAutostart
Name: "autostart"; Description: "{cm:TaskAutostart}"; GroupDescription: "{cm:GroupStartup}"; Flags: unchecked
#endif
#ifdef FeatureFileAssoc
Name: "fileassoc"; Description: "{cm:TaskFileAssoc}"; GroupDescription: "{cm:GroupIntegration}"; Flags: unchecked
#endif
#ifdef FeatureSamples
Name: "samples"; Description: "{cm:TaskSamples}"; GroupDescription: "{cm:GroupContent}"; Flags: unchecked
#endif

[Run]
; 完成页默认勾选「立即运行」，但静默安装时不自动启动
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchApp}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Registry]
#ifdef FeatureAutostart
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#RunValue}"; ValueData: """{app}\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: autostart
#endif
#ifdef FeatureFileAssoc
; 只登记「打开方式」条目（Applications\<exe>），不修改任何扩展名的默认关联
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExe}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#AppName}"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExe}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExe}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: fileassoc
#endif

[Code]
var
  LegacyFound: Boolean;

{ 取自定义消息文本：Inno 只在自己显示消息时才把 %n 换成换行，
  从代码里取出来必须手动换，否则用户会看到字面的 "%n%n"。 }
function Cm(const MsgName: String): String;
var
  S: String;
  I: Integer;
begin
  S := ExpandConstant('{cm:' + MsgName + '}');
  Result := '';
  I := 1;
  while I <= Length(S) do
  begin
    if (S[I] = '%') and (I < Length(S)) and (S[I + 1] = 'n') then
    begin
      Result := Result + #13#10;
      I := I + 2;
    end
    else
    begin
      Result := Result + S[I];
      I := I + 1;
    end;
  end;
end;

{ 判断目录是否为本程序的既有安装目录（用于「覆盖确认」时豁免） }
function LooksLikeOurInstall(const Dir: String): Boolean;
begin
  Result := FileExists(AddBackslash(Dir) + '{#AppExe}');
end;

{ 目录里是否有任何条目（忽略 . 与 ..）— 用于覆盖前提醒 }
function DirHasEntries(const Dir: String): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if not DirExists(Dir) then
    Exit;
  if FindFirst(AddBackslash(Dir) + '*', FindRec) then
  try
    repeat
      if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
      begin
        Result := True;
        Break;
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

{ 目录页：先验证可创建、再提醒覆盖、最后才放行 —— 失败给原因而不是装到一半报错 }
function NextButtonClick(CurPageID: Integer): Boolean;
var
  Target: String;
begin
  Result := True;
  if CurPageID <> wpSelectDir then
    Exit;

  Target := Trim(WizardDirValue);
  if Target = '' then
  begin
    MsgBox(Cm('DirEmpty'), mbError, MB_OK);
    Result := False;
    Exit;
  end;

  if DirExists(Target) then
  begin
    if DirHasEntries(Target) and not LooksLikeOurInstall(Target) then
      if MsgBox(FmtMessage(Cm('DirNotEmpty'), [Target]),
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) <> IDYES then
      begin
        Result := False;
        Exit;
      end;
    Exit;
  end;

  if not ForceDirectories(Target) then
  begin
    MsgBox(FmtMessage(Cm('DirCannotCreate'), [Target]), mbError, MB_OK);
    Result := False;
  end;
end;

{ 安装结束后清理旧版自绘安装器留下的痕迹（升级路径） }
procedure CleanupLegacyInstall;
begin
#ifdef LegacyUninstallKey
  if RegKeyExists(HKCU, '{#LegacyUninstallKey}') then
  begin
    Log('removing legacy uninstall key: {#LegacyUninstallKey}');
    RegDeleteKeyIncludingSubkeys(HKCU, '{#LegacyUninstallKey}');
  end;
#endif
#ifdef LegacyRunValue
  if RegValueExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#LegacyRunValue}') then
  begin
    Log('removing legacy autostart value: {#LegacyRunValue}');
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#LegacyRunValue}');
  end;
#endif
  if FileExists(ExpandConstant('{app}\uninstall.exe')) then
  begin
    Log('removing legacy uninstaller');
    DeleteFile(ExpandConstant('{app}\uninstall.exe'));
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    CleanupLegacyInstall;
end;

procedure InitializeWizard;
begin
  LegacyFound := False;
#ifdef LegacyUninstallKey
  LegacyFound := RegKeyExists(HKCU, '{#LegacyUninstallKey}');
#endif
  if LegacyFound then
    Log('legacy installation detected; will clean up after install');
end;

{ 卸载时保留用户配置与数据：只删除安装目录内的程序文件，Inno 默认已处理 }
function InitializeUninstall(): Boolean;
begin
  Result := True;
end;
