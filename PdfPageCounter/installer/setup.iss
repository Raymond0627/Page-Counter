; ---------------------------------------------------------------------------
;  Lumeed Pcount - Inno Setup script
;
;  Build with:  installer\build-installer.ps1   (preferred, handles publish + staging)
;  Or directly: "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\setup.iss
;
;  Expected layout (produced by build-installer.ps1):
;    installer\staging\*  -> self-contained .NET 8 publish output
;    installer\redist\*   -> Windows App Runtime redistributable MSIX
; ---------------------------------------------------------------------------

#define MyAppName        "Lumeed Pcount"
#define MyAppExeName     "LumeedPcount.exe"
#define MyAppPublisher   "Lumeed"
#define MyAppId          "{{04E7617D-06D6-4F5F-A29B-B66DDEF461CE}"

; Overridable at compile time, e.g. ISCC.exe /DMyAppVersion=1.2.0 setup.iss
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

; Windows App Runtime version the app's bootstrap requires. Bump this whenever
; the Microsoft.WindowsAppSDK package version is bumped.
#define RuntimeMinVersion "2.4.0.0"
#define RuntimeMsixName    "Microsoft.WindowsAppRuntime.2.msix"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\PdfPageCounter.App\Assets\AppIcon.ico
OutputDir=output
OutputBaseFilename=Lumeed-Pcount-Setup-{#MyAppVersion}-x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
Uninstallable=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; The Windows App Runtime MSIX is deliberately listed FIRST. With
; SolidCompression enabled, Inno must decompress every file listed above the
; requested one to reach it, so this keeps ExtractTemporaryFile cheap.
Source: "redist\{#RuntimeMsixName}"; DestDir: "{tmp}"; Flags: ignoreversion

; Self-contained .NET 8 + Windows App SDK projection assemblies.
Source: "staging\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Code]
const
  RuntimeMinVersion = '{#RuntimeMinVersion}';
  RuntimeMsixName   = '{#RuntimeMsixName}';

  // Helper scripts receive the result path as $args[0] (and, for the install
  // body, the MSIX path as $args[1]). Deriving the path from $PSCommandPath
  // instead would silently produce no result file, which reads as "runtime
  // missing" -- so it is passed explicitly.

  // Fails unless some installed Microsoft.WindowsAppRuntime.2 satisfies the
  // minimum. Get-AppxPackage returns packages oldest-first, so the result has
  // to be filtered rather than taken from the first row.
  RuntimeCheckBody =
    '$p = @(Get-AppxPackage -Name Microsoft.WindowsAppRuntime.2 | ' +
    'Where-Object { [version](($_.Version -split ''-'')[0]) -ge [version]''' + RuntimeMinVersion + ''' });' +
    'if ($p.Count -eq 0) {' +
    '  throw ''Windows App Runtime ' + RuntimeMinVersion + ' or newer is not installed.''' +
    '}' + #13#10;

  RuntimeInstallBody =
    'Add-AppxPackage -Path $args[1] -ForceApplicationShutdown -ForceUpdateFromAnyVersion' + #13#10;

  // Each body ends with a CRLF so ScriptTail starts on its own line. Without
  // that break the tail's success marker would be appended to the body's last
  // command, turning it into a positional argument and failing the script.
  ScriptHead =
    '$ErrorActionPreference = ''Stop'';' + #13#10 +
    '$Out = [string]$args[0];' + #13#10 +
    'try {' + #13#10;

  ScriptTail =
    '  [System.IO.File]::WriteAllText($Out, ''OK'', [System.Text.Encoding]::ASCII)' + #13#10 +
    '  exit 0' + #13#10 +
    '} catch {' + #13#10 +
    '  [System.IO.File]::WriteAllText($Out, [string]$_.Exception.Message, [System.Text.Encoding]::ASCII)' + #13#10 +
    '  exit 1' + #13#10 +
    '}';

function PowerShellExe: String;
begin
  Result := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
end;

function ScriptOutPath: String;
begin
  Result := ExpandConstant('{tmp}\LumeedPcount-Setup.out');
end;

// Runs one helper script. Returns its exit code, or -1 if it never started.
// Output receives the script's ASCII verdict file ("OK" or an error message).
function RunHelper(const Body: String; const Args: String; out Output: String): Integer;
var
  ScriptPath, Text: String;
  Raw: AnsiString;
  ResultCode: Integer;
begin
  Output := '';
  Result := -1;
  ScriptPath := ExpandConstant('{tmp}\LumeedPcount-Setup.ps1');

  DeleteFile(ScriptOutPath);
  if not SaveStringToFile(ScriptHead + Body + ScriptTail, ScriptPath, False) then
  begin
    Log('Setup helper: could not write ' + ScriptPath);
    Exit;
  end;

  if not Exec(PowerShellExe,
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ScriptPath + '" ' + Args,
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Log('Setup helper: failed to launch PowerShell');
    Exit;
  end;

  Result := ResultCode;
  Raw := '';
  if FileExists(ScriptOutPath) then
    LoadStringFromFile(ScriptOutPath, Raw);
  Text := Raw;
  Output := Text;
  Log('Setup helper: exit=' + IntToStr(ResultCode) + ' output="' + Text + '"');
end;

// 1 = runtime present, 0 = runtime definitely missing, -1 = could not tell.
function ProbeRuntime(out Detail: String): Integer;
var
  Output: String;
  Code: Integer;
begin
  Detail := '';
  Code := RunHelper(RuntimeCheckBody, '"' + ScriptOutPath + '"', Output);

  if Code < 0 then
  begin
    Detail := 'the Windows App Runtime check did not start.';
    Result := -1;
    Exit;
  end;

  // The script ran but produced nothing: treat as unknown, never as missing.
  if Output = '' then
  begin
    Detail := 'the Windows App Runtime check reported no result (PowerShell exit ' +
              IntToStr(Code) + ').';
    Result := -1;
    Exit;
  end;

  if (Code = 0) and (Pos('OK', Uppercase(Output)) > 0) then
    Result := 1
  else
    Result := 0;

  Detail := Output;
end;

function InstallRuntime(out Detail: String): Boolean;
var
  Output, MsixPath: String;
  Code: Integer;
begin
  Detail := '';
  MsixPath := ExpandConstant('{tmp}\') + RuntimeMsixName;

  if not FileExists(MsixPath) then
    ExtractTemporaryFile(RuntimeMsixName);

  if not FileExists(MsixPath) then
  begin
    Detail := 'the bundled Windows App Runtime package could not be extracted.';
    Result := False;
    Exit;
  end;

  Code := RunHelper(RuntimeInstallBody, '"' + ScriptOutPath + '" "' + MsixPath + '"', Output);
  Result := (Code = 0) and (Pos('OK', Uppercase(Output)) > 0);

  if Result then
    Exit;

  if Output = '' then
    Detail := 'Add-AppxPackage did not run (PowerShell exit ' + IntToStr(Code) + ').'
  else
    Detail := Output;
end;

// Runs after the wizard opens and before any files are copied, so a failed
// prerequisite aborts with Inno's rollback instead of leaving a broken app.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Detail: String;
  State: Integer;
begin
  Result := '';
  NeedsRestart := False;

  State := ProbeRuntime(Detail);
  Log('Setup: Windows App Runtime probe state=' + IntToStr(State) + ' detail="' + Detail + '"');
  if State = 1 then
    Exit;

  if InstallRuntime(Detail) then
  begin
    State := ProbeRuntime(Detail);
    Log('Setup: re-probe after install state=' + IntToStr(State));
    if State <> 0 then
      Exit;
    Result := 'The Windows App Runtime ' + RuntimeMinVersion + ' is required but could ' +
              'not be installed.' + #13#10#13#10 + Detail;
    Exit;
  end;

  if State = -1 then
  begin
    // Nothing was detected at all, so do not block the install: the app's own
    // Windows App SDK bootstrap attempts the runtime install on first launch.
    Log('Setup: proceeding despite unverified Windows App Runtime. ' + Detail);
    Exit;
  end;

  Result := 'The Windows App Runtime ' + RuntimeMinVersion + ' is required but could ' +
            'not be installed.' + #13#10#13#10 + Detail;
end;

// Stops a running instance so an upgrade or uninstall can replace the files.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#MyAppExeName}', '',
      SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
