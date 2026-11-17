; Instalador de Ctrl Monitor (Inno Setup 6).
;
; Se compila con build\package.ps1, que publica el exe autocontenido y pasa:
;   /DAppVersion=1.2.3  /DSourceDir=<carpeta publicada>  /DOutputDir=<salida>
;
; El archivo está en UTF-8 con BOM para que Inno Setup lea bien los acentos.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

#define AppName "Ctrl Monitor"
#define AppExe "CtrlMonitor.exe"
#define AppUrl "https://github.com/AgusTinMd/Ctrl-Monitoring"

[Setup]
; El AppId identifica la app entre versiones: no cambiarlo nunca.
AppId={{A927A1A8-ED45-44B3-A163-3E52B497BE56}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=AgusTinMd
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=CtrlMonitorSetup-{#AppVersion}
SetupIconFile=..\src\CtrlMonitor.App\Assets\fan.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription=Instalador de {#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; La app vive en la bandeja y cancela el cierre normal; se cierra en
; PrepareToInstall en lugar de usar el Restart Manager.
CloseApplications=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
spanish.PawnIoMissing=No se detectó PawnIO.%n%nCtrl Monitor usa el driver PawnIO para leer la placa madre (ventiladores, voltajes y temperaturas del Super I/O). Sin él la app funciona, pero muestra menos sensores.%n%nAl terminar la instalación podés abrir la página de descarga de PawnIO.
english.PawnIoMissing=PawnIO was not detected.%n%nCtrl Monitor uses the PawnIO driver to read the motherboard (fans, voltages and Super I/O temperatures). Without it the app still works but shows fewer sensors.%n%nWhen setup finishes you can open the PawnIO download page.
spanish.OpenPawnIo=Abrir la página de descarga de PawnIO (recomendado)
english.OpenPawnIo=Open the PawnIO download page (recommended)

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "https://pawnio.eu/"; Description: "{cm:OpenPawnIo}"; Flags: postinstall shellexec skipifsilent; Check: not IsPawnIoInstalled
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: postinstall nowait skipifsilent shellexec

[UninstallRun]
; Cerrar la app y borrar la tarea de inicio que crea la opción «Iniciar con Windows».
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "StopApp"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN CtrlMonitor /F"; Flags: runhidden; RunOnceId: "RemoveStartupTask"

[Code]
function IsPawnIoInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\PawnIO')
    or RegKeyExists(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO');
end;

function InitializeSetup: Boolean;
begin
  Result := True;

  if (not IsPawnIoInstalled) and (not WizardSilent) then
    MsgBox(CustomMessage('PawnIoMissing'), mbInformation, MB_OK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  // Si la app está abierta (incluso oculta en la bandeja) bloquea el exe.
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#AppExe} /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(800);
  Result := '';
end;
