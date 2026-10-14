; HotelPOS terminal installer (Inno Setup 6). Built by build\publish.ps1:
;   ISCC /DAppVersion=0.1.0 /DPublishDir=<artifacts\publish\0.1.0> client.iss
; Optional command-line parameters for unattended roll-out to many terminals:
;   /API=http://192.168.1.100:5000   pre-fills the server address (first-run then asks only the terminal name)
;   /CERT=\\server\share\hotelpos.cer trusts the server's self-signed HTTPS certificate
;   /VERYSILENT /SUPPRESSMSGBOXES     no wizard

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\" + AppVersion
#endif

[Setup]
AppId={{2E8F0A4D-7C1B-4F7E-8B3A-9D5C6E1F2A34}
AppName=HotelPOS
AppVersion={#AppVersion}
AppPublisher=HotelPOS
DefaultDirName={autopf}\HotelPOS\Client
DefaultGroupName=HotelPOS
DisableProgramGroupPage=yes
OutputBaseFilename=HotelPOS-Client-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
UninstallDisplayName=HotelPOS

[Dirs]
; Terminal settings are shared by every Windows user of the PC (waiters may log on as different users).
Name: "{commonappdata}\HotelPOS\Desktop"; Permissions: users-modify

[Files]
Source: "{#PublishDir}\client\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\HotelPOS"; Filename: "{app}\HotelPOS.Desktop.exe"
Name: "{autodesktop}\HotelPOS"; Filename: "{app}\HotelPOS.Desktop.exe"

[Run]
Filename: "{app}\HotelPOS.Desktop.exe"; Description: "Start HotelPOS"; Flags: nowait postinstall skipifsilent

[Code]
function JsonEscape(Value: String): String;
begin
  Result := Value;
  StringChangeEx(Result, '\', '\\', True);
  StringChangeEx(Result, '"', '\"', True);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Api, Cert, SettingsFile: String;
  ResultCode: Integer;
begin
  if CurStep <> ssPostInstall then
    Exit;

  // An existing terminal keeps its configuration (name, printers); only a new one gets the server address.
  Api := ExpandConstant('{param:API|}');
  SettingsFile := ExpandConstant('{commonappdata}\HotelPOS\Desktop\settings.json');
  if (Api <> '') and not FileExists(SettingsFile) then
    SaveStringToFile(SettingsFile, '{"apiBaseUrl":"' + JsonEscape(Api) + '"}', False);

  Cert := ExpandConstant('{param:CERT|}');
  if Cert <> '' then
    if not Exec(ExpandConstant('{sys}\certutil.exe'), '-addstore -f Root "' + Cert + '"', '', SW_HIDE,
        ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
      MsgBox('The server certificate could not be trusted (certutil code ' + IntToStr(ResultCode) + ').', mbError, MB_OK);
end;
