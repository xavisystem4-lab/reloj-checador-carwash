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

## Pendiente antes de poder usarlo

1. Cambiar `RelojChecador.WPF` (y el publish/instalador) de `win-x64` a `win-x86`.
2. Registrar `zkemkeeper.dll` en el equipo de desarrollo/CI (`regsvr32 zkemkeeper.dll`)
   para poder generar el COM interop assembly, o referenciarlo con
   `Embed Interop Types` desde una referencia COM directa en el `.csproj`.
3. Escribir `ZKTecoDeviceAdapter : IAttendanceDeviceAdapter` en
   `RelojChecador.Infrastructure.Devices` (hoy solo existe `SimulatorDeviceAdapter`),
   probado contra el reloj F22/ID real (192.168.1.66:4370) ya registrado en la base
   local.
4. Verificar `RegEvent`/eventos en tiempo real del SDK para la sincronización
   instantánea pedida (no solo descarga on-demand) — capacidad ya contemplada como
   `DeviceCapabilities.RealTimeEvents`.
