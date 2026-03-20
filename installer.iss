[Setup]
AppId={{7DC1B696-04BA-4818-924B-8CDE5B864B77}
AppName=Translator
AppVersion=1.0.5
AppPublisher=ozas
AppPublisherURL=https://github.com/ozashub/translator-cs
DefaultDirName={commonpf}\Translator
DefaultGroupName=Translator
OutputDir=publish
OutputBaseFilename=TranslatorSetup-x64
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\Translator.exe
PrivilegesRequired=admin
WizardStyle=modern
CloseApplications=force
RestartApplications=no

[Files]
Source: "publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{autodesktop}\Translator"; Filename: "{app}\Translator.exe"; Tasks: desktopicon
Name: "{group}\Translator"; Filename: "{app}\Translator.exe"
Name: "{group}\Uninstall Translator"; Filename: "{uninstallexe}"

[Tasks]
Name: "desktopicon"; Description: "Create desktop shortcut"; GroupDescription: "Shortcuts:"

[InstallDelete]
Type: filesandordirs; Name: "{app}\*"

[Run]
Filename: "{app}\Translator.exe"; Description: "Launch Translator"; Flags: postinstall nowait skipifsilent

[Code]
procedure TaskKill(name: String);
var
  ResultCode: Integer;
begin
  Exec('taskkill', '/F /IM ' + name, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function InitializeSetup(): Boolean;
begin
  TaskKill('Translator.exe');
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    TaskKill('Translator.exe');
end;
