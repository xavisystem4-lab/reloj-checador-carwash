# Dashboard de Reportes — Reloj Checador

Sitio estático (HTML + CSS + JS puro, sin build ni framework) desplegado en Netlify:

**https://reloj-checador-carwash.netlify.app**

Lee directo de Supabase (proyecto dedicado `reloj-checador-carwash`,
`vkvlucpjgvqrlvevcimq`) usando el cliente JS oficial (`@supabase/supabase-js`, vía CDN
`esm.sh`, sin instalar nada). Es de **solo lectura**: la migración `initial_schema` solo
tiene políticas de RLS para `SELECT`, nunca para `INSERT`/`UPDATE`/`DELETE` — nadie puede
modificar datos del negocio desde el navegador, sin importar qué credenciales use. Todo
lo que escribe pasa por la app de escritorio de cada sucursal (ver
`src/RelojChecador.Infrastructure.Cloud/README.md`).

## Cómo crear el primer usuario del Dashboard (una sola vez)

A propósito **no hay formulario de "crear cuenta"** en este sitio — así nunca hay una vía
de auto-registro abierta para leer datos de asistencia del negocio. Las cuentas se crean
directo en el panel de Supabase:

1. Entra a [Authentication → Users](https://supabase.com/dashboard/project/vkvlucpjgvqrlvevcimq/auth/users)
   en el dashboard de Supabase.
2. Clic en **"Add user"** → **"Create new user"**.
3. Escribe el correo y la contraseña que va a usar la persona que revisa los reportes
   (tú decides quién — el dueño del carwash, un gerente, etc.). Márcalo como
   "Auto Confirm User" para que pueda entrar de inmediato sin confirmar por correo.
4. Esa misma persona entra a https://reloj-checador-carwash.netlify.app con ese correo y
   contraseña.

Ese primer paso manual es **solo para la primera cuenta** (nadie puede entrar todavía, así
que nadie puede usar el panel de abajo). A partir de ahí, esa persona ya puede invitar a
las demás desde el propio Dashboard — botón **"👤 Usuarios"** en la barra superior.

Cualquier cuenta (creada manualmente o invitada desde el panel) ve los reportes de
**todas** las sucursales (no hay todavía separación de acceso por sucursal en el
Dashboard; si algún día hace falta, se resuelve con una tabla de permisos + políticas RLS
más finas, no está construido en esta versión).

## Panel "Usuarios" (invitar / editar nombre / quitar acceso)

Botón **"👤 Usuarios"** en la barra superior, visible para cualquiera que ya tenga sesión
iniciada — el modelo de confianza es deliberadamente simple (un solo negocio pequeño, no
un sistema con roles jerárquicos): cualquier persona con acceso de lectura también puede
invitar o quitar acceso a otras.

- **Invitar**: pide nombre + correo, Supabase le manda a esa persona un correo con un
  enlace para que **ella misma** defina su contraseña — este sitio nunca genera ni ve
  contraseñas de nadie.
- **Editar nombre**: el lápiz ✏️ junto a cualquier usuario; cada persona también puede
  cambiar el suyo propio con el botón de su nombre en la esquina superior derecha.
- **Quitar acceso**: el bote de basura 🗑️ (no aparece junto a tu propia cuenta, para no
  poder auto-eliminarte por accidente).

Todo esto corre a través de una Edge Function de Supabase, `manage-users`
(`supabase/functions/manage-users/index.ts`) — es la única pieza de este proyecto que usa
la `service_role` key, y lo hace **del lado del servidor** (Supabase la inyecta como
variable de entorno dentro de la función; nunca viaja al navegador). El sitio estático
solo llama a la función con `supabase.functions.invoke(...)`, que adjunta el JWT de la
sesión actual — la función (desplegada con `verify_jwt=true`) rechaza cualquier llamada
sin una sesión válida.

Redesplegar la función tras editarla:

```bash
npx supabase functions deploy manage-users --project-ref vkvlucpjgvqrlvevcimq
```

(o usando la herramienta MCP de Supabase, como se hizo la primera vez).

## Recomendado: cerrar el registro público

Por defecto, Supabase permite que cualquiera se registre por su cuenta llamando
directamente a la API de autenticación (no a través de este sitio, que no lo expone,
pero sí con una llamada HTTP directa). Para cerrar esa puerta:

1. [Authentication → Providers → Email](https://supabase.com/dashboard/project/vkvlucpjgvqrlvevcimq/auth/providers) en el dashboard de Supabase.
2. Apaga **"Allow new users to sign up"**.

Con esto, solo las cuentas que tú crees manualmente (paso anterior) pueden entrar.

## Qué muestra

- **Badge de conexión** en el header (arriba, visible también en móvil sin hacer
  scroll): "Conectado" en verde si al menos un reloj checador está en vivo (mismo
  criterio de 5 minutos que la fila de pills por dispositivo, más abajo), "Desconectado"
  si no.
- **Filtros**: sucursal, rango de fechas, búsqueda por nombre de empleado o PIN.
- **KPIs**: marcaciones en el rango, empleados distintos, dispositivos activos,
  marcaciones sin vincular a un empleado.
- **Tabla**: fecha/hora, empleado (o "PIN X · sin vincular" si el dispositivo todavía no
  tiene un `EmployeeDeviceMapping` para ese PIN), sucursal, dispositivo, método de
  verificación, tipo (entrada/salida).
- **Exportar CSV** del reporte visible (respeta los filtros aplicados).
- Se actualiza solo cada 10 segundos mientras la pestaña está abierta (la app de
  escritorio también sube cada ~10s como respaldo periódico, ver
  `SupabaseSyncOptions.IntervalSeconds` en el repo principal — además, cada marcación
  nueva dispara su propia sincronización inmediata sin esperar ese ciclo, ver
  `DevicesViewModel.PersistAndTriggerSyncAsync`).

### "🔄 Actualizar asistencias" — sincronización remota bajo demanda

Botón junto a "Actualizar"/"Exportar CSV" que le pide al sistema local de la sucursal que
se conecte al reloj checador AHORA MISMO, descargue lo más reciente y lo suba, en vez de
esperar el próximo ciclo automático. Nunca hay una conexión directa de este sitio hacia la
PC del negocio — sería exponerla a Internet, justo lo que este diseño evita — el flujo es
100% mediante una tabla intermedia en Supabase que ambos lados consultan/escriben, cada
uno saliendo hacia afuera, nunca recibiendo conexiones entrantes:

1. Este botón inserta una fila en `public.sync_requests` (permitido por RLS solo para
   `INSERT`/`SELECT` de `authenticated` — la primera tabla del esquema donde el navegador
   puede escribir algo, ver la migración `20260814060000_add_sync_requests.sql` para el
   razonamiento completo).
2. La app de escritorio de la sucursal la CONSULTA periódicamente (cada ~10s, ver
   `RemoteSyncRequestPollingService` en el repo principal) — nunca al revés. Si la PC está
   apagada o sin internet, la fila simplemente se queda `pending` hasta que vuelva a estar
   en línea y la recoja sola, sin que nadie tenga que reintentar nada a mano.
3. Al recogerla, la marca `in_progress`, conecta con el reloj (reutiliza exactamente los
   mismos pasos que los botones "Conectar"/"Descargar asistencias" de la app), sube lo
   nuevo a Supabase, y termina marcándola `completed` (con un resumen) o `failed` (con el
   motivo) — solo `service_role` (la app de escritorio) puede escribir esos campos, el
   navegador nunca puede auto-marcarse "completado".
4. Este archivo hace polling de esa misma fila cada 3s mientras está activa, mostrando
   "Solicitud enviada…" → "Sincronizando…" → "✅ resumen" / "❌ error", y al completarse
   refresca la lista y los KPIs solo — no hace falta recargar la página.

Duplicados: como mucho una solicitud `pending`/`in_progress` a la vez en toda la tabla
(índice único parcial en Postgres) — un segundo clic (o una segunda pestaña) se engancha a
la que ya está en curso en vez de crear otra. Si se cierra la pestaña con una solicitud
todavía activa, al volver a abrir el Dashboard se retoma sola.

### Zona horaria de `attendances.timestamp_utc` — IMPORTANTE, no es UTC real

A pesar del nombre de la columna, `timestamp_utc` **no** es un instante UTC real: el
reloj checador entrega su propia hora local (Mexicali) y todo el sistema (app de
escritorio, SQLite, Supabase) la guarda tal cual, solo etiquetada como UTC sin
convertirla — decisión deliberada documentada en `Attendance.Create` del repo principal
("todo el negocio opera en una sola zona horaria, no hay conversión real").

Por eso `app.js` usa `formatAttendanceDateTime()` (con `timeZone: 'UTC'` explícito) en
vez del `formatDateTime()` normal para mostrar marcaciones: fuerza a que el navegador
muestre los componentes crudos del valor guardado, **sin** restarle además su propio
huso horario. Usar `toLocaleString()` sin ese parámetro (como se hacía antes de
corregir este bug) provoca un doble desfase — la hora ya local del reloj se convierte
una segunda vez, adelantándose o atrasándose por el offset del navegador (en Mexicali,
~7 horas). El filtro Desde/Hasta de `loadReport()` tiene el mismo cuidado: construye el
rango con el string tal cual (`${valor}T00:00:00.000Z`) en vez de pasar por
`new Date(...).toISOString()`, que también reinterpretaría la fecha como hora local del
navegador.

`formatDateTime()` (sin sufijo) se conserva para `last_sign_in_at` de Supabase Auth —
ese sí es un timestamp UTC genuino generado por el servidor, y necesita la conversión
normal a hora local para mostrarse correctamente.

## Desplegar cambios

El sitio vive en Netlify, proyecto `reloj-checador-carwash` (`d4473e62-f94c-4789-bed5-636c49720eeb`).
Para actualizarlo tras editar estos archivos:

```bash
cd dashboard
npx -y @netlify/mcp@latest --site-id d4473e62-f94c-4789-bed5-636c49720eeb --proxy-path "<token fresco>"
```

El `--proxy-path` expira y hay que regenerarlo cada vez (ver flujo de deploy del
proyecto principal). Se corre **desde dentro de `dashboard/`**, no desde la raíz del
repo — si se corre desde la raíz intenta subir los ~700 MB del monorepo .NET completo
(carpetas `bin`/`obj` de compilación incluidas) y falla.

## Seguridad: qué es seguro de commitear aquí y qué no

`app.js` tiene la URL del proyecto y la **AnonKey** de Supabase escritas directo en el
código, a la vista de cualquiera que abra las herramientas de desarrollador del
navegador. Esto es intencional y seguro: la AnonKey está diseñada para ser pública — la
protección real es RLS (Row Level Security) en la base de datos, no que la clave esté
"escondida". Nunca se debe poner aquí la `service_role` key (esa sí es secreta) — ver
`src/RelojChecador.Infrastructure.Cloud/README.md` para dónde vive esa.

## Verificado (sin necesitar una cuenta de usuario real)

- El sitio carga (`200`), sirve `app.js`/`styles.css` correctamente, y el resto del
  repositorio (.NET) **no** quedó expuesto por accidente (`/RelojChecador.slnx` y
  cualquier ruta de `src/` dan `404`).
- El esquema/RLS ya se probó por separado contra el proyecto real con la `anon` key (ver
  `src/RelojChecador.Infrastructure.Cloud/README.md`): lectura sin sesión → `200` vacío;
  escritura → `401` bloqueado.
- **No se pudo probar el flujo de login real** (correo + contraseña → ver los reportes)
  porque eso requeriría crear una cuenta de Supabase Auth y escribir una contraseña en
  el formulario — algo que, por diseño, nunca se hace automáticamente. Falta que el
  usuario cree su primera cuenta (ver arriba) y confirme que puede entrar.
