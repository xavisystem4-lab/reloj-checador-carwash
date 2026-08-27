-- A pedido explicito del usuario: se quita el paso de aprobacion manual para cuentas
-- nuevas -- cualquiera que se registre (o que un Admin de de alta) entra directo, sin
-- esperar a que un Admin la apruebe. Los roles (admin/user) y el panel de gestion de
-- usuarios se mantienen igual -- solo cambia que el estado inicial ya no es 'pending'.
--
-- Contexto: este cambio de rumbo salio de un bug real detectado en produccion. Javier
-- Galaviz (ya admin+approved desde la migracion anterior) no podia iniciar sesion y veia
-- "Cuenta pendiente" -- la causa NO era el esquema de aprobacion en si, sino un deadlock
-- conocido de supabase-js (llamar a un .select() que necesita el token de sesion DESDE
-- DENTRO del callback de onAuthStateChange puede colgarse: ver el fix en dashboard/app.js,
-- que ahora difiere esa llamada con setTimeout). Aun asi, una vez explicado, el usuario
-- decidio que prefiere quitar la aprobacion por completo en vez de mantenerla.
create or replace function public.handle_new_auth_user()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
  insert into public.profiles (id, status) values (new.id, 'approved') on conflict (id) do nothing;
  return new;
end;
$$;
