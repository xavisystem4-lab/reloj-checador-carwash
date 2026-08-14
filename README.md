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
