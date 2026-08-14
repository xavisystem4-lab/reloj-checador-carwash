-- Solicitudes de sincronización remota disparadas desde el Dashboard ("Actualizar
-- asistencias") — la app de escritorio (service_role) las consulta periódicamente (ver
-- RemoteSyncRequestPollingService en el repo principal) y actualiza su estado. Nunca hay
-- conexión entrante hacia la PC local: el flujo es 100% saliente (polling) en ambos
-- sentidos — el Dashboard escribe la solicitud en la nube, el sistema local la recoge
-- cuando le toca su ciclo, nunca al revés.
--
-- Excepción deliberada y acotada a la regla general del esquema ("todo lo que escribe pasa
-- por la app de escritorio, nunca por el navegador", ver initial_schema.sql): esta es la
-- PRIMERA tabla donde el Dashboard (usuario autenticado) puede escribir — pero solo puede
-- INSERTAR la solicitud (con su propio user_id) y LEER su estado; nunca puede marcarla
-- in_progress/completed/failed. Esos únicos tres campos de estado los escribe siempre el
-- sistema local real (service_role, que ignora RLS) — así el registro de auditoría es
-- confiable: si una fila dice "completed", de verdad la procesó el sistema local, no un
-- usuario del navegador marcándola a mano.
create table public.sync_requests (
  id uuid primary key default gen_random_uuid(),
  status text not null default 'pending' check (status in ('pending', 'in_progress', 'completed', 'failed')),
  requested_by_user_id uuid references auth.users(id) on delete set null,
  requested_by_email text,
  requested_at_utc timestamptz not null default now(),
  started_at_utc timestamptz,
  completed_at_utc timestamptz,
  result_summary text,
  error_message text
);
comment on table public.sync_requests is 'Solicitudes de sincronización remota ("Actualizar asistencias" en el Dashboard). Auditoría: cada fila queda como historial permanente, nunca se borra.';

create index sync_requests_status_idx on public.sync_requests(status);

-- Prevención de solicitudes duplicadas a nivel de base de datos: como mucho UNA solicitud
-- pending/in_progress a la vez en toda la tabla. Encaja con que hoy solo hay un dispositivo
-- real en producción (ver DevicesViewModel — "revisar esto antes de soportar varios
-- relojes conectados simultáneamente"); si algún día se soportan varios relojes a la vez,
-- este índice es el primer lugar a revisar para volverlo por dispositivo en vez de global.
create unique index sync_requests_one_active_idx on public.sync_requests ((1))
  where status in ('pending', 'in_progress');

alter table public.sync_requests enable row level security;

create policy "authenticated_insert_sync_requests" on public.sync_requests
  for insert to authenticated
  with check (requested_by_user_id = auth.uid());

create policy "authenticated_read_sync_requests" on public.sync_requests
  for select to authenticated using (true);

-- Sin política de UPDATE/DELETE para 'authenticated' a propósito — ver comentario de la
-- tabla arriba: solo service_role (el sistema local) puede cambiar el estado.
