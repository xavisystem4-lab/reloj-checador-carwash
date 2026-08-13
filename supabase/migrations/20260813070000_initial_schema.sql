-- Esquema espejo del dominio local (RelojChecador.Domain) para sincronización desde la
-- app de escritorio (self-contained WPF, service_role key vía Windows Credential
-- Manager) y lectura de reportes desde el Dashboard web (usuarios autenticados,
-- solo lectura).
--
-- Ya aplicada al proyecto real (reloj-checador-carwash, vkvlucpjgvqrlvevcimq) el
-- 2026-08-13 vía el MCP de Supabase — este archivo es la versión versionada en el repo
-- de esa misma migración, para que el esquema quede documentado en git y sea
-- reproducible en otro proyecto de Supabase si algún día hiciera falta.
--
-- Decisión de seguridad explícita: la app de escritorio usa la service_role key (que
-- ignora RLS) para escribir, en vez de un usuario autenticado con políticas de INSERT/
-- UPDATE. Se acepta porque este proyecto de Supabase es dedicado y aislado (un solo
-- negocio, sin otros tenants) — el radio de exposición si esa clave se filtrara queda
-- limitado a los datos de este carwash, no a otros clientes. La clave vive en Windows
-- Credential Manager (RelojChecador.Infrastructure.Security), nunca en texto plano en
-- el código ni en la base local SQLite. Ver src/RelojChecador.Infrastructure.Cloud/README.md.
--
-- IDs: se reutiliza el mismo uuid que la entidad tiene en SQLite local — la sincronización
-- es un upsert por Id, no genera un Id nuevo en la nube.

create table public.branches (
  id uuid primary key,
  code text not null unique,
  name text not null,
  legal_entity_name text,
  address text,
  time_zone_id text not null,
  manager_employee_id uuid,
  is_active boolean not null default true,
  created_at_utc timestamptz not null,
  updated_at_utc timestamptz not null,
  concurrency_token uuid not null
);
comment on table public.branches is 'Espejo de RelojChecador.Domain.Branches.Branch. Sincronizada por la app de escritorio (service_role); solo lectura para el Dashboard.';

create table public.employees (
  id uuid primary key,
  number text not null unique,
  full_name text not null,
  branch_id uuid not null references public.branches(id),
  department text,
  position text,
  hire_date date not null,
  status text not null,
  phone text,
  email text,
  rfc text,
  curp text,
  nss text,
  created_at_utc timestamptz not null,
  updated_at_utc timestamptz not null,
  concurrency_token uuid not null
);
comment on table public.employees is 'Espejo de RelojChecador.Domain.Employees.Employee.';
create index employees_branch_id_idx on public.employees(branch_id);

create table public.devices (
  id uuid primary key,
  name text not null,
  brand text not null,
  model text not null,
  serial_number text,
  mac_address text,
  ip_address text not null,
  tcp_port integer not null,
  machine_number text,
  branch_id uuid not null references public.branches(id),
  time_zone_id text not null,
  status text not null,
  last_communication_at_utc timestamptz,
  last_sync_at_utc timestamptz,
  firmware_version text,
  capabilities integer not null default 0,
  created_at_utc timestamptz not null,
  updated_at_utc timestamptz not null,
  concurrency_token uuid not null
);
comment on table public.devices is 'Espejo de RelojChecador.Domain.Devices.Device. A propósito NO incluye CredentialReference: es una clave hacia Windows Credential Manager de la máquina de origen, sin significado fuera de ella.';
create index devices_branch_id_idx on public.devices(branch_id);

create table public.employee_device_mappings (
  id uuid primary key,
  employee_id uuid not null references public.employees(id),
  device_id uuid not null references public.devices(id),
  device_user_pin text not null,
  enrolled_at_utc timestamptz not null
);
comment on table public.employee_device_mappings is 'Espejo de RelojChecador.Domain.EmployeeDeviceMappings.EmployeeDeviceMapping.';
create index employee_device_mappings_employee_id_idx on public.employee_device_mappings(employee_id);
create index employee_device_mappings_device_id_idx on public.employee_device_mappings(device_id);

create table public.attendances (
  id uuid primary key,
  device_id uuid not null references public.devices(id),
  branch_id uuid not null references public.branches(id),
  employee_id uuid references public.employees(id),
  device_user_pin text not null,
  timestamp_utc timestamptz not null,
  verify_method text not null,
  punch_type integer,
  raw_payload text not null,
  created_at_utc timestamptz not null,
  updated_at_utc timestamptz not null,
  concurrency_token uuid not null
);
comment on table public.attendances is 'Espejo de RelojChecador.Domain.Attendances.Attendance — la tabla principal que consulta el Dashboard de reportes.';
create index attendances_branch_id_idx on public.attendances(branch_id);
create index attendances_employee_id_idx on public.attendances(employee_id);
create index attendances_timestamp_utc_idx on public.attendances(timestamp_utc);

create table public.app_users (
  id uuid primary key,
  username text not null unique,
  full_name text,
  email text,
  role text not null,
  is_active boolean not null default true,
  branch_ids uuid[] not null default '{}',
  created_at_utc timestamptz not null,
  updated_at_utc timestamptz not null,
  concurrency_token uuid not null
);
comment on table public.app_users is 'Espejo de RelojChecador.Domain.Identity.User — usuarios de la app de escritorio (NO son cuentas de auth.users de Supabase; el Dashboard usa su propio esquema de autenticación).';

-- RLS: habilitada en todo. La app de escritorio (service_role) ignora RLS por diseño de
-- Supabase, así que no necesita política propia. El Dashboard (usuarios autenticados vía
-- Supabase Auth) solo puede LEER — nunca hay política de INSERT/UPDATE/DELETE para
-- 'authenticated': todo lo que escribe pasa por la app de escritorio, nunca por el navegador.
alter table public.branches enable row level security;
alter table public.employees enable row level security;
alter table public.devices enable row level security;
alter table public.employee_device_mappings enable row level security;
alter table public.attendances enable row level security;
alter table public.app_users enable row level security;

create policy "authenticated_read_branches" on public.branches for select to authenticated using (true);
create policy "authenticated_read_employees" on public.employees for select to authenticated using (true);
create policy "authenticated_read_devices" on public.devices for select to authenticated using (true);
create policy "authenticated_read_employee_device_mappings" on public.employee_device_mappings for select to authenticated using (true);
create policy "authenticated_read_attendances" on public.attendances for select to authenticated using (true);
-- app_users NO tiene política de lectura para 'authenticated': son cuentas internas de la
-- app de escritorio, no un reporte — el Dashboard no necesita verlas.
