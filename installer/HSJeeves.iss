; HSJeeves installer (Inno Setup 6.3 or newer). Build with installer\build.ps1, which passes /DAppVersion.
; Per-user install (no admin rights): settings, logs and the autostart entry all belong to the same Windows user.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#define AppName "HSJeeves"
#define AppExe "HSJeeves.exe"
#define BuildDir "..\src\HSJeeves\bin\Release\net472"

[Setup]
; Fixed for the life of the product: a new GUID would install side by side instead of upgrading.
AppId={{114BFFB8-5761-40D1-80AC-0E56359951D8}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=HSJeeves
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=HSJeeves-Setup-{#AppVersion}
SetupIconFile=..\src\HSJeeves\Assets\HSJeeves.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Closes a running HSJeeves (tray app) during an upgrade and offers to restart it.
CloseApplications=yes
RestartApplications=yes

[Tasks]
Name: "startup"; Description: "Start HSJeeves when I sign in to Windows"; GroupDescription: "Options:"

[Files]
Source: "{#BuildDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"

[Registry]
; Removed again on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; \
  ValueData: """{app}\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start HSJeeves now"; Flags: nowait postinstall skipifsilent

[Code]
var
  KeyPage: TInputQueryWizardPage;
  FolderPage: TInputDirWizardPage;
  FoundHsDir: String;

function SettingsPath: String;
begin
  Result := ExpandConstant('{userappdata}\HSJeeves\settings.json');
end;

// Version of the installed copy (empty when not installed). Per-user install: the uninstall entry is under HKCU.
function InstalledVersion: String;
begin
  if not RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{114BFFB8-5761-40D1-80AC-0E56359951D8}_is1',
                             'DisplayVersion', Result) then
    Result := '';
end;

// Returns False when the user declines the same-version / downgrade prompt. An older installed version just updates.
// Silent installs never ask.
function ConfirmVersionChange: Boolean;
var
  Installed: String;
  InstalledPacked, NewPacked: Int64;
begin
  Result := True;
  Installed := InstalledVersion;
  if (Installed = '') or WizardSilent then Exit;
  if not (StrToVersion(Installed, InstalledPacked) and StrToVersion('{#AppVersion}', NewPacked)) then Exit;
  case ComparePackedVersion(InstalledPacked, NewPacked) of
    0: Result := MsgBox('HSJeeves ' + Installed + ' is already installed. Reinstall the same version?',
                        mbConfirmation, MB_YESNO) = IDYES;
    1: Result := MsgBox('A newer version (' + Installed + ') is installed. Install the older version {#AppVersion} instead?' + #13#10 +
                        'Your settings and API key are kept.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
  end;
end;

// .NET Framework 4.7.2 = release 461808. Windows 10/11 normally has 4.8 already.
function InitializeSetup: Boolean;
var
  Release: Cardinal;
begin
  Result := True;
  if not RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release)
     or (Release < 461808) then
  begin
    MsgBox('HSJeeves needs .NET Framework 4.7.2 or newer.' + #13#10 +
           'Install it from https://dotnet.microsoft.com/download/dotnet-framework and run this setup again.',
           mbError, MB_OK);
    Result := False;
    Exit;
  end;
  Result := ConfirmVersionChange;
end;

// A Hearthstone folder is valid when it holds Hearthstone.exe.
function IsHsDir(const Dir: String): Boolean;
begin
  Result := (Dir <> '') and FileExists(AddBackslash(Dir) + 'Hearthstone.exe');
end;

// Battle.net registers Hearthstone's uninstall entry; fall back to the default folders.
function DetectHsDir: String;
var
  S: String;
begin
  Result := '';
  if RegQueryStringValue(HKLM32, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Hearthstone', 'InstallLocation', S)
     and IsHsDir(S) then
    Result := S
  else if IsHsDir('C:\Program Files (x86)\Hearthstone') then
    Result := 'C:\Program Files (x86)\Hearthstone'
  else if IsHsDir('C:\Program Files\Hearthstone') then
    Result := 'C:\Program Files\Hearthstone';
end;

procedure InitializeWizard;
begin
  // Only shown when Hearthstone was not found automatically.
  FoundHsDir := DetectHsDir;
  FolderPage := CreateInputDirPage(wpSelectTasks, 'Hearthstone folder', 'Where is Hearthstone installed?',
    'Setup could not find Hearthstone. Select the folder that contains Hearthstone.exe. ' +
    'HSJeeves uses it to read the game log (concede detection). You can also change it later: tray icon > Settings.', False, '');
  FolderPage.Add('');

  KeyPage := CreateInputQueryPage(wpSelectTasks, 'API key', 'Connect HSJeeves to your account',
    'Paste the key from the /hdttoken command in Discord (the same key the old HDT plugin used works too). ' +
    'You can also enter it later: tray icon > Settings.');
  KeyPage.Add('API key:', False);
end;

// Upgrade: settings.json already exists, so keep the user's settings and key and skip the page.
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if PageID = KeyPage.ID then
    Result := FileExists(SettingsPath);
  // Skip when Hearthstone was found, or on upgrade (the existing setting stays).
  if PageID = FolderPage.ID then
    Result := (FoundHsDir <> '') or FileExists(SettingsPath);
end;

// Warn (not block) when the chosen folder does not hold Hearthstone.exe: the app still works, only concede detection is off.
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = FolderPage.ID) and (not IsHsDir(FolderPage.Values[0])) then
    Result := MsgBox('Hearthstone.exe was not found in this folder. Continue anyway?', mbConfirmation, MB_YESNO) = IDYES;
end;

function JsonEscape(const S: String): String;
begin
  Result := S;
  StringChangeEx(Result, '\', '\\', True);
  StringChangeEx(Result, '"', '\"', True);
end;

// Fresh install only. The app moves the plain key into Windows-protected storage on first start.
procedure CurStepChanged(CurStep: TSetupStep);
var
  Json, Key, StartFlag: String;
begin
  if (CurStep = ssPostInstall) and (not FileExists(SettingsPath)) then
  begin
    Key := Trim(KeyPage.Values[0]);
    if WizardIsTaskSelected('startup') then StartFlag := 'true' else StartFlag := 'false';
    Json := '{' + #13#10 +
            '  "ApiUrl": "https://dee-service.cc/API",' + #13#10 +
            '  "DryRun": true,' + #13#10 +
            '  "StartWithWindows": ' + StartFlag;
    if Key <> '' then
      Json := Json + ',' + #13#10 + '  "ApiKey": "' + JsonEscape(Key) + '"';
    // Only when auto-detection failed and the user picked a folder.
    if (FoundHsDir = '') and (Trim(FolderPage.Values[0]) <> '') then
      Json := Json + ',' + #13#10 + '  "HearthstoneDirectory": "' + JsonEscape(Trim(FolderPage.Values[0])) + '"';
    Json := Json + #13#10 + '}' + #13#10;
    ForceDirectories(ExtractFileDir(SettingsPath));
    SaveStringToFile(SettingsPath, Json, False);
  end;
end;
