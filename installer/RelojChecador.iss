; Script de Inno Setup para RelojChecador.
;
; REQUIERE Inno Setup 6.x (https://jrsoftware.org/isinfo.php) instalado en Windows.
; No se puede compilar desde macOS/Linux — ISCC.exe es exclusivo de Windows.
;
; Flujo para generar el instalador:
;   1) En Windows, ejecutar installer\publish.ps1 (genera installer\publish\RelojChecador.WPF.exe)
;   2) Compilar este script: ISCC.exe installer\RelojChecador.iss
;   3) El instalador queda en installer\output\RelojChecador-Setup-<version>.exe
;
; Antes de distribuir en producción: reemplazar "MyAppPublisher" por la razón social real
; del carwash (ver decisión pendiente en la memoria del proyecto) y considerar firmar
; digitalmente el .exe/instalador (SignTool) para evitar la advertencia de SmartScreen —
; requiere un certificado de firma de código, que no está incluido aquí.

#define MyAppName "Reloj Checador"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Carwash Mexicali"
#define MyAppExeName "RelojChecador.WPF.exe"
#define MyPublishDir "publish"

[Setup]
AppId={{6E2B6E6E-6E6E-4E7B-9E1E-52454C4F4A43}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Requiere privilegios de administrador porque se instala en Archivos de Programa
; (compartido para todos los usuarios del equipo, típico en un puesto de trabajo fijo
; de sucursal). Alternativa si se prefiere instalación sin admin: cambiar
; PrivilegesRequired a "lowest" y DefaultDirName a "{localappdata}\Programs\{#MyAppName}".
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=output
OutputBaseFilename=RelojChecador-Setup-{#MyAppVersion}
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos adicionales:"; Flags: unchecked

[Files]
; Todo el contenido publicado (el .exe autocontenido + dependencias nativas, si las hay).
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Desinstalar {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Iniciar {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Deliberadamente NO se borra %LocalAppData%\RelojChecador (base SQLite local, logs,
; configuración) al desinstalar — es información del negocio, no basura de la app.
; Si algún día se necesita una desinstalación completa, agregar aquí un paso explícito
; y confirmado por el usuario, nunca por defecto.
