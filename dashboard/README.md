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

Puedes repetir esto para dar acceso a varias personas — cualquier cuenta creada así ve
los reportes de **todas** las sucursales (no hay todavía separación de acceso por
sucursal en el Dashboard; si algún día hace falta, se resuelve con una tabla de
permisos + políticas RLS más finas, no está construido en esta versión).

## Recomendado: cerrar el registro público

Por defecto, Supabase permite que cualquiera se registre por su cuenta llamando
directamente a la API de autenticación (no a través de este sitio, que no lo expone,
pero sí con una llamada HTTP directa). Para cerrar esa puerta:

1. [Authentication → Providers → Email](https://supabase.com/dashboard/project/vkvlucpjgvqrlvevcimq/auth/providers) en el dashboard de Supabase.
2. Apaga **"Allow new users to sign up"**.

Con esto, solo las cuentas que tú crees manualmente (paso anterior) pueden entrar.

## Qué muestra

- **Filtros**: sucursal, rango de fechas, búsqueda por nombre de empleado o PIN.
- **KPIs**: marcaciones en el rango, empleados distintos, dispositivos activos,
  marcaciones sin vincular a un empleado.
- **Tabla**: fecha/hora (en la zona horaria de tu navegador), empleado (o "PIN X · sin
  vincular" si el dispositivo todavía no tiene un `EmployeeDeviceMapping` para ese PIN),
  sucursal, dispositivo, método de verificación, tipo (entrada/salida).
- **Exportar CSV** del reporte visible (respeta los filtros aplicados).
- Se actualiza solo cada 10 segundos mientras la pestaña está abierta (la app de
  escritorio también sube cada ~10s, así que los datos aparecen aquí casi al instante
  después de la marcación real).

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
