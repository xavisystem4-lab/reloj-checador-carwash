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
; Mantener sincronizado a mano con <Version> en Directory.Build.props (Inno Setup no
; puede leer un .props de MSBuild) — el botón "Actualizar versión" de la app compara su
; propio Assembly.GetEntryAssembly().Version contra la última versión en GitHub Releases,
; así que si estos dos números se desincronizan, el auto-actualizador queda mostrando
; una versión incorrecta aunque el instalador esté bien.
#define MyAppVersion "1.11.1"
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
; Sin ArchitecturesAllowed/ArchitecturesInstallIn64BitMode a propósito: la app se publica
; como win-x86 (32 bits) porque zkemkeeper.dll (SDK real de ZKTeco, ver
; third-party/zkteco-sdk/README.md) es un COM server de 32 bits. Un app de 32 bits corre
; bien en Windows de 32 y de 64 bits vía WOW64 — no hace falta restringir arquitectura, y
; {autopf} sin ArchitecturesInstallIn64BitMode ya resuelve a "Archivos de programa (x86)"
; en Windows de 64 bits, que es donde debe vivir un ejecutable de 32 bits.
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
; Todo el contenido publicado (el .exe autocontenido + dependencias nativas, incluido el
; SDK de ZKTeco — ver third-party/zkteco-sdk/README.md). zkemkeeper.dll se copia aquí
; igual que el resto — el registro COM ya NO se hace con "Flags: regserver" (ver
; comentario en [Run] de por qué se cambió) sino con un paso explícito de regsvr32.
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Desinstalar {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Registra zkemkeeper.dll (COM de 32 bits del SDK de ZKTeco, ver
; third-party/zkteco-sdk/README.md) para que ZKTecoDeviceAdapter pueda activarlo por
; Type.GetTypeFromProgID("zkemkeeper.CZKEM") en tiempo de ejecución.
;
; Antes esto se hacía con "Flags: regserver" directo en [Files] — se cambió a este paso
; explícito porque, en la primera instalación real (ver reporte del usuario: "5.
; Autenticación -> No se pudo inicializar el SDK del fabricante... no se encontró el
; ProgID"), el registro claramente NO quedó hecho aunque la instalación terminó sin
; avisar nada. "regserver" puede fallar en silencio; un paso [Run] explícito con
; regsvr32.exe SIN "/s" (sin modo silencioso) muestra el resultado real —éxito o el
; error concreto de Windows— en un cuadro de diálogo durante la instalación, así que la
; próxima vez que falle se va a notar de inmediato en vez de descubrirse hasta abrir la
; pantalla de Dispositivos.
;
; "{sys}" resuelve automáticamente a SysWOW64 (no System32) porque este instalador corre
; como proceso de 32 bits (no se fuerza ArchitecturesInstallIn64BitMode, ver [Setup]) —
; ese detalle importa: regsvr32.exe de 64 bits no puede registrar un DLL de 32 bits.
Filename: "{sys}\regsvr32.exe"; Parameters: """{app}\zkemkeeper.dll"""; StatusMsg: "Registrando el SDK del reloj checador…"; Flags: waituntilterminated

Filename: "{app}\{#MyAppExeName}"; Description: "Iniciar {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Contraparte del registro de arriba — antes la desregistración quedaba a cargo del
; "Flags: regserver" que ya no se usa, así que ahora hace falta este paso explícito para
; no dejar basura en el registro de Windows tras desinstalar.
Filename: "{sys}\regsvr32.exe"; Parameters: "/s /u ""{app}\zkemkeeper.dll"""; Flags: waituntilterminated

[UninstallDelete]
; Deliberadamente NO se borra %LocalAppData%\RelojChecador (base SQLite local, logs,
; configuración) al desinstalar — es información del negocio, no basura de la app.
; Si algún día se necesita una desinstalación completa, agregar aquí un paso explícito
; y confirmado por el usuario, nunca por defecto.
