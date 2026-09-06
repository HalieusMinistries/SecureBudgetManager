#define MyAppName "Secure Budget Manager"
#define MyAppVersion "1.0.2"
#define MyAppPublisher "Secure Budget Manager"
#define MyAppExeName "SecureBudgetManager.exe"
#define MyUpgradeCode "8C3F0A61-2E47-4B9A-9D11-6A5F2C8E1B70"

[Setup]
AppId={{8C3F0A61-2E47-4B9A-9D11-6A5F2C8E1B70}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://localhost
DefaultDirName={autopf}\Secure Budget Manager
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=SecureBudgetManager-1.0.2-win-x64
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion=1.0.2.0
SetupIconFile=..\src\SecureBudgetManager.App\Assets\app.ico
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[InstallDelete]
Type: files; Name: "{app}\e_sqlcipher.dll"
Type: files; Name: "{app}\SQLitePCLRaw.provider.e_sqlcipher.dll"

[Files]
Source: "..\src\SecureBudgetManager.App\bin\Release\net8.0-windows\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Secure Budget Manager"; Flags: nowait postinstall skipifsilent

[Code]
var
  RemoveHouseholdData: Boolean;

function InitializeUninstall(): Boolean;
var
  Answer: Integer;
begin
  Result := True;
  RemoveHouseholdData := False;
  if UninstallSilent then
    Exit;

  Answer := MsgBox(
    'Keep the local household database and backups in %LocalAppData%\SecureBudgetManager?'#13#10#13#10 +
    'Yes keeps the data (ordinary uninstall).'#13#10 +
    'No permanently deletes household data. This is optional and is not the default.',
    mbConfirmation,
    MB_YESNO or MB_DEFBUTTON1);
  RemoveHouseholdData := (Answer = IDNO);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if (CurUninstallStep = usPostUninstall) and RemoveHouseholdData then
  begin
    DataDir := ExpandConstant('{localappdata}\SecureBudgetManager');
    if DirExists(DataDir) then
      DelTree(DataDir, True, True, True);
  end;
end;
