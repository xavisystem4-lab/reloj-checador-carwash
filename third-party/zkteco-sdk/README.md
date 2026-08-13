# ZKTeco Standalone SDK (32-bit, Ver 6.2.4.11)

Copiado de [github.com/ZKTeco/Standalone-SDK](https://github.com/ZKTeco/Standalone-SDK)
(carpeta `Communication Protocol SDK(32Bit Ver6.2.4.11)/sdk/`), publicado ahí por el
propio fabricante para construir software de terceros — no es un dump no autorizado.

## Lo importante: es de 32 bits

`zkemkeeper.dll` es un componente COM/ActiveX **de 32 bits únicamente**
(`file zkemkeeper.dll` → `PE32 executable ... Intel 80386`). Un proceso .NET de 64
bits **no puede** cargar un COM server de 32 bits vía interop — son procesos
separados con distinto ancho de puntero. Esto obliga a que `RelojChecador.WPF` (y
cualquier proyecto que referencie `RelojChecador.Infrastructure.Devices` una vez
que exista `ZKTecoDeviceAdapter`) se publique y ejecute como **`win-x86`**, no
`win-x64` — ver `RuntimeIdentifier` en los `.csproj` y `-r win-x64` en
`.github/workflows/build.yml` / `installer/RelojChecador.iss`, que hoy asumen x64
y habrá que actualizar cuando se conecte este adaptador.

## Archivos

- `zkemkeeper.dll` — el componente COM principal (registra vía `regsvr32`, se usa
  desde .NET por COM interop, típicamente como `CZKEMClass`/`IZKEM`).
- `zkemsdk.dll`, `plcommpro.dll`, `commpro.dll`, `comms.dll`, `tcpcomm.dll`,
  `usbcomm.dll`, `rscomm.dll`, `rscagent.dll`, `plcomms.dll`, `plrscomm.dll`,
  `plrscagent.dll`, `pltcpcomm.dll` — dependencias nativas de `zkemkeeper.dll`
  (comunicación TCP/USB/RS232 de bajo nivel). Deben quedar junto al `.dll`
  principal y junto al `.exe` final para que el registro COM las encuentre.

## Cómo se resolvió (sin generar el interop assembly)

En vez de registrar `zkemkeeper.dll` en cada máquina que compila (imposible desde
macOS/Linux, frágil en CI) y generar un `Interop.zkemkeeper.dll` con `tlbimp`,
`ZKTecoDeviceAdapter` (en `RelojChecador.Infrastructure.Devices/ZKTeco/`) usa **enlace
tardío**: `Type.GetTypeFromProgID("zkemkeeper.ZKEM")` + `dynamic`. Eso compila igual en
cualquier sistema operativo — el SDK real solo hace falta en tiempo de EJECUCIÓN, en la
máquina Windows de la sucursal, donde `installer/RelojChecador.iss` ya lo registra
(paso explícito `regsvr32.exe` en `[Run]`/`[UninstallRun]` — no el `Flags: regserver`
original de Inno Setup, que en la primera instalación real falló en silencio sin
registrar nada).

El ProgID correcto (`zkemkeeper.ZKEM`, `VersionIndependentProgID` real del CLSID
`{00853A19-BD51-419B-9269-2DABE57EB61F}`) se confirmó consultando el registro de una
instalación real en Windows tras un `regsvr32` exitoso — el valor original asumido aquí
(`zkemkeeper.CZKEM`, "la convención más citada" sin verificar) resultó estar mal.

Por la misma razón (evitar depender del ensamblado de interop generado, que es lo único
que da eventos COM fuertemente tipados con `+=`), el "tiempo real" pedido por el usuario
se implementa por **sondeo** (`ReadGeneralLogData` cada pocos segundos comparando contra
la última marcación vista) en vez del evento nativo `OnAttTransactionEx` — ver el
comentario de clase en `ZKTecoDeviceAdapter.cs` para el detalle completo.

## Pendiente

- **Confirmar contra el F22/ID real en Windows.** El registro COM y el ProgID ya se
  verificaron correctos (ver arriba). Falta confirmar el resto: los nombres de método/
  códigos del SDK (p. ej. el mapeo de `dwVerifyMode` a `VerifyMethod`, o el "backup
  number" 12 de `SSR_DeleteEnrollData`) siguen la convención más citada en la
  documentación/comunidad del SDK — no verificada todavía contra hardware real, porque
  el desarrollo se hizo desde macOS sin acceso directo a la VM de Windows del usuario.
- `FingerprintTemplateTransfer` y `UserPhotoSync` (ver `DeviceCapabilities`): el SDK las
  soporta, pero `ZKTecoDeviceAdapter` no las implementa todavía — a propósito no las
  anuncia en `GetSupportedCapabilitiesAsync`.
