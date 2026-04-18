[Setup]
AppId={{B8F3A2E1-4C5D-6E7F-8A9B-0C1D2E3F4A5B}
AppName=Claude Usage Tracker
AppVersion=1.2.1
AppVerName=Claude Usage Tracker 1.2.1
AppPublisher=zitify
AppPublisherURL=https://zitify.co.kr
DefaultDirName={autopf}\ClaudeUsageTracker
DefaultGroupName=Claude Usage Tracker
OutputDir=installer_output
OutputBaseFilename=ClaudeUsageTracker_Setup_v1.2.1
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName=Claude Usage Tracker
SetupLogging=yes

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "taskbarpin"; Description: "작업 표시줄에 고정"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon"; Description: "Windows 시작 시 자동 실행"; GroupDescription: "추가 옵션:"

[Files]
Source: "publish\ClaudeUsageTracker.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Claude Usage Tracker"; Filename: "{app}\ClaudeUsageTracker.exe"
Name: "{group}\Uninstall Claude Usage Tracker"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Claude Usage Tracker"; Filename: "{app}\ClaudeUsageTracker.exe"; Tasks: desktopicon
Name: "{userstartup}\Claude Usage Tracker"; Filename: "{app}\ClaudeUsageTracker.exe"; Tasks: startupicon

[Run]
Filename: "{app}\ClaudeUsageTracker.exe"; Description: "Claude Usage Tracker 실행"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: dirifempty; Name: "{userappdata}\ClaudeUsageTracker"

[Code]
procedure PinToTaskbar;
var
  ResultCode: Integer;
  PsCmd: string;
begin
  PsCmd := 'powershell -ExecutionPolicy Bypass -Command "' +
    '$shell = New-Object -ComObject Shell.Application; ' +
    '$folder = $shell.Namespace(''' + ExpandConstant('{app}') + '''); ' +
    '$item = $folder.ParseName(''ClaudeUsageTracker.exe''); ' +
    '$verb = $item.Verbs() | Where-Object { $_.Name -match ''작업 표시줄에 고정|Pin to Tas'' }; ' +
    'if ($verb) { $verb.DoIt() }"';
  Exec('cmd.exe', '/c ' + PsCmd, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('taskbarpin') then
    PinToTaskbar;
end;
