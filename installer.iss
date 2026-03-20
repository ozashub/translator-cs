[Setup]
AppName=Translator
AppVersion=1.0.2
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

[Files]
Source: "publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{autodesktop}\Translator"; Filename: "{app}\Translator.exe"; Tasks: desktopicon
Name: "{group}\Translator"; Filename: "{app}\Translator.exe"
Name: "{group}\Uninstall Translator"; Filename: "{uninstallexe}"

[Tasks]
Name: "desktopicon"; Description: "Create desktop shortcut"; GroupDescription: "Shortcuts:"

[Run]
Filename: "{app}\Translator.exe"; Description: "Launch Translator"; Flags: postinstall nowait skipifsilent
