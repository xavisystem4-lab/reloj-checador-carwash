# Reloj Checador

Software de escritorio para Windows que administra relojes checadores biométricos,
empleados, sucursales, asistencia y nómina, con sincronización a Supabase y operación
offline-first. Proyecto para un carwash en Mexicali, B.C., México — una sola empresa,
varias sucursales (single-tenant).

## Vista previa de la interfaz

Captura real tomada en un runner de Windows durante el CI (ver
`.github/workflows/`, paso "Capturar pantalla de la app") — no es un mockup.
Hoy solo existe esta pantalla mínima (Sucursales + Dispositivos); la
navegación completa del diseño visual es la Fase 3, todavía pendiente.

![Pantalla de Sucursales](docs/screenshots/sucursales.png)

## Stack

- **.NET 10 LTS** / C#
- **WPF + MVVM** (CommunityToolkit.Mvvm), Generic Host para inyección de dependencias
- **EF Core + SQLite** como base local de cada instalación
- **Supabase** (PostgreSQL + Auth) como plataforma central — proyecto dedicado
  `reloj-checador-carwash`, esquema y sincronización push-only ya conectados (ver
  `src/RelojChecador.Infrastructure.Cloud/README.md`)
- **Serilog** para logging estructurado
- **xUnit** para pruebas unitarias/integración
- **Dashboard web** (`dashboard/`): sitio estático HTML/CSS/JS sin build, desplegado en
  Netlify, lee reportes directo de Supabase — ver `dashboard/README.md`

## Estructura

```
src/RelojChecador.Domain/               Entidades y reglas de negocio, sin dependencias externas
src/RelojChecador.Application/          Casos de uso, contratos (IAttendanceDeviceAdapter, repositorios), Result/Error
src/RelojChecador.Infrastructure.Data/  EF Core + SQLite, repositorios, migraciones
src/RelojChecador.Infrastructure.Devices/ Adaptadores de dispositivos (Simulator + ZKTecoDeviceAdapter real)
src/RelojChecador.Infrastructure.Cloud/ Motor de sincronización push-only con Supabase (ver su propio README)
src/RelojChecador.Infrastructure.Security/ Windows Credential Manager (solo compila en Windows)
src/RelojChecador.Infrastructure.Logging/ Configuración de Serilog
src/RelojChecador.WPF/                  Aplicación de escritorio (composition root, ViewModels, Views)
tests/                                  Pruebas unitarias/integración por capa
tools/RelojChecador.DeviceSimulator/    Simulador standalone del protocolo del reloj (pendiente de contenido)
installer/                              Script de Inno Setup + guía para generar el instalador de Windows
supabase/                               Migraciones SQL versionadas (ya aplicadas al proyecto real); Edge Functions pendiente
dashboard/                              Sitio estático (HTML/CSS/JS) de reportes, desplegado en Netlify — ver su propio README
```

## Compilar y probar

```bash
dotnet build
dotnet test
```

> **Nota de entorno**: este proyecto se ha desarrollado desde macOS. Todo excepto la UI
> compila y se prueba de forma nativa multiplataforma. El proyecto `RelojChecador.WPF`
> (y `Infrastructure.Security`) **compilan** también desde macOS/Linux gracias a
> `EnableWindowsTargeting`, pero **solo se pueden ejecutar y verificar visualmente en
> Windows real**. Se ha verificado que `dotnet publish -r win-x86 --self-contained`
> genera un `.exe` de Windows válido incluso compilando desde macOS. Es `win-x86`
> (32 bits), no `win-x64`, porque el SDK real de ZKTeco (`zkemkeeper.dll`, ver
> `third-party/zkteco-sdk/README.md`) es un COM server de 32 bits.

## Generar el instalador de Windows

Ver [`installer/README.md`](installer/README.md). Requiere ejecutarse en Windows con
Inno Setup instalado — no es posible compilarlo desde macOS/Linux.

## Estado del proyecto (Fase 4 — producto mínimo funcional)

**Hecho:**
- Arquitectura Clean/Onion con 12 proyectos y referencias correctas entre capas
- Entidades de dominio: `Branch`, `Employee`, `Device`, `EmployeeDeviceMapping`, `User`
- Contrato `IAttendanceDeviceAdapter` + patrón `Result`/`Error` + `SimulatorDeviceAdapter`
  (datos de prueba) y `ZKTecoDeviceAdapter` real (COM tardío contra `zkemkeeper.dll` —
  ver `third-party/zkteco-sdk/README.md`; probado en construcción contra el F22/ID de
  campo, pendiente de confirmación final en Windows real) con monitoreo en tiempo real
  por sondeo (la asistencia aparece sola en la pantalla de Dispositivos, no requiere
  presionar "Descargar")
- Base local SQLite con EF Core: DbContext, configuraciones, migración inicial, repositorios
- Composition root real del WPF (Generic Host, DI, Serilog, migraciones automáticas al
  iniciar, manejo global de excepciones no controladas, primera pantalla que lee de la
  base local)
- Instalador (Inno Setup), compilado y probado en Windows real vía CI — publica como
  `win-x86` (32 bits, requerido por `zkemkeeper.dll`) y registra el SDK de ZKTeco como
  servidor COM al instalar
- Entidad `Attendance` (persistencia local real de las marcaciones, con deduplicación
  respaldada por un índice único — antes se perdían al cerrar la app) + repositorio
  `IEmployeeDeviceMappingRepository`
- Motor de sincronización push-only con Supabase (proyecto dedicado
  `reloj-checador-carwash`): esquema con RLS aplicado y verificado (lectura solo para
  usuarios autenticados, escritura solo desde la app de escritorio vía `service_role`),
  sincronización incremental de asistencias por cursor, tablas chicas completas en cada
  ciclo, offline-first (nunca tumba la app sin internet) — **confirmada funcionando en
  hardware real** (indicador "Nube: conectado" en la barra superior, punto verde/rojo
  según estado, botón "Conectar con nube" para forzar un ciclo, sincroniza cada 5s) — ver
  `src/RelojChecador.Infrastructure.Cloud/README.md` para cómo activarla en una instalación
- `ZKTecoDeviceAdapter` **confirmado contra hardware real** (F22/ID de campo) en Windows:
  ProgID correcto (`zkemkeeper.ZKEM`), descarga de asistencias vía
  `SSR_GetGeneralLogData` + `ReadAllGLogData` (parámetros `ref` como `object` genérico),
  registro de éxito/fallo de cada intento de conexión para reflejar el estado real
- Módulo de auto-actualización vía GitHub Releases
  (`RelojChecador.Infrastructure.Updates`): consulta la última release del repo público,
  compara contra la versión del ensamblado, descarga el instalador con progreso y lo
  lanza — botón "Actualizar versión" en la barra inferior de la app
- Modo oscuro en la app de escritorio (barra superior) y en el Dashboard
- Panel de usuarios del Dashboard (invitar/quitar acceso), mostrando nombre en vez de
  correo — ver `dashboard/README.md`
- Pantalla de Empleados: alta, edición y listado — mismo patrón MVVM que
  Sucursales/Dispositivos, tramo principal de la Fase 3 (navegación completa de la UI). El
  alta permite vincular a un reloj checador (dispositivo + PIN) en el mismo formulario, sin
  paso aparte
- Vincular Empleado↔Dispositivo (`EmployeeDeviceMapping`): desde la pantalla de Empleados,
  botón "Vincular a dispositivo" por fila (PIN capturado a mano, no descargado del
  dispositivo — ver decisión de alcance en `EmployeesViewModel`); columna "Dispositivos
  vinculados" muestra el resumen. Prerequisito de una futura pantalla de Asistencia que
  muestre nombres en vez de PINs crudos
- Motor de sincronización: cada tabla se sube de forma aislada (un fallo puntual en una no
  bloquea a las demás en el mismo ciclo — antes si podía pasar) y la pantalla de Empleados
  recarga la lista completa desde la base local tras cada alta/edición/vínculo en vez de
  mutar la vista a mano, para eliminar de raíz un caso real donde el DataGrid no reflejaba
  una fila recién guardada hasta reiniciar la app (el dato en sí nunca se perdía)
- Pantalla de Asistencia: consulta las marcaciones ya guardadas localmente (filtros de
  sucursal, rango de fechas y texto libre), con nombre de empleado resuelto — mismo
  criterio de resolución que ya usaba el Dashboard web (`Attendance.EmployeeId` directo, o
  si no hay, `EmployeeDeviceMapping` por dispositivo+PIN). Al crear un vínculo
  Empleado↔Dispositivo ahora se concilian retroactivamente las marcaciones de ese
  dispositivo+PIN que hubieran llegado antes (usa `Attendance.ReconcileEmployee`, ya
  existía en el dominio sin que nada lo invocara). Última pantalla del menú de Fase 3;
  Reportes queda para Fases 5-6
- Eliminar empleados: baja lógica (`ChangeStatus` a `Terminated`), nunca borra el registro
  ni su historial — se oculta de la lista por defecto, con un checkbox "Mostrar dados de
  baja" para volver a verlos
- Exportar CSV desde la pantalla de Asistencia de la app de escritorio (mismas columnas y
  traducciones que el CSV del Dashboard web, para que abran igual en Excel)
- Corregido: el indicador "Conectado"/"Desconectado" de cada reloj en el Dashboard (basado
  en `Device.LastCommunicationAtUtc`) solo se actualizaba al presionar "Conectar" a mano en
  Dispositivos — nunca con las marcaciones que llegaban por el monitoreo en tiempo real.
  Reportado por el usuario como "a veces se desconecta" sin explicación; ahora cada
  marcación en tiempo real también refresca esa marca de tiempo
- Pantalla de Reportes (Fases 5-6, alcance acotado explícitamente con el usuario — **sin
  ningún cálculo fiscal**, ni ISR/IMSS ni la regla de Ley Federal del Trabajo para horas
  extra): horas trabajadas + insumo de nómina por semana (lunes a domingo, ver
  `RelojChecador.Application.Payroll.WeekBoundary`), calculado desde las marcaciones ya
  guardadas. `Employee` gana `WeeklySalary` (sueldo semanal fijo, requerido) y
  `OvertimeHourlyRate` (tarifa fija en pesos por hora extra, opcional, capturada por el
  usuario — nunca una regla legal asumida por el sistema). El cálculo
  (`WorkedHoursCalculator`) es defensivo: nunca inventa el cierre de un turno; cualquier
  marcación sin pareja se reporta como advertencia visible en la columna "Advertencias",
  nunca se suma a ciegas. **Los descansos (`PunchType` 2/3) siguen sin confirmarse contra
  hardware real** — ver `PunchTypeToTextConverter` — así que su cálculo es especulativo
  hasta comprobarse. Empleados dados de alta ANTES de esta versión quedan con
  `WeeklySalary = 0` tras actualizar (la migración no puede inventar un sueldo) — hay que
  editarlos con su sueldo real o el reporte los mostrará en $0
- Corregido: los ítems de cualquier `ComboBox` (Sucursal, Estatus, Dispositivo, etc.) eran
  casi ilegibles al pasar el mouse sobre ellos — el resaltado nativo de Windows usa un
  fondo claro que Styles.xaml no cubría, dejando el texto claro del tema oscuro sobre un
  fondo también claro. `ComboBoxItem` ya tiene su propio `ControlTemplate` (como
  `ListBoxItem`), usando `BrushSelection` para el resaltado en vez del color nativo
- Intervalo de sincronización 30s (antes 5s, a pedido explícito del usuario) —
  `SupabaseSyncOptions.IntervalSeconds` en `appsettings.json`, y el auto-refresco del
  Dashboard web ajustado igual para seguir mostrando los datos prácticamente al día
- Corregir el PIN de un vínculo Empleado↔Dispositivo ya existente: caso real detectado con
  el usuario — capturó el número de empleado en vez del PIN real del reloj al vincular, y
  "Vincular a dispositivo" de nuevo con el PIN correcto lo rechazaba (índice único
  `(DeviceId, EmployeeId)`, ver `EmployeeDeviceMappingConfiguration`) sin ninguna forma de
  corregirlo. Nuevo botón "Editar vínculo(s)" (`EmployeeDeviceMapping.UpdatePin`) por fila
  de Empleados — solo corrige el PIN de vínculos existentes, no elimina ni agrega (el
  motor de sync es push-only por upsert, nunca hace `DELETE` remoto — eliminar un vínculo
  local dejaría un huérfano en Supabase para siempre, así que esa función queda fuera de
  alcance por ahora)
- Corregido: el `ComboBox` cerrado (Sucursal, Estatus, etc.) se veía con fondo claro y
  texto ilegible en modo oscuro — a diferencia del fix anterior (solo `ComboBoxItem`, el
  desplegable abierto), el propio control cerrado no tenía `ControlTemplate` propio y el
  chrome nativo de Windows (Aero2) ignoraba el `Background`/`Foreground` asignados en la
  práctica. Ahora tiene plantilla completa (Border + ToggleButton + Popup propios, mismo
  patrón que `TextBox`/`ComboBoxItem`)
- Corregido: doble desfase de zona horaria (~7h) en el Dashboard web — `timestamp_utc` de
  las marcaciones NO es UTC real (el reloj checador entrega su hora local de Mexicali sin
  convertir, ver `Attendance.Create`), pero el Dashboard sí le aplicaba una conversión real
  de huso horario al mostrarlo (`toLocaleString`), restándole el offset una segunda vez.
  Ver `dashboard/README.md` para el detalle completo y el badge nuevo de "Conectado" en el
  header (visible también en móvil)
- Reconexión automática al reloj checador (`DevicesViewModel`, `_autoReconnectTimer`, cada
  15s): antes, cualquier corte de red o reinicio del dispositivo dejaba de subir
  marcaciones nuevas hasta que alguien entraba a Dispositivos y presionaba "Conectar" a
  mano — reportado por el usuario como que la nube "no se actualiza" hasta hacerlo. Se
  respeta un "Desconectar" manual explícito (no reconecta solo hasta que el usuario vuelva
  a presionar "Conectar")
- Sincronización con Supabase casi instantánea, a pedido explícito del usuario ("que sea
  prácticamente instantáneo... en cuanto hay un evento, lo comunique de inmediato con la
  nube"): el intervalo de respaldo baja de 30s a 10s (`SupabaseSyncOptions.IntervalSeconds`,
  Dashboard ajustado igual), y además cada marcación nueva (tiempo real o descarga manual)
  dispara su propio ciclo de sincronización de inmediato en vez de esperar el próximo tick
  — ver `DevicesViewModel.PersistAndTriggerSyncAsync`, que reutiliza el mismo
  `SupabaseSyncBackgroundService.TriggerSyncNowAsync` del botón "Conectar con nube"
- **Bug crítico corregido: el `IHost` nunca se arrancaba.** `App.xaml.cs` construía el host
  (`Host.CreateDefaultBuilder().Build()`) pero jamás llamaba `Start()`/`StartAsync()` — sin
  eso, ningún `IHostedService` (`SupabaseSyncBackgroundService`, el único registrado en toda
  la solución) ejecuta su ciclo automático. En la práctica, el intervalo periódico nunca
  corrió por sí solo: todo lo que parecía sincronizar "solo" venía de llamadas directas a
  `TriggerSyncNowAsync()` (el botón "Conectar con nube", y los triggers atados a
  marcaciones). Cambios que no pasaban por esos triggers puntuales (editar un empleado, una
  sucursal, un vínculo) dependían 100% de presionar "Conectar con nube" a mano. Corregido
  con `_host.Start()` en `OnStartup` y `_host.StopAsync(...)` ordenado en `OnExit`
- **Sincronización remota "Actualizar asistencias"** (Dashboard → sistema local → nube, a
  pedido explícito del usuario, con requisitos de seguridad detallados): nuevo botón en el
  Dashboard que le pide al sistema local de la sucursal que se conecte al reloj checador y
  suba lo más reciente de inmediato, sin esperar el próximo ciclo. Arquitectura 100%
  saliente en ambos sentidos (nunca hay conexión entrante hacia la PC del negocio) vía una
  nueva tabla intermedia `public.sync_requests` en Supabase:
  - El Dashboard solo puede `INSERT` (crear la solicitud, con su propio `user_id`) y
    `SELECT` (ver el estado) — nunca `UPDATE`/`DELETE`; excepción acotada y explícita a la
    regla general del esquema ("todo lo que escribe pasa por la app de escritorio"), ver la
    migración `20260814060000_add_sync_requests.sql`.
  - El sistema local (`RemoteSyncRequestPollingService`, nuevo `BackgroundService`) la
    consulta cada `IntervalSeconds` (10s) — nunca al revés. Si la PC está apagada, la
    solicitud queda `pending` y se completa sola al reconectar, sin nada especial que
    implementar aparte de que el polling sea confiable (de ahí el fix del `IHost` de
    arriba, requisito real para esta función).
  - `RemoteSyncRequestCoordinator` (Infrastructure.Cloud) detecta la solicitud, la marca
    `in_progress` y avisa vía evento — nunca toca dispositivos directamente, mantiene la
    separación de capas. `DevicesViewModel` (WPF) la procesa reutilizando tal cual
    `ConnectAsync`/`DownloadAttendanceCoreAsync`/`TriggerSyncNowAsync` (sin duplicar
    lógica) y reporta `completed`/`failed` con un mensaje legible.
  - Duplicados: índice único parcial en Postgres (como mucho una solicitud
    `pending`/`in_progress` a la vez en toda la tabla) + el Dashboard se engancha a una
    activa existente en vez de crear otra.
  - Estados visibles en el Dashboard: "Solicitud enviada…" → "Sincronizando…" →
    "✅ resumen" / "❌ error"; al completarse, la lista y los KPIs se refrescan solos.
  - 12 tests nuevos (`tests/RelojChecador.Infrastructure.Tests/Cloud/`) cubriendo
    `SupabaseRestClient.GetAsync/PatchAsync` y todo el ciclo de
    `RemoteSyncRequestCoordinator` (detección, guardia contra duplicados, éxito/fallo) con
    un `HttpMessageHandler` falso — sin tocar la red real.

**Pendiente (bloqueado por decisiones o datos externos):**
- Confirmar el login real del Dashboard (requiere que el usuario cree su primera cuenta
  en Supabase Auth — ver `dashboard/README.md` — algo que, por diseño, nunca se hace
  automáticamente desde aquí)
- Navegación completa de la UI (Fase 3 del diseño visual — Sucursales, Empleados,
  Dispositivos, Asistencia y Reportes ya existen; falta el resto de secciones finales)
- Confirmar contra hardware real el significado de `PunchType` 2/3 (descansos) — hoy
  especulativo, ver `WorkedHoursCalculator`
- Cálculo fiscal de nómina (ISR, IMSS), auditoría, incidencias — resto de Fases 5-6, fuera
  de alcance hasta confirmar las tablas y reglas vigentes
- Razón social real y logotipo/icono para el instalador y la app

## Convenciones

- Cada tarea completada se compila y prueba antes del siguiente paso (`dotnet build` +
  `dotnet test` en verde) y queda en un commit propio con su justificación.
- Nunca se inventan comandos/protocolos de fabricantes de dispositivos ni reglas fiscales
  sin confirmarlas explícitamente.
