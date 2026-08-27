-- Roles y aprobación de cuentas del Dashboard web.
--
-- Ya aplicada al proyecto real (reloj-checador-carwash, vkvlucpjgvqrlvevcimq) el
-- 2026-08-27 vía el MCP de Supabase — este archivo es la versión versionada en el repo de
-- esa misma migración (ver dashboard/README.md, sección "Roles y aprobación de cuentas").
--
-- Contexto: hasta esta migración, cualquier cuenta con sesión iniciada podía invitar/
-- editar/eliminar otras cuentas (sin jerarquía). Se introduce un rol (admin/user) y un
-- estado de aprobación (pending/approved/rejected) para que (a) los registros públicos
-- nuevos (ver signup-form en dashboard/) queden bloqueados hasta que un Admin los
-- apruebe, y (b) solo un Admin pueda gestionar usuarios.

-- Perfiles del Dashboard web: rol (admin/user) y estado de aprobación (pending/approved/
-- rejected). email/full_name NO se duplican aquí a propósito: la Edge Function manage-users
-- ya los obtiene de auth.admin.listUsers()/user_metadata (como hoy hace la acción "list"),
-- así que profiles se queda minimalista y siempre consistente con auth.users.
create table public.profiles (
  id uuid primary key references auth.users(id) on delete cascade,
  role text not null default 'user' check (role in ('admin', 'user')),
  status text not null default 'pending' check (status in ('pending', 'approved', 'rejected')),
  approved_by uuid references auth.users(id) on delete set null,
  approved_at timestamptz,
  created_at timestamptz not null default now()
);
comment on table public.profiles is 'Rol y estado de aprobación de cada cuenta de auth.users que puede entrar al Dashboard web. Creada automáticamente por el trigger on_auth_user_created al registrarse (status=pending por defecto); la Edge Function manage-users (service_role) es la única que puede aprobarla/rechazarla/cambiarle el rol.';

alter table public.profiles enable row level security;

-- Cada quien lee su propio perfil (para saber si ya fue aprobado); los Admin aprobados
-- leen todos (para el panel de "Usuarios"/"Pendientes"). Sin políticas de insert/update/
-- delete para 'authenticated': esas escrituras solo las hace el trigger (creación) o la
-- Edge Function con service_role (aprobar/rechazar/rol/eliminar) — mismo patrón que
-- sync_requests con sus campos de estado.
create policy "profiles_read_own_or_admin_all" on public.profiles for select to authenticated
  using (
    id = auth.uid()
    or exists (select 1 from public.profiles p where p.id = auth.uid() and p.role = 'admin' and p.status = 'approved')
  );

-- Al crearse una cuenta en auth.users (auto-registro público vía signUp, o alta manual
-- desde la Edge Function con auth.admin.createUser), se crea automáticamente su fila en
-- profiles con status='pending' por defecto. SECURITY DEFINER porque el usuario recién
-- creado todavía no tiene permiso propio para insertar en profiles.
create function public.handle_new_auth_user()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
  insert into public.profiles (id) values (new.id) on conflict (id) do nothing;
  return new;
end;
$$;

create trigger on_auth_user_created
  after insert on auth.users
  for each row execute function public.handle_new_auth_user();

-- Refuerzo defensivo en las tablas de negocio: ya no basta con estar "authenticated", hace
-- falta además estar 'approved' en profiles. Así, aunque alguien "pending" tenga una sesión
-- técnica válida (p. ej. si la confirmación de correo está desactivada y signUp devuelve
-- sesión de inmediato), RLS le sigue negando cualquier dato real -- el bloqueo del
-- frontend (ver dashboard/app.js) es la primera línea, esta es la de respaldo.
drop policy "authenticated_read_branches" on public.branches;
create policy "approved_read_branches" on public.branches for select to authenticated
  using (exists (select 1 from public.profiles p where p.id = auth.uid() and p.status = 'approved'));

drop policy "authenticated_read_employees" on public.employees;
create policy "approved_read_employees" on public.employees for select to authenticated
  using (exists (select 1 from public.profiles p where p.id = auth.uid() and p.status = 'approved'));

drop policy "authenticated_read_devices" on public.devices;
create policy "approved_read_devices" on public.devices for select to authenticated
  using (exists (select 1 from public.profiles p where p.id = auth.uid() and p.status = 'approved'));

drop policy "authenticated_read_employee_device_mappings" on public.employee_device_mappings;
create policy "approved_read_employee_device_mappings" on public.employee_device_mappings for select to authenticated
  using (exists (select 1 from public.profiles p where p.id = auth.uid() and p.status = 'approved'));

drop policy "authenticated_read_attendances" on public.attendances;
create policy "approved_read_attendances" on public.attendances for select to authenticated
  using (exists (select 1 from public.profiles p where p.id = auth.uid() and p.status = 'approved'));

drop policy "authenticated_read_sync_requests" on public.sync_requests;
create policy "approved_read_sync_requests" on public.sync_requests for select to authenticated
  using (exists (select 1 from public.profiles p where p.id = auth.uid() and p.status = 'approved'));

drop policy "authenticated_insert_sync_requests" on public.sync_requests;
create policy "approved_insert_sync_requests" on public.sync_requests for insert to authenticated
  with check (
    requested_by_user_id = auth.uid()
    and exists (select 1 from public.profiles p where p.id = auth.uid() and p.status = 'approved')
  );

-- Seed: Javier Galaviz y Nathalia Trujillo ya tienen cuenta (creadas antes de este cambio)
-- y son los únicos Admin iniciales -- se marcan directo, sin recrear sus cuentas. IDs
-- reales verificados por separado contra auth.users, no generados por esta migración.
insert into public.profiles (id, role, status, approved_at) values
  ('fbba424e-71e4-4573-b037-835c1d13965e', 'admin', 'approved', now()), -- Javier Galaviz (softgalaweb@gmail.com)
  ('3c690028-9594-478c-88f5-f6836bd6c871', 'admin', 'approved', now())  -- Nathalia Trujillo (rh.driveincarwash@gmail.com)
on conflict (id) do update set role = 'admin', status = 'approved', approved_at = now();
