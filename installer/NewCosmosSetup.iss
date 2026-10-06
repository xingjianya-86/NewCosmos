; 帝皇权杖δ-me13（民政社会救助管理系统）Windows 安装包
; 构建自 Release + ReadyToRun 自包含发布产物（publish\win-x64）
; 安全要点：config\database.ini 绝不随包分发，部署机首次启动由配置向导生成

#define MyAppName "帝皇权杖δ-me13"
#define MyAppVersion "1.1.20261008"
#define MyAppPublisher "NewCosmos"
#define MyAppExeName "NewCosmos.exe"
#define SrcDir "..\publish\win-x64"

[Setup]
AppId={{8B3A0F2E-9C41-4D7E-B5A2-3C1E9D6F7A08}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\NewCosmos
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\publish
OutputBaseFilename=NewCosmosSetup_{#MyAppVersion}
SetupIconFile=appicon.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
; 文件属性版本须为 4 段式且每段 ≤65535，20260915 超限故拆为 2026.915；安装向导显示版本见 MyAppVersion
VersionInfoVersion=1.1.2026.1008
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} 民政社会救助管理系统
CloseApplications=yes

[Languages]
Name: "chinesesimp"; MessagesFile: "Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标："; Flags: unchecked

[Files]
; 全量文件（database.ini 凭据绝不入包；update.ini 保留本机修改；彩票模型单独处理，见下一条；
; 输出\ 为本机运行产生的文书目录，含公民身份信息（PII），绝不入包；Logs/Temp 为运行时残留）
Source: "{#SrcDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "config\database.ini,config\update.ini,Scripts\Lottery\models\*,输出\*,Logs\*,Temp\*,*.log"
; 在线更新配置：仅首次安装写入，升级不覆盖本机修改
Source: "{#SrcDir}\config\update.ini"; DestDir: "{app}\config"; Flags: onlyifdoesntexist
; 彩票预训练模型：新装机随包分发（免重新训练）；升级时 onlyifdoesntexist 保留用户已重训的模型
; （注：Scripts\Lottery 已从仓库移除；skipifsourcedoesntexist 使缺失时编译不中止）
Source: "{#SrcDir}\Scripts\Lottery\models\*"; DestDir: "{app}\Scripts\Lottery\models"; Flags: ignoreversion onlyifdoesntexist skipifsourcedoesntexist

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "立即运行 {#MyAppName}"; Flags: nowait postinstall skipifsilent
; 在线更新静默升级完成后自动重启新版（/UPDATE=1 由客户端 UpdateService 传入）
Filename: "{app}\{#MyAppExeName}"; Flags: nowait runascurrentuser; Check: IsUpdateMode

[Code]
function IsUpdateMode: Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
  begin
    if CompareText(ParamStr(I), '/UPDATE=1') = 0 then
    begin
      Result := True;
      Exit;
    end;
  end;
end;