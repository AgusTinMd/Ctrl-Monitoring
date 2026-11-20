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
; "auto" solo muestra el selector cuando Windows no coincide con ningún
; idioma declarado; con "yes" siempre se puede elegir español, inglés o
; portugués, aunque el sistema esté en otro idioma.
ShowLanguageDialog=yes
; La app vive en la bandeja y cancela el cierre normal; se cierra en
; PrepareToInstall en lugar de usar el Restart Manager.
CloseApplications=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[CustomMessages]
spanish.PawnIoTask=Instalar PawnIO 2.2.0 (recomendado)
english.PawnIoTask=Install PawnIO 2.2.0 (recommended)
brazilianportuguese.PawnIoTask=Instalar o PawnIO 2.2.0 (recomendado)
spanish.PawnIoTaskGroup=Lectura de la placa madre
english.PawnIoTaskGroup=Motherboard readings
brazilianportuguese.PawnIoTaskGroup=Leitura da placa-mãe
spanish.PawnIoDownloadTitle=Descargando PawnIO
english.PawnIoDownloadTitle=Downloading PawnIO
brazilianportuguese.PawnIoDownloadTitle=Baixando o PawnIO
spanish.PawnIoDownloadDesc=Ctrl Monitor usa PawnIO para leer los sensores de la placa madre. Se descarga desde el sitio oficial y se verifica su SHA-256.
english.PawnIoDownloadDesc=Ctrl Monitor uses PawnIO to read the motherboard sensors. It is downloaded from the official site and its SHA-256 is verified.
brazilianportuguese.PawnIoDownloadDesc=O Ctrl Monitor usa o PawnIO para ler os sensores da placa-mãe. Ele é baixado do site oficial e o SHA-256 é verificado.
spanish.PawnIoDownloadFailed=No se pudo descargar PawnIO:%n%n%1%n%nLa instalación va a continuar. Sin PawnIO, Ctrl Monitor funciona pero muestra menos sensores; podés instalarlo después desde https://pawnio.eu/.
english.PawnIoDownloadFailed=PawnIO could not be downloaded:%n%n%1%n%nSetup will continue. Without PawnIO, Ctrl Monitor still works but shows fewer sensors; you can install it later from https://pawnio.eu/.
brazilianportuguese.PawnIoDownloadFailed=Não foi possível baixar o PawnIO:%n%n%1%n%nA instalação vai continuar. Sem o PawnIO, o Ctrl Monitor funciona, mas mostra menos sensores; você pode instalá-lo depois em https://pawnio.eu/.
spanish.PawnIoInstalling=Instalando PawnIO…
english.PawnIoInstalling=Installing PawnIO…
brazilianportuguese.PawnIoInstalling=Instalando o PawnIO…
spanish.PawnIoInstallFailed=PawnIO no se pudo instalar (código %1).%n%nCtrl Monitor va a funcionar igual, pero con menos sensores. Podés instalarlo a mano desde https://pawnio.eu/.
english.PawnIoInstallFailed=PawnIO could not be installed (code %1).%n%nCtrl Monitor will still work, but with fewer sensors. You can install it manually from https://pawnio.eu/.
brazilianportuguese.PawnIoInstallFailed=Não foi possível instalar o PawnIO (código %1).%n%nO Ctrl Monitor vai funcionar mesmo assim, mas com menos sensores. Você pode instalá-lo manualmente em https://pawnio.eu/.
spanish.OpenPawnIo=Abrir la página de descarga de PawnIO
english.OpenPawnIo=Open the PawnIO download page
brazilianportuguese.OpenPawnIo=Abrir a página de download do PawnIO

[Tasks]
; Se oculta si PawnIO ya está instalado. Queda marcada por defecto.
Name: "pawnio"; Description: "{cm:PawnIoTask}"; GroupDescription: "{cm:PawnIoTaskGroup}"; Check: not IsPawnIoInstalled
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
const
  PawnIoUrl = 'https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe';
  PawnIoSha256 = '1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032';

var
  DownloadPage: TDownloadWizardPage;

function IsPawnIoInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\PawnIO')
    or RegKeyExists(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO')
    or RegKeyExists(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO');
end;

function WantsPawnIo: Boolean;
begin
  Result := (not IsPawnIoInstalled) and WizardIsTaskSelected('pawnio');
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(
    CustomMessage('PawnIoDownloadTitle'),
    CustomMessage('PawnIoDownloadDesc'),
    nil);
  DownloadPage.ShowBaseNameInsteadOfUrl := True;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if (CurPageID = wpReady) and WantsPawnIo then
  begin
    DownloadPage.Clear;
    DownloadPage.Add(PawnIoUrl, 'PawnIO_setup.exe', PawnIoSha256);
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
      except
        // Sin PawnIO la app funciona: se avisa y se sigue instalando.
        if not DownloadPage.AbortedByUser then
          SuppressibleMsgBox(
            FmtMessage(CustomMessage('PawnIoDownloadFailed'), [GetExceptionMessage]),
            mbError, MB_OK, IDOK);
        Log('PawnIO no se descargó: ' + GetExceptionMessage);
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;

// Ejecuta el instalador oficial de PawnIO, ya descargado en {tmp}.
procedure RunPawnIoSetup;
var
  Installer: String;
  ResultCode: Integer;
begin
  Installer := ExpandConstant('{tmp}\PawnIO_setup.exe');

  if not FileExists(Installer) then
    Exit;

  WizardForm.StatusLabel.Caption := CustomMessage('PawnIoInstalling');

  if not Exec(Installer, '-install -silent', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    // Exec deja en ResultCode el error de Windows; se muestra igual que el código de salida.
    SuppressibleMsgBox(
      FmtMessage(CustomMessage('PawnIoInstallFailed'), [IntToStr(ResultCode)]),
      mbError, MB_OK, IDOK);
    Exit;
  end;

  // 3010 y 1641 significan «instalado, hace falta reiniciar».
  if (ResultCode = 0) or (ResultCode = 3010) or (ResultCode = 1641) then
    Log(Format('PawnIO instalado (código %d).', [ResultCode]))
  else
    SuppressibleMsgBox(
      FmtMessage(CustomMessage('PawnIoInstallFailed'), [IntToStr(ResultCode)]),
      mbError, MB_OK, IDOK);
end;

// En modo silencioso no hay página de descarga: se baja sin interfaz.
procedure DownloadPawnIoSilently;
begin
  try
    DownloadTemporaryFile(PawnIoUrl, 'PawnIO_setup.exe', PawnIoSha256, nil);
  except
    Log('PawnIO no se descargó: ' + GetExceptionMessage);
  end;
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

function IsUnderFolder(const Path, Folder: String): Boolean;
begin
  Result := (Folder <> '') and
    (Pos(Uppercase(AddBackslash(Folder)), Uppercase(AddBackslash(Path))) = 1);
end;

// La tarea de «Iniciar con Windows» ejecuta el exe como administrador. Si la
// carpeta elegida no está en Program Files (por ejemplo E:\Ctrl Monitor),
// suele heredar permisos de escritura para cualquier usuario: otro programa
// sin privilegios podría reemplazar el exe y ganar administrador al iniciar
// sesión. Por eso se cortan los permisos heredados y se deja solo lectura y
// ejecución para los usuarios. Se usan SID para que funcione en cualquier idioma.
procedure ProtectInstallFolder;
var
  App: String;
  ResultCode: Integer;
begin
  App := RemoveBackslashUnlessRoot(ExpandConstant('{app}'));

  if IsUnderFolder(App, ExpandConstant('{commonpf64}')) or
     IsUnderFolder(App, ExpandConstant('{commonpf32}')) then
    Exit;

  // Administradores y SYSTEM: control total; Usuarios: lectura y ejecución.
  if not Exec(ExpandConstant('{sys}\icacls.exe'),
    '"' + App + '" /inheritance:r /grant:r *S-1-5-32-544:(OI)(CI)F *S-1-5-18:(OI)(CI)F *S-1-5-32-545:(OI)(CI)RX /C /Q',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
    Log(Format('icacls sobre la carpeta devolvió %d', [ResultCode]));

  // Los archivos ya copiados vuelven a heredar de la carpeta protegida.
  if not Exec(ExpandConstant('{sys}\icacls.exe'),
    '"' + App + '\*" /reset /T /C /Q',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
    Log(Format('icacls sobre los archivos devolvió %d', [ResultCode]));
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    ProtectInstallFolder;

    if WantsPawnIo then
    begin
      // En modo silencioso no pasó por NextButtonClick: se descarga acá.
      if WizardSilent and (not FileExists(ExpandConstant('{tmp}\PawnIO_setup.exe'))) then
        DownloadPawnIoSilently;

      RunPawnIoSetup;
    end;
  end;
end;
