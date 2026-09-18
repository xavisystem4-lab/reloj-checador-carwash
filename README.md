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
- **Descarga automática cada 10s** (`DevicesViewModel._autoDownloadTimer`), a pedido
  explícito del usuario: "que el botón de descarga asistencia se actualice por sí solo",
  sin depender de que el monitoreo en tiempo real esté funcionando ni de una señal remota
  del Dashboard. Mientras haya un dispositivo conectado, descarga y sube a la nube
  exactamente igual que el botón "Descargar asistencias" — es una TERCERA vía puramente
  local, independiente de las otras dos (monitoreo en tiempo real, solicitud remota desde
  el Dashboard), así que sigue funcionando aunque cualquiera de esas otras dos falle en
  silencio. Deliberadamente silenciosa en la bitácora cuando no hay nada nuevo (no
  registra cada 10s sin motivo). `DownloadAttendanceCoreAsync` (el núcleo compartido por
  los tres caminos: botón manual, este timer, y la solicitud remota) gana una guardia de
  reentrancia (`_isDownloading`) — con descargas disparándose cada 10s, es real que dos
  caminos coincidan si el dispositivo tarda en responder.
- **Deducciones de nómina (ISR/IMSS/otro) — captura 100% MANUAL, nunca calculada.** El
  usuario pidió avanzar con el cálculo fiscal, pero al preguntarle por el régimen y las
  tablas a usar fue explícito: nada de eso, todo se captura a mano. Nueva entidad
  `PayrollDeduction` (una fila por `(EmployeeId, WeekStart)`, nunca recurrente — ISR/IMSS
  varían cada semana según lo devengado) con tres montos (`IsrAmount`/`ImssAmount`/
  `OtherAmount`, este último con una etiqueta libre para INFONAVIT/préstamos/faltas/etc.).
  En Reportes: botón "Editar deducciones" por fila (mismo patrón que "Editar vínculo(s)"
  en Empleados) abre un diálogo para capturarlas; la tabla muestra ISR/IMSS/Otro/Neto a
  pagar (`TotalPay` menos las tres deducciones, sin impedir que salga negativo — es el
  usuario quien capturó los montos). Se sincroniza a Supabase igual que el resto del
  dominio (`payroll_deductions`, solo lectura para `authenticated`) — deja la puerta
  abierta, barata, para una futura UI de nómina en el Dashboard, que NO se construye en
  esta entrega. 10 tests nuevos (Domain + repositorio EF).
- **Modernización visual + exportar/imprimir Sucursales + claridad de "Usuarios" en el
  Dashboard**, a pedido explícito del usuario ("las fuentes están un poco chicas...
  mejora mucho la interfaz"):
  - Escala tipográfica un paso arriba en toda la app de escritorio (base 13→14, títulos
    24→26, subtítulos 12-13→14, microetiquetas 11→12) y en el Dashboard (`body` gana
    `font-size: 15px` explícito, antes heredaba 11-13.5px sueltos por elemento) —
    moderado a propósito, un salto mayor arriesgaba romper las columnas de ancho fijo de
    los `DataGrid` sin poder verlo renderizado antes de que el usuario lo probara.
  - Pulido de componentes: `DataGrid` con más aire (`RowHeight` 32→36), botones/campos
    con más padding, `TabItem` con ícono por pestaña (🏢👥⏱️📋💰) y tinte de fondo en la
    activa, `GroupBox` con `DropShadowEffect` sutil — sin tocar la paleta de colores, que
    ya era adecuada.
  - Sucursales (la única pantalla sin ningún botón de exportación hasta ahora) gana
    "⬇ Exportar CSV" y "🖶 Imprimir / Exportar PDF" — este último es el primer uso de
    impresión del proyecto: un `FlowDocument` + el `PrintDialog` nativo de WPF, donde el
    driver "Microsoft Print to PDF" (incluido de fábrica en Windows 10/11) cubre el caso
    de PDF sin ninguna dependencia nueva en el `.exe` self-contained.
  - Dashboard: el botón que ya mostraba el nombre de la sesión actual (`user-name-button`,
    confirmado con una consulta a Supabase que la cuenta sí tenía `full_name` guardado)
    se confundía visualmente con "👤 Usuarios" de al lado — ahora muestra "👤 {nombre}"
    explícito y el de administración se renombra a "⚙️ Administrar usuarios".
- **Importación masiva de empleados desde CSV**, a pedido del usuario tras compartir un
  catálogo real de 54 empleados (Excel). Análisis previo detectó un límite real del
  sistema (nunca existió un empleado con sueldo desconocido) y se resolvió volviendo
  `Employee.WeeklySalary` `decimal?` de verdad en TODO el sistema (dominio, EF, Supabase,
  `WorkedHoursCalculator`, UI) — `null` significa "pendiente de captura", nunca se suma
  como `$0`. Nueva propiedad `Employee.Notes` (texto libre) para conservar observaciones
  de origen en importaciones futuras, para auditoría.
  - `EmployeeImportParser` (Application, lógica pura sin infraestructura, igual criterio
    que `WorkedHoursCalculator`): parsea CSV, nunca inventa puesto/sueldo faltante, genera
    alertas (sueldo pendiente, "SIN PUESTO").
  - Botón "⬆ Importar desde CSV" en Empleados abre `ImportEmployeesDialog`: vista previa
    completa (con alertas) antes de cualquier cambio real, resumen de conteos, sucursales
    nuevas que se crearían — solo entonces se habilita "Importar". Nunca sobreescribe un
    empleado existente (número duplicado = se omite y se reporta), nunca vincula a un
    dispositivo (el CSV no trae esa información, se hace después individualmente).
  - `AddEmployeeDialog`/`EditEmployeeDialog` ganan campo "Notas" y el sueldo semanal pasa
    a ser opcional (antes obligatorio) — consistentes con la vía masiva.
  - 20 tests nuevos/actualizados (Domain + Application).
- **Botón "📤 Enviar empleados al reloj"** (Dispositivos), a pedido explícito del usuario
  tras la importación masiva ("agrega un botón para mandar esta información al reloj
  checador"). Escribe Nombre + PIN en la memoria del dispositivo vía `SSR_SetUserInfo`
  (`IAttendanceDeviceAdapter.CreateOrUpdateUserAsync` — ya existía en el adaptador desde
  antes pero nunca estuvo conectado a ningún botón) para cada empleado activo de la
  sucursal del dispositivo que aún no tenga vínculo (`EmployeeDeviceMapping`) con él.
  - El PIN se asigna en automático (1, 2, 3…), nunca el "Number" de negocio (ej.
    "EMP-001") — confirmado con el usuario: el teclado del reloj es numérico y ese
    formato lo rechazaría. Antes de asignar, se descargan los usuarios reales ya
    existentes en el dispositivo (`DownloadUsersAsync`) para no chocar con PINs ocupados
    por gente enrolada a mano antes de que existiera este botón.
  - Solo prepara el PIN + nombre para que la persona pueda enrolar su huella físicamente
    en el reloj — nunca sube huellas ni hace ese paso por sí solo. Reporta en la bitácora
    cuántos se enviaron y cuáles fallaron, sin detener el lote por un solo error.
- **Filtros en Empleados** (búsqueda por nombre/número + sucursal + estatus), a pedido
  explícito del usuario ("aplícale filtro a empleados") para poder navegar el catálogo
  real de 54+ empleados sin desplazarse a mano por todo el `DataGrid`. Los tres filtros se
  combinan entre sí y con el checkbox "Mostrar dados de baja" ya existente, todo en
  memoria sobre la lista ya cargada (`ApplyVisibilityFilter`) — instantáneo, sin volver a
  tocar la base de datos por cada letra escrita. El combo de sucursal solo lista las
  sucursales que de verdad tienen empleados (nunca una vacía como opción inútil).
- **Filtros en Reportes** (búsqueda por nombre/número + sucursal), a pedido explícito del
  usuario ("el filtro lo quiero... en reportes"). Mismo criterio que Empleados: se filtra
  en memoria sobre lo ya calculado para la semana actual (`PayrollViewModel.ApplyFilter`),
  nunca vuelve a recalcular la nómina por escribir en el buscador o cambiar de sucursal.
  El combo de sucursal se reconstruye cada vez que cambia la semana, listando solo las
  sucursales que tienen alguna fila esa semana.
- **Editar Dispositivos y Sucursales**, a pedido explícito del usuario ("quiero editar mi
  Dispositivo ya creado y también quiero editar sucursales"). Ambas pantallas solo tenían
  alta hasta ahora.
  - `Device.UpdateDetails` (nombre, marca, modelo, sucursal, zona horaria, serie, MAC) +
    `UpdateNetworkSettings` (ya existía, IP/puerto) — botón "✏️ Editar dispositivo" en el
    panel de diagnóstico, siempre sobre `SelectedDevice`. Tras guardar se recarga la
    lista completa y se reselecciona el mismo dispositivo por Id — si estaba conectado,
    la conexión se reinicia a propósito (la IP/puerto pudo haber cambiado), y el
    auto-reconnect (15s) la retoma sola.
  - `Branch.ChangeCode` (nuevo, mismo criterio que `Employee.ChangeNumber`: corrige un
    error de captura del alta) + `Rename`/`UpdateTimeZone`/`UpdateLegalInfo`/`Activate`/
    `Deactivate` (ya existían, sin usar hasta ahora) — botón "Editar" por fila en el
    `DataGrid` de Sucursales, incluye el estatus Activa/Inactiva que el alta no expone.
  - 5 tests nuevos (Domain).
- **"Desarrollado por SoftGala"** a la izquierda de la versión, en la barra inferior
  siempre visible — pedido explícito del usuario.
- **Fix: el estado del dispositivo no llegaba a Supabase de inmediato** — reportado por el
  usuario con captura ("cada vez que yo cambie los parámetros ya sea de IP, puerto, etc.,
  este siempre debe actualizar en Supabase y mostrar conectado"): el Dashboard mostraba
  "Desconectado (hace 8 h)" mientras la app de escritorio ya decía "Conectado (hace 0s)".
  Causa real: `ConnectAsync`/`DisconnectAsync` (`TryPersistCommunicationResultAsync`) y el
  nuevo `UpdateDeviceAsync` (editar dispositivo) nunca disparaban
  `SupabaseSyncBackgroundService.TriggerSyncNowAsync()` — el cambio de estado quedaba
  esperando hasta el próximo ciclo automático (`IntervalSeconds`), y un "Desconectar"
  manual ni siquiera tocaba `Device.Status` localmente. Ahora los tres empujan el cambio a
  Supabase de inmediato, mismo criterio que ya existía para marcaciones nuevas.
- **Fix crítico: crash de la app por actualizar la UI desde un hilo de fondo** —
  reportado por el usuario con la ventana de error de Windows
  (`TaskScheduler.UnobservedTaskException` → `NotSupportedException`: "Este tipo de
  CollectionView no admite cambios en el SourceCollection de un subproceso distinto del
  subproceso Dispatcher"), disparado al procesar una solicitud remota de sincronización
  desde el Dashboard. Causa real: `OnRemoteSyncRequested` solo marshalizaba al Dispatcher
  el primer `AppendLog`; el fire-and-forget de `ProcessRemoteSyncRequestAsync` (que toca
  `AttendanceRecords`, `LogEntries` y varias propiedades observables vía `ConnectAsync`/
  `DownloadAttendanceCoreAsync`) quedaba corriendo en el hilo de sondeo de
  `RemoteSyncRequestPollingService` — WPF lo rechaza, y como nadie observaba esa excepción
  (Task sin `await`), terminaba re-lanzada por el finalizer y tumbaba la app entera. Se
  encontró y corrigió el mismo patrón en `OnAttendancePunchReceived` (marcaciones en
  tiempo real) antes de que también fallara. Ambos ahora inician toda la cadena async
  dentro de un único bloque marshalizado al Dispatcher; `ProcessRemoteSyncRequestAsync`
  además gana un try/catch general (defensivo ante cualquier excepción futura no
  relacionada con hilos, dado que se invoca fire-and-forget).
- **Fix: una solicitud remota abandonada por el crash de arriba quedaba "Sincronizando…"
  para siempre en el Dashboard** — confirmado directamente contra Supabase (`sync_requests`
  con `status = 'in_progress'` desde hace horas, `completed_at_utc` nulo).
  `RemoteSyncRequestCoordinator.PollForPendingRequestAsync` solo consultaba
  `status=eq.pending`; una fila que quedó "in_progress" por un cierre a la mitad del
  proceso nunca se volvía a recoger, ni siquiera al reiniciar la app (el guardia
  `_activeRequestId` es solo en memoria). Ahora también reclama solicitudes "in_progress"
  abandonadas hace más de 2 minutos (`or=(status.eq.pending,and(status.eq.in_progress,
  started_at_utc.lt.…))`, filtro de PostgREST) — se autorrecupera solo, sin intervención
  manual en Supabase.
- **Ícono de marca "GalaCheck"** en el `.exe`/accesos directos/taskbar (`ApplicationIcon`
  en el `.csproj`, generado como `.ico` multi-resolución desde el PNG cuadrado
  proporcionado) y personalización del instalador (Inno Setup `SetupIconFile` +
  `WizardImageFile`/`WizardSmallImageFile`) — pedido explícito del usuario. **No
  verificable visualmente desde macOS** (a diferencia de la app, el asistente de
  instalación de Inno Setup no se captura en el screenshot del CI).
- **Fix: el logotipo del instalador "no se apreciaba"** (reportado por el usuario tras
  probar v1.18.0). Causa real: `WizardImageFile` se compuso sobre un fondo azul marino
  oscuro, pero el logotipo "GalaCheck" es en tonos azul marino/cian sobre transparencia —
  colores oscuros sobre fondo oscuro, contraste casi nulo (confirmado muestreando los
  píxeles reales del PNG: el texto "Gala" es `#001338`, casi idéntico al fondo elegido).
  Recompuesto sobre fondo blanco — contraste fuerte con ambos tonos del logo.
- **Fix: `Device.LastCommunicationAtUtc` se quedaba congelado durante horas pese a que el
  dispositivo seguía conectado en vivo** — la causa real de "Desconectado (hace 8h)"
  persistente en el Dashboard, confirmado consultando Supabase directamente
  (`last_communication_at_utc` sin moverse en las 4 tablas principales durante 8+ horas
  seguidas, mientras la app mostraba "Conectado (hace 0s)" — ese texto es el estado del
  *ciclo de sincronización con Supabase*, `UpdateViewModel.CloudSyncShortStatus`, NO el
  del dispositivo físico; son dos cosas distintas). Ese campo solo se tocaba en dos
  eventos puntuales — un Conectar/Desconectar, o una marcación real — así que si el reloj
  se queda conectado sin que nadie poncher durante varias horas (de madrugada, por
  ejemplo), nunca se refrescaba, aunque la conexión siguiera perfectamente sana. Ahora
  `TryAutoDownloadAsync` (cada 10s mientras está conectado) trata una descarga exitosa
  —aunque traiga 0 marcaciones nuevas— como prueba real de comunicación viva y refresca el
  campo + lo empuja a Supabase de inmediato.
- **Fix: la app se quedaba "Conectado" para siempre aunque el reloj hubiera dejado de
  responder de verdad** — diagnosticado en vivo con el usuario (confirmó que "Descargar
  asistencias" a mano fallaba o no traía nada nuevo pese a haber checadas reales, con la
  pantalla mostrando "Conectado" todo el tiempo). Causa real: un fallo genuino de lectura
  del dispositivo (`DownloadAttendanceCoreAsync`) nunca tocaba `IsConnected` — como
  `TryAutoReconnectAsync` solo actúa cuando `IsConnected` es `false`, el auto-reconnect
  (cada 15s) jamás volvía a intentar una reconexión real: un punto muerto silencioso hasta
  que alguien presionara "Desconectar" y "Conectar" a mano. Ahora un fallo real de lectura
  (manual, automático o por solicitud remota — los tres pasan por el mismo núcleo) corta
  el monitoreo, marca `IsConnected = false` de verdad y empuja el fallo a Supabase de
  inmediato, para que el ciclo de 15s retome la reconexión sin intervención manual.
- **Logotipo del negocio "Drive In Car Wash"** en la esquina superior izquierda de
  `MainWindow`, a pedido explícito del usuario ("que haga contraste al tema día y noche").
  Recortado del archivo original (quitando el margen blanco) y montado sobre una tarjeta
  blanca fija (`Background="White"`, nunca `DynamicResource`) — el logo trae tonos azul
  marino que se perderían casi por completo puestos directo sobre el fondo oscuro del
  tema noche (mismo error de contraste ya corregido en el instalador, ver v1.18.1); una
  tarjeta blanca constante garantiza contraste fuerte sin importar el tema activo.
- **Fix: regresión del fix anterior** — el usuario reportó que "en la 1.17.2 sí
  funcionaba y ahorita ya no": el dispositivo prendido, con la IP correcta, con internet,
  pero sin comunicar. Causa real: el fix de arriba marcaba desconectado ante CUALQUIER
  fallo de lectura, incluido uno pasajero (p. ej. un timeout puntual del SDK de ZKTeco) —
  antes ese mismo fallo aislado se ignoraba solo y el siguiente ciclo de 10s reintentaba
  sobre la MISMA conexión ya abierta, sin problema. Forzar una reconexión completa desde
  cero en cada fallo resultó menos confiable que simplemente reintentar, porque el
  handshake de reconexión (5 niveles de diagnóstico) es más propenso a fallar que una
  lectura sobre una conexión ya establecida. Ahora se exigen 3 fallos SEGUIDOS
  (`_consecutiveDownloadFailures`, ~30s sostenidos) antes de dar por muerta la conexión —
  un fallo aislado se ignora igual que antes de v1.19.0; solo una racha real dispara la
  reconexión.
- **Fix crítico de raíz: el monitoreo en tiempo real nunca funcionó, desde siempre** —
  encontrado leyendo el código a fondo a pedido explícito del usuario ("resuélvelo de raíz,
  busca en el código que falló"), tras confirmar con pruebas reales de red (ping + puerto
  TCP 4370 abiertos, conexión cruda aceptada) que el dispositivo estaba sano y descartar así
  cualquier causa de red/hardware. `ZKTecoDeviceAdapter.PollForNewPunchesAsync` guardaba
  "desde cuándo buscar marcaciones nuevas" con `DateTime.UtcNow` (hora UTC real), pero
  `RawAttendanceRecord.TimestampUtc` —pese al nombre— en realidad contiene la hora LOCAL
  cruda del reloj sin convertir (documentado a propósito: "el reloj no aplica ninguna
  conversión de zona horaria", y consistente con el resto del sistema, que nunca hace
  conversión real de huso horario — ver `AttendanceViewModel`/`PayrollViewModel`, que arman
  sus rangos con `DateTime.Now` + `DateTime.SpecifyKind(..., DateTimeKind.Utc)` sin sumar
  ningún offset, a propósito, para un negocio de una sola sucursal). Con Mexicali en
  UTC-7/UTC-8, la hora local del dispositivo para CUALQUIER marcación nueva quedaba
  numéricamente por detrás de un `DateTime.UtcNow` real — la comparación `>` nunca era
  cierta, así que el evento `AttendancePunchReceived` (la aparición "al instante" de una
  marcación) jamás se disparó, para ninguna marcación, desde que se escribió el adaptador.
  Corregido inicializando esa marca con `DateTime.Now` (hora local), comparando como
  corresponde: local contra local, igual que el resto del sistema. Campo renombrado de
  `_realTimeSinceUtc` a `_realTimeSinceDeviceLocal` para no perpetuar el nombre engañoso
  que costó encontrar el bug.
- **"Usuarios del reloj"** (Dispositivos → botón nuevo junto a "Consultar información"), a
  pedido explícito del usuario tras preguntar "¿cuántos empleados están dados de alta en
  el reloj checador?" — antes solo se veía el conteo total (`InfoUserCount`); ahora hay una
  pantalla completa con el detalle real (PIN, nombre, privilegio, habilitado) leído
  directo de la memoria del dispositivo (`DownloadUsersAsync`), con:
  - **Editar** — corrige nombre/habilitado de un usuario ya existente (`CreateOrUpdateUserAsync`,
    mismo método usado por "Enviar empleados al reloj"). El PIN no se edita aquí: el SDK lo
    usa como identificador de a cuál usuario escribir, no se puede "renombrar" con esa
    llamada.
  - **Eliminar** individual y **selección masiva** ("Seleccionar todos" + "Eliminar
    seleccionados") — ambos caminos pasan por el mismo método (`DeleteDeviceUsersAsync`),
    un fallo en un PIN no detiene el resto del lote, se reportan todos los fallos juntos al
    final. Deliberadamente NO borra `EmployeeDeviceMapping` en la base local — el historial
    de asistencia ya guardado no se pierde, y si se vuelve a dar de alta con el mismo PIN el
    vínculo local sigue siendo válido.
  - Confirmación explícita antes de eliminar (individual o en lote), dejando claro que es
    irreversible en el dispositivo pero no afecta el historial ya guardado.
- **v1.60.0 — la sincronización se repara sola cuando el Id de una sucursal no coincide con
  el de Supabase.** Caso real (sucursal "CAFETERIA", barra roja "Nube: error — branches …
  409 … branches_code_key" + "employees … 409 … employees_branch_id_fkey"): el motor es
  push-only y nunca manda `DELETE`, así que una sucursal borrada en local y recreada con el
  mismo `Code` nacía con un `Id` nuevo que chocaba con la fila que la nube conservaba, y de
  rebote todos sus empleados fallaban por llave foránea. Ahora, ante un 409/`23505` en
  `branches`, `SupabaseSyncBackgroundService.PushBranchesAsync` consulta los `Id`/`Code` de
  la nube, reasigna el `Id` local (`IBranchIdReconciler` → `EfBranchIdReconciler`: sucursal,
  empleados, dispositivos, marcaciones y la lista de sucursales de los usuarios, en una
  transacción) y reintenta en el mismo ciclo — sin consultar la nube en el caso normal. Si el
  `Id` de la nube ya lo ocupa otra sucursal local no toca nada y reporta el error (fusión
  manual). `SupabaseRestClient.UpsertBatchAsync` lanza ahora `SupabaseApiException`
  (subclase de `HttpRequestException` con el código SQLSTATE). El comando
  `fix-branch-id` del CLI queda como respaldo manual y usa el mismo reconciliador (además
  ahora repara `Users.BranchIds`, que antes omitía).
- **v1.61.0 — el reporte semanal trae las marcaciones que faltan antes de calcular.** Caso
  real: en Reportes toda la semana salía en rojo ("Descanso"/"Falta") porque
  `PayrollViewModel` solo leía la base local, y las marcaciones solo llegaban ahí por la
  descarga de la pestaña Dispositivos (en una instalación nueva, o con el reloj sin conectar,
  la semana quedaba vacía aunque Supabase ya tuviera las marcaciones). Ahora Actualizar,
  cambiar de semana y abrir Reportes primero (1) descargan del reloj si está conectado
  (`DevicesViewModel.DownloadForReportAsync`, reutiliza la descarga con dedupe y sube lo
  nuevo) y (2) traen de Supabase lo que falte (`SupabaseAttendancePullService`, paginado de
  1000 en 1000, tope de 20 s) conservando el `Id` de la nube (`Attendance.Restore`) para que
  la sincronización no las duplique. Se omiten las de un dispositivo no registrado en esa PC.
  Ninguna fuente puede romper el reporte: cada falla se resume en la barra de estado
  ("Marcaciones — reloj no conectado · nube: N nueva(s) de M").
- **v1.62.0 — vinculación automática de PINs leyendo los usuarios del reloj físico.** Causa
  real de que el reporte siguiera "todo en rojo" tras v1.61.0: las marcaciones sí existían,
  pero ningún PIN estaba vinculado a un empleado (`EmployeeDeviceMapping`); una marcación sin
  vínculo queda "pendiente de asignación" y `PayrollViewModel.GroupByResolvedEmployee` la
  descarta, así que el empleado sale con "Falta" toda la semana. Antes solo se podía vincular a
  mano (Empleados → Vincular pendientes, que además solo cubría PINs con marcaciones y sugería
  únicamente por Número = PIN). Ahora `DevicesViewModel.AutoLinkDeviceUsersAsync` lee TODOS los
  usuarios (PIN + nombre) de la memoria del reloj conectado (`DownloadUsersAsync`) y
  `DeviceUserEmployeeMatcher` (lógica pura, con pruebas) los empareja con los empleados: nombre
  igual (sin distinguir mayúsculas/acentos/espacios), nombre truncado por el reloj (~24
  caracteres, mín. 15, único candidato) y, como respaldo, Número = PIN. Solo vincula con
  EXACTAMENTE un candidato; homónimos, ambiguos o empleados que ya tienen otro PIN se reportan
  para vincular a mano. Crea los vínculos, concilia las marcaciones pendientes de ese PIN
  (`Attendance.ReconcileEmployee`) y sube lo nuevo a Supabase. Se ejecuta solo al pulsar
  Actualizar en Reportes con el reloj conectado (antes de descargar las marcaciones) y también
  con el botón "🔗 Vincular con empleados" del diálogo "Usuarios del reloj". La barra de estado
  de Reportes avisa además cuántas marcaciones de la semana siguen con PIN sin vincular.
- **v1.63.0 — Número de empleado = PIN pasa a ser el criterio principal de vinculación, y el
  reporte web muestra las marcaciones de la semana.** (1) `DeviceUserEmployeeMatcher` ahora
  revisa PRIMERO Número = PIN (ignorando ceros a la izquierda: `0114` = `114`) y solo después
  el nombre, porque el nombre en el reloj puede venir abreviado o distinto y el número no; gana
  sobre un nombre que apunte a otro empleado. (2) Al pulsar Actualizar en Reportes, la consulta
  a Supabase cubre desde la semana anterior a la mostrada hasta hoy (antes solo la semana en
  pantalla), para poder reportar "de la semana pasada a la fecha". (3) Dashboard web (Reporte
  de asistencia): en lugar de horas normales/extra acumuladas, cada empleado muestra la hora de
  **Entrada** (primera marcación del día) y **Salida** (última) de cada día, una tabla por
  semana lunes-domingo (`buildEmployeeWeekView`), con Descanso/Falta en los días sin marcación
  y el semáforo de puntualidad; la hoja es Carta horizontal y el Excel trae dos columnas
  (Entrada/Salida) por día. Se eliminaron `computeEmployeeHours`/`pairAndSumMs`/
  `capOpenUntilIso` (solo servían a las horas acumuladas). El resto de Reportes de escritorio
  (horas/nómina) no cambia.
- **v1.64.0 — CAUSA RAÍZ real del reporte en rojo: catálogo reemplazado.** Verificado en los
  datos de Supabase (18/09/2026): "Reemplazar catálogo" creó 54 empleados nuevos (`EMP-001…`,
  nombres cortos: "Adali") y dio de baja a los ~59 anteriores (número = PIN, nombres
  completos: "Adali Monserrat Tabanico Ramos", PIN 38), que siguieron siendo dueños de su PIN
  (`EmployeeDeviceMapping`) y de 723 de las 730 marcaciones desde el 7/sep; los 54 vigentes
  tenían 0 vínculos y 0 marcaciones, y el reporte solo lista vigentes. Además el número nuevo
  NO coincide con el PIN (Andrés Herrera = `EMP-007`, PIN 6), así que **el número ya no se usa
  como criterio principal** (v1.63.0 lo hacía; solo queda como respaldo con número solo-dígitos)
  ni se deduce el PIN del sufijo `EMP-NNN`. `DeviceUserEmployeeMatcher` se reescribió: cada PIN
  tiene todos los nombres con que se le reconoce (el del reloj y el del empleado dado de baja que
  hoy lo tiene) y se le asigna el empleado vigente cuyas palabras aparecen TODAS, en orden, en
  alguno de esos nombres ("Antony Beltran" ↔ "Antony Salvador Beltran Garcia"), en pasadas que
  resuelven encadenados (más específico gana: "Isaac Rojo" antes que "Isaac"; "Pablo" espera a que
  "Jose Pablo Rojo" tome el PIN 30) y nunca adivinan ante empate. `AutoLinkDeviceUsersAsync`
  ahora REASIGNA la fila del vínculo (`EmployeeDeviceMapping.ReassignEmployee`, mismo `Id`: la
  nube no recibe DELETE y su índice único (dispositivo, PIN) rechazaría una fila nueva) y pasa las
  marcaciones sin dueño o de un dueño dado de baja al vigente (`ListByDeviceAndPinAsync` +
  `Attendance.ReconcileEmployee`, que las marca para resubir). Corre en cada Actualizar de
  Reportes aunque el reloj NO esté conectado (basta con los vínculos ya guardados); conectado,
  además lee los usuarios del reloj. La importación desde la nube también pasa las marcaciones
  al dueño vigente del PIN. Prueba de regresión con el catálogo real completo (43 PINs
  resueltos, 8 empleados sin PIN que no se inventan, 3 que ya checan con su PIN real).
- **v1.64.1 — Reportes (escritorio): la tabla ya no corta encabezados ni textos.** Las columnas tenían anchos fijos menores que su encabezado ("Departamen", "Falta:", "Horas norn", "Neto a pa") y el texto descriptivo y la barra de estado (más larga desde que resume reloj/nube/PINs) no se ajustaban. Ahora las columnas usan Width="Auto" (con mínimo), Advertencias se ajusta en varias líneas y los dos textos hacen salto de línea.
- **v1.65.0 — PINs "de relleno" y tabla de Reportes que ya sale completa.** (1) Segunda causa
  real del reporte en rojo (verificada en Supabase tras v1.64.x): "Enviar empleados al reloj" dio a
  los 54 vigentes PINs NUEVOS (60–110) sin ninguna checada, mientras las 1262 marcaciones seguían en
  los PINs viejos (1–59), a nombre de empleados dados de baja; como todos los vigentes "ya tenían"
  PIN, el emparejador no tenía a quién asignar ("54 ya estaban, 57 sin coincidencia").
  `PinSlot.HasPunches` (`IAttendanceRepository.ListPinsWithAttendancesAsync`) distingue un PIN de
  relleno (dueño vigente y cero checadas): su dueño vuelve a estar disponible, se empareja por
  nombre con el PIN viejo y se le quita el de relleno (`PinMatchKind.Released`). Como todos los
  vínculos suben en UN solo lote y Supabase tiene índices únicos (dispositivo, empleado) y
  (dispositivo, PIN), el vínculo de relleno se borra PRIMERO en la nube
  (`SupabaseSyncBackgroundService.TryDeleteMappingsRemoteAsync`, tercera excepción a "solo empuja";
  si la nube está configurada y no responde se omite ese empleado hasta el siguiente Actualizar) y
  luego se traspasa la fila del PIN viejo (`ReassignEmployee`). El usuario "de relleno" queda
  huérfano en el reloj: borrarlo desde "Usuarios del reloj". (2) Tabla de Reportes: el tema global
  fija `RowHeight=36` y relleno de 12 px (cortaba la 2ª línea de las insignias de días y los
  encabezados), y el DataGrid ENCOGÍA las columnas de ancho fijo para meterlas en la ventana (la de
  Checadas quedaba en ~140 de 440 px, solo se veían 3 días). Ahora: altura automática, relleno
  chico, encabezados en dos líneas, cada columna con `MinWidth = Width` (si no cabe, barra
  horizontal en vez de cortar), los 7 días en una fila, Sucursal y Departamento en una columna de
  dos líneas e ISR/IMSS/Otro en una sola columna "Deducciones" (el desglose sigue en el diálogo y
  el CSV). Verificado con capturas de la app real sobre una base de ejemplo aislada (variable de
  entorno `RELOJCHECADOR_DATA_DIR`, ver `App.xaml.cs`). Dato a corregir en el catálogo: el
  departamento de 41 empleados es literalmente "CARWASHCARWASH".
- **v1.66.0 — "Actualizar" trae las marcaciones del reloj físico en el momento (Reportes y
  Asistencia).** Antes Reportes solo usaba el reloj si ya estaba conectado (si no, "reloj no
  conectado") y Asistencia solo recargaba la base local. Ahora `DevicesViewModel.DownloadForReportAsync`
  (1) conecta con el reloj si no lo está (`EnsureConnectedAsync`, tope de 20 s; si ya hay un intento
  en curso del reintento automático espera ese mismo; al vencer cancela el intento), (2) vincula los
  PINs con los empleados vigentes y (3) descarga todas las marcaciones que tiene el reloj, guardando
  las que faltan y subiéndolas a Supabase. Si no logra conectar devuelve el motivo y aun así hace lo
  que no necesita el reloj; nunca lanza. El botón Actualizar de Asistencia
  (`AttendanceViewModel.RefreshFromDeviceAsync`) hace lo mismo antes de recargar la lista y deja el
  resultado en la barra de estado ("Reloj — N nueva(s) de M leída(s)" o el motivo del fallo).
- **v1.67.0 — doble checada: se toma la PRIMERA entrada; y Advertencias ya no agranda las filas.**
  Ya con las checadas visibles (v1.66.0) el reporte mostraba horas absurdas y "31 con advertencias":
  con dos "Entradas" seguidas (p. ej. Adali 15/09: 07:53 Entrada, 15:29 Entrada, 15:53 Salida — el
  reloj F22/ID no tiene botones y `ShiftPunchTypeClassifier` no puede saber que la de las 15:29 es un
  toque repetido) `WorkedHoursCalculator.PairAndSum` descartaba la PRIMERA (la llegada real) y contaba
  solo 0:24 h ese día (1:53 h otro). Decisión explícita del usuario (18/09/2026): conservar la
  primera entrada e ignorar la repetida, igual que el reporte web (primera checada = entrada); la
  advertencia dice cuál se tomó y cuál se ignoró. Cambia la regla anterior (conservar la más
  reciente), por lo que las horas de semanas pasadas con dobles checadas suben. Además la columna
  Advertencias de Reportes envolvía TODO el texto en ~130 px y una fila con varias advertencias medía
  cientos de píxeles: ahora muestra 2 líneas con "…" y el detalle completo (una por línea) en el
  tooltip (`PayrollRow.WarningsTooltip`); Empleado y Advertencias se reparten el ancho sobrante.
- **v1.68.0 — Reporte de asistencia de la web como el de la PC, y el latido del reloj.**
  (1) Dashboard web: "Reporte de asistencia" abre una ventana que replica la pantalla Reportes de la
  PC — navegación por semana (lunes a domingo, empieza en la actual), buscador, filtros de sucursal
  y departamento, una fila por empleado ACTIVO con sus 7 días (insignia de color del semáforo con las
  horas, como la PC, y debajo la entrada–salida), Faltas, Horas normales/extra y, solo para cuentas
  Admin, Sueldo semanal, Pago horas extra, Total, Deducciones y Neto (decisión del usuario). El cálculo
  es un port exacto del de la PC en `dashboard/payroll-calc.js` (horas, descanso/falta, doble checada =
  primera entrada, advertencias, sueldo + horas extra, semáforo) con 30 pruebas en
  `tests/dashboard` (`node --test tests/dashboard/payroll-calc.test.mjs`). Los montos solo se
  DESCARGAN de Supabase para Admin (RLS no restringe esas columnas por rol; además
  `payroll_deductions` es legible por cualquier autenticado — ver nota de seguridad abajo).
  "Actualizar" recarga y, además, crea el mismo pedido de "🔄 Actualizar asistencias" para que la PC
  baje las marcaciones del reloj; al terminar la tabla se refresca sola. "Vista previa e imprimir" abre
  la hoja Carta horizontal con la MISMA tabla de nómina que la vista previa de la PC (logo, encabezado,
  Empleado…Neto a pagar) con Imprimir / Exp. Excel / Exp. PDF; CSV con las columnas del CSV de la PC
  más Número y Entrada–Salida por día. (2) Indicador "Conectado" de arriba: la web lo calcula con la
  última comunicación del reloj (≤ 5 min). Causa de que se quedara en "Desconectado" con la PC viva
  (sincronizando cada 10 s): el latido solo se guardaba en la descarga automática de 10 s, no en
  Actualizar, "Descargar asistencias" ni el pedido remoto, y `PersistAttendanceAsync` hacía un
  `SaveChanges` por CADA marcación —también las repetidas— (~1269 por ciclo). Ahora
  `DownloadAttendanceCoreAsync` registra el latido una vez por lote exitoso y el lote ya no guarda por
  marcación repetida. El indicador muestra por reloj su estado y hace cuánto no se comunica al pasar el
  ratón. Nota de seguridad: el registro público está abierto y auto-aprueba cuentas; Supabase permite a
  cualquier cuenta aprobada leer `employees` completo (incluye sueldo) y a cualquier autenticado
  `payroll_deductions` — la web ya no muestra montos a cuentas 'user', pero la API sí los entrega;
  conviene cerrar el registro o restringir esas políticas.

**Pendiente (bloqueado por decisiones o datos externos):**
- Navegación completa de la UI (Fase 3 del diseño visual — Sucursales, Empleados,
  Dispositivos, Asistencia y Reportes ya existen; falta el resto de secciones finales)
- Confirmar contra hardware real el significado de `PunchType` 2/3 (descansos) — hoy
  especulativo, ver `WorkedHoursCalculator`
- Cálculo AUTOMÁTICO de ISR/IMSS — descartado explícitamente por el usuario (ver "Hecho":
  la captura MANUAL de estos montos ya existe). Solo se retomaría si el usuario pide
  cálculo automático y aporta las tablas/reglas vigentes que quiere aplicar
- Incidencias de nómina (faltas, permisos, vacaciones) — resto de Fases 5-6
- Razón social real para el instalador (el ícono/logotipo de marca "GalaCheck" ya se
  resolvió — ver "Hecho")

## Convenciones

- Cada tarea completada se compila y prueba antes del siguiente paso (`dotnet build` +
  `dotnet test` en verde) y queda en un commit propio con su justificación.
- Nunca se inventan comandos/protocolos de fabricantes de dispositivos ni reglas fiscales
  sin confirmarlas explícitamente.
