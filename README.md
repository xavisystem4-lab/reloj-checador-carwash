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
  `src/RelojChecador.Infrastructure.Cloud/README.md`); el Dashboard web de reportes
  todavía no existe (ver "Pendiente" abajo)
- **Serilog** para logging estructurado
- **xUnit** para pruebas unitarias/integración

## Estructura

```
src/RelojChecador.Domain/               Entidades y reglas de negocio, sin dependencias externas
src/RelojChecador.Application/          Casos de uso, contratos (IAttendanceDeviceAdapter, repositorios), Result/Error
src/RelojChecador.Infrastructure.Data/  EF Core + SQLite, repositorios, migraciones
src/RelojChecador.Infrastructure.Devices/ Adaptadores de dispositivos (Simulator listo; ZKTeco pendiente del SDK)
src/RelojChecador.Infrastructure.Cloud/ Motor de sincronización push-only con Supabase (ver su propio README)
src/RelojChecador.Infrastructure.Security/ Windows Credential Manager (solo compila en Windows)
src/RelojChecador.Infrastructure.Logging/ Configuración de Serilog
src/RelojChecador.WPF/                  Aplicación de escritorio (composition root, ViewModels, Views)
tests/                                  Pruebas unitarias/integración por capa
tools/RelojChecador.DeviceSimulator/    Simulador standalone del protocolo del reloj (pendiente de contenido)
installer/                              Script de Inno Setup + guía para generar el instalador de Windows
supabase/                               Migraciones SQL versionadas (ya aplicadas al proyecto real); Edge Functions pendiente
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
  ciclo, offline-first (nunca tumba la app sin internet) — ver
  `src/RelojChecador.Infrastructure.Cloud/README.md` para cómo activarla en una instalación

**Pendiente (bloqueado por decisiones o datos externos):**
- Confirmar `ZKTecoDeviceAdapter` contra el F22/ID real en Windows (nombres de método y
  códigos del SDK como el mapeo de `dwVerifyMode` siguen la convención más citada de la
  comunidad, sin verificar todavía contra hardware — ver comentarios en la clase)
- Confirmar la sincronización con Supabase con la `service_role` key real corriendo en
  Windows (el esquema/RLS ya se verificó con la `anon` key; falta el flujo completo con
  datos reales — no se pudo probar en esta sesión, hecha desde macOS)
- Dashboard web de reportes (Supabase Auth + lectura de las tablas ya sincronizadas)
- Módulo de auto-actualización de la app (verificar versión disponible y actualizar sin
  reinstalar a mano)
- Navegación completa de la UI (Fase 3 del diseño visual — hoy solo hay una ventana mínima
  de prueba, no las pantallas finales)
- Reportes, auditoría, incidencias, nómina (Fases 5-6)
- Razón social real y logotipo/icono para el instalador y la app

## Convenciones

- Cada tarea completada se compila y prueba antes del siguiente paso (`dotnet build` +
  `dotnet test` en verde) y queda en un commit propio con su justificación.
- Nunca se inventan comandos/protocolos de fabricantes de dispositivos ni reglas fiscales
  sin confirmarlas explícitamente.
