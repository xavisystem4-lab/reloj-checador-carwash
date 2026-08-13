# Sincronización con Supabase

Motor de sincronización **push-only** (v1): la app de escritorio es la única fuente de
verdad y empuja sus cambios hacia Supabase; el Dashboard web (pendiente, Fase siguiente)
solo lee. No hay sincronización en sentido contrario todavía.

Proyecto de Supabase dedicado a este negocio: `reloj-checador-carwash`
(`https://vkvlucpjgvqrlvevcimq.supabase.co`), separado de cualquier otro proyecto de
Supabase que el usuario tenga para otros negocios.

## Cómo activarla en una instalación (una sola vez, en Windows)

La app funciona 100% local sin esto — es opcional, no bloquea nada. Para activarla:

1. En el [dashboard de Supabase](https://supabase.com/dashboard/project/vkvlucpjgvqrlvevcimq/settings/api-keys),
   copia la **`service_role` key** (Settings → API → Project API keys). Es una clave
   secreta con acceso total de escritura — nunca la compartas, nunca la pegues en un
   chat, nunca la subas a git.
2. Crea (o edita) el archivo `%LocalAppData%\RelojChecador\appsettings.Local.json` en la
   máquina de esa sucursal, con este contenido:
   ```json
   {
     "Supabase": {
       "ServiceRoleKey": "PEGA_AQUI_LA_SERVICE_ROLE_KEY"
     }
   }
   ```
3. Reinicia la app. En los logs (`%LocalAppData%\RelojChecador\logs\`) debe aparecer
   `Sincronización con Supabase activa.` en vez de `...deshabilitada`.

Este archivo vive fuera de la carpeta de instalación y fuera del repositorio a propósito
— sobrevive a reinstalaciones/actualizaciones (mismo criterio que la base SQLite local y
los logs, ver `installer/RelojChecador.iss`, sección `[UninstallDelete]`) y nunca se
commitea (ver `.gitignore`).

## Por qué `service_role` y no un usuario autenticado

Decisión de seguridad explícita, documentada también en la migración SQL
(`initial_schema`): la app de escritorio usa la `service_role` key (que en Supabase
ignora Row Level Security) en vez de autenticarse como un usuario con políticas de
INSERT/UPDATE. Se acepta este riesgo porque el proyecto de Supabase es dedicado y
aislado — un solo negocio, sin otros tenants — así que el radio de exposición si esa
clave se filtrara queda limitado a los datos de este carwash.

## Qué se sincroniza y cómo

- **Branches, Employees, Devices, EmployeeDeviceMappings, Users**: se reenvían completos
  en cada ciclo (son tablas chicas — el costo de red es insignificante).
- **Attendances**: puede crecer mucho con el tiempo. Se sincroniza de forma incremental
  con un cursor (`ISyncCursorStore`, tabla local `SyncCursors`) que recuerda hasta qué
  `UpdatedAtUtc` ya se envió, y se drena en lotes de 500 dentro del mismo ciclo — así,
  tras una temporada sin internet, la cola pendiente no tarda horas en ponerse al día.
- Todo se sube por **upsert idempotente** (`on_conflict=id`, `resolution=merge-duplicates`):
  reenviar la misma fila (p. ej. tras un reintento de red) nunca duplica, solo sobreescribe
  con el mismo valor.
- Sin conexión a internet es un caso **esperado** (operación offline-first): cada ciclo va
  en su propio `try/catch`, se registra en el log como advertencia y se reintenta en el
  siguiente ciclo (cada `IntervalSeconds`, 5s por defecto — configurable en
  `appsettings.json` sin recompilar) — nunca tumba la app.

## Verificado sin necesitar la `service_role` key

El esquema y las políticas de RLS ya se probaron contra el proyecto real (vía `curl` con
la `anon` key, que sí es segura de manejar): una lectura sin sesión devuelve `200` con
lista vacía, y un intento de escritura devuelve `401` con
`"new row violates row-level security policy"`. Lo que falta verificar es el flujo
completo (`service_role` key real → escritura real → aparece en Supabase) corriendo la
app en Windows con datos reales — no se pudo probar en esta sesión de desarrollo (hecha
desde macOS, sin la clave real).
