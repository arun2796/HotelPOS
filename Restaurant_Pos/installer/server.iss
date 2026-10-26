; HotelPOS server installer (Inno Setup 6). Built by build\publish.ps1:
;   ISCC /DAppVersion=0.1.0 /DPublishDir=<artifacts\publish\0.1.0> server.iss
; The wizard collects the database connection, the first admin password and the port, then hands them to
; scripts\configure-server.ps1 (settings, service, firewall, start), which can also be re-run by hand.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\" + AppVersion
#endif

[Setup]
AppId={{6B0E2B7C-3F5D-4C55-9C2E-5D1B2B6A9F10}
AppName=HotelPOS Server
AppVersion={#AppVersion}
AppPublisher=HotelPOS
DefaultDirName={autopf}\HotelPOS\Server
DefaultGroupName=HotelPOS
DisableProgramGroupPage=yes
OutputBaseFilename=HotelPOS-Server-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
UninstallDisplayName=HotelPOS Server

[Files]
Source: "{#PublishDir}\server\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
Source: "{#PublishDir}\version.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\HotelPOS server health"; Filename: "http://localhost:{code:GetPort}/health"
Name: "{group}\HotelPOS server logs"; Filename: "{commonappdata}\HotelPOS\Api\logs"
Name: "{group}\HotelPOS backups"; Filename: "{commonappdata}\HotelPOS\Backups"

[UninstallRun]
; The database, backups and logs stay: uninstalling the program must never delete the restaurant's data.
Filename: "{sys}\sc.exe"; Parameters: "stop HotelPOSApi"; Flags: runhidden; RunOnceId: "StopService"
Filename: "{sys}\sc.exe"; Parameters: "delete HotelPOSApi"; Flags: runhidden; RunOnceId: "DeleteService"
; The bundled PostgreSQL service (Phase 26); its data in C:\ProgramData\HotelPOS\pg stays as well.
Filename: "{sys}\sc.exe"; Parameters: "stop HotelPOSDb"; Flags: runhidden; RunOnceId: "StopDbService"
Filename: "{sys}\sc.exe"; Parameters: "delete HotelPOSDb"; Flags: runhidden; RunOnceId: "DeleteDbService"

[Code]
var
  PostgresPage: TInputOptionWizardPage;
  DatabasePage: TInputQueryWizardPage;
  SuperuserPage: TInputQueryWizardPage;
  AdminPage: TInputQueryWizardPage;
  CertificatePage: TInputQueryWizardPage;

function JsonEscape(Value: String): String;
begin
  Result := Value;
  StringChangeEx(Result, '\', '\\', True);
  StringChangeEx(Result, '"', '\"', True);
end;

function GetPort(Param: String): String;
begin
  Result := Trim(DatabasePage.Values[5]);
  if Result = '' then
    Result := '5000';
end;

// Phase 26: the package carries PostgreSQL (pgsql\). The default is to use it; an existing server stays possible.
function UsesBundledPostgres: Boolean;
begin
  Result := PostgresPage.SelectedValueIndex = 0;
end;

procedure InitializeWizard;
begin
  PostgresPage := CreateInputOptionPage(wpSelectDir, 'PostgreSQL',
    'The database server that keeps the restaurant''s data',
    'HotelPOS comes with PostgreSQL. On a new server keep the first option: Setup creates the database cluster in ' +
    'C:\ProgramData\HotelPOS\pg and its Windows service "HotelPOSDb"; nothing else has to be installed. ' +
    'Choose the second option only when a PostgreSQL server (14 or later) already runs for HotelPOS on this or another PC.',
    True, False);
  PostgresPage.Add('Install the bundled PostgreSQL on this PC (recommended)');
  PostgresPage.Add('Use an existing PostgreSQL server');
  PostgresPage.SelectedValueIndex := 0;
  // An upgrade keeps what the installation uses today ({app} is not expandable yet here: the directory edit's value is).
  if FileExists(AddBackslash(WizardDirValue) + 'appsettings.Production.json') and
     not FileExists(ExpandConstant('{commonappdata}\HotelPOS\pg\data\PG_VERSION')) then
    PostgresPage.SelectedValueIndex := 1;

  DatabasePage := CreateInputQueryPage(PostgresPage.ID, 'Database',
    'The HotelPOS role and database',
    'HotelPOS uses its own role and database (not postgres). With the bundled PostgreSQL keep Server and Port as they are. ' +
    'On a new installation choose the password for this user here; Setup creates the role and the database.');
  DatabasePage.Add('Server:', False);
  DatabasePage.Add('Port:', False);
  DatabasePage.Add('Database:', False);
  DatabasePage.Add('User:', False);
  DatabasePage.Add('Password for this user (choose one on a new installation):', True);
  DatabasePage.Add('HotelPOS API port:', False);
  DatabasePage.Values[0] := 'localhost';
  DatabasePage.Values[1] := '5432';
  DatabasePage.Values[2] := 'hotelpos';
  DatabasePage.Values[3] := 'hotelpos';
  DatabasePage.Values[5] := '5000';

  // Its own page: a TInputQueryWizardPage shows six fields at most, and this one must not be the one that is cut off.
  SuperuserPage := CreateInputQueryPage(DatabasePage.ID, 'Create the database',
    'Let Setup create the HotelPOS role and database',
    'Enter the password of the PostgreSQL "postgres" account (chosen when PostgreSQL was installed). Setup then creates the role and database from the previous page. ' +
    'Leave it empty only if they already exist (upgrade, or prepared by a technician).');
  SuperuserPage.Add('postgres password:', True);

  AdminPage := CreateInputQueryPage(SuperuserPage.ID, 'Administrator',
    'First administrator password',
    'Used only when the admin account is created on a new database; it must be changed at the first sign-in. Leave empty when upgrading.');
  AdminPage.Add('Password:', True);
  AdminPage.Add('Repeat password:', True);

  CertificatePage := CreateInputQueryPage(AdminPage.ID, 'HTTPS certificate',
    'Certificate for secure access (port 5001)',
    'Phones and tablets need HTTPS. Leave both fields empty to create a LAN certificate (the root CA file for the devices is written to C:\ProgramData\HotelPOS\Api). Or give the path of your own .pfx file and its password. When upgrading, empty keeps the current certificate.');
  CertificatePage.Add('Certificate file (.pfx), optional:', False);
  CertificatePage.Add('Certificate password:', True);
end;

// With the bundled PostgreSQL the superuser password is generated by Setup: the page is not needed.
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = SuperuserPage.ID) and UsesBundledPostgres;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = DatabasePage.ID) and (Trim(DatabasePage.Values[4]) = '') and
     not FileExists(ExpandConstant('{app}\appsettings.Production.json')) then
  begin
    MsgBox('Enter the database password (needed for a new installation).', mbError, MB_OK);
    Result := False;
  end;
  if (CurPageID = CertificatePage.ID) and (Trim(CertificatePage.Values[0]) <> '') and
     (not FileExists(Trim(CertificatePage.Values[0])) or (CertificatePage.Values[1] = '')) then
  begin
    MsgBox('The certificate file must exist and needs its password. Leave both empty to create a LAN certificate.', mbError, MB_OK);
    Result := False;
  end;
  if (CurPageID = AdminPage.ID) and (AdminPage.Values[0] <> AdminPage.Values[1]) then
  begin
    MsgBox('The two administrator passwords are different.', mbError, MB_OK);
    Result := False;
  end;
  // A new installation needs the first administrator password: the server refuses to create the account without one.
  if (CurPageID = AdminPage.ID) and (Trim(AdminPage.Values[0]) = '') and
     not FileExists(ExpandConstant('{app}\appsettings.Production.json')) then
  begin
    MsgBox('Enter the first administrator password (needed for a new installation). It must be changed at the first sign-in.', mbError, MB_OK);
    Result := False;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Handover, Json, LogFile, Reason: String;
  ResultCode, I: Integer;
  Lines: TArrayOfString;
begin
  if CurStep <> ssPostInstall then
    Exit;

  // Handed to the script in a temporary file (deleted by the script) so passwords never appear on a command line.
  Handover := ExpandConstant('{tmp}\hotelpos-install.json');
  Json := '{' +
    '"DbHost":"' + JsonEscape(DatabasePage.Values[0]) + '",' +
    '"DbPort":"' + JsonEscape(DatabasePage.Values[1]) + '",' +
    '"Database":"' + JsonEscape(DatabasePage.Values[2]) + '",' +
    '"DbUser":"' + JsonEscape(DatabasePage.Values[3]) + '",' +
    '"DbPassword":"' + JsonEscape(DatabasePage.Values[4]) + '",' +
    '"SuperuserPassword":"' + JsonEscape(SuperuserPage.Values[0]) + '",' +
    '"AdminPassword":"' + JsonEscape(AdminPage.Values[0]) + '",' +
    '"Port":"' + JsonEscape(GetPort('')) + '",' +
    '"CertificatePath":"' + JsonEscape(Trim(CertificatePage.Values[0])) + '",' +
    '"CertificatePassword":"' + JsonEscape(CertificatePage.Values[1]) + '",' +
    '"BundledPostgres":"' + IntToStr(1 - PostgresPage.SelectedValueIndex) + '"}';
  SaveStringToFile(Handover, Json, False);

  WizardForm.StatusLabel.Caption := 'Configuring the HotelPOS service...';
  // The script's output goes to configure-server.log next to the program, so the reason for a failure is kept and
  // its last "ERROR:" line can be shown here (the script prints exactly one such line when it stops).
  LogFile := ExpandConstant('{app}\configure-server.log');
  if not Exec(ExpandConstant('{cmd}'),
      '/C ""powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\scripts\configure-server.ps1') +
      '" -InstallDir "' + ExpandConstant('{app}') + '" -SettingsFile "' + Handover + '" > "' + LogFile + '" 2>&1"',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
  begin
    DeleteFile(Handover);
    Reason := '';
    if LoadStringsFromFile(LogFile, Lines) then
      for I := GetArrayLength(Lines) - 1 downto 0 do
        if (Reason = '') and (Pos('ERROR: ', Lines[I]) = 1) then
          Reason := Copy(Lines[I], 8, Length(Lines[I]));
    if Reason = '' then
      Reason := 'See ' + LogFile + ' for the output.';
    MsgBox('The service could not be configured (code ' + IntToStr(ResultCode) + ').' + #13#10#13#10 +
      Reason + #13#10#13#10 +
      'Fix the cause (docs\ops\05-troubleshooting.md), then run scripts\configure-server.ps1 in an elevated PowerShell ' +
      'with -DbPassword and -AdminPassword; it ends with "Server is up: Healthy".', mbError, MB_OK);
  end;
end;
