-- BUG REAL en produccion: "infinite recursion detected in policy for relation profiles".
-- La politica de SELECT sobre profiles referenciaba a la propia tabla profiles dentro de
-- su propia clausula USING (para el caso "soy admin, veo todas las filas") -- Postgres
-- prohibe esto estructuralmente (auto-referencia de una tabla dentro de su propia
-- politica RLS), sin importar que logicamente terminara en un solo nivel. El patron
-- correcto (recomendado por la documentacion de Supabase) es mover ese chequeo a una
-- funcion SECURITY DEFINER: al ser propiedad de "postgres" (dueno de la tabla profiles),
-- la consulta DENTRO de la funcion corre bajo ese dueno, que por defecto se salta RLS por
-- completo -- rompe la recursion de raiz. La funcion no recibe un uuid como parametro:
-- solo evalua auth.uid() (el usuario que llama), para no exponer via RPC si CUALQUIER
-- otro id es admin.
create or replace function public.current_user_is_admin_approved()
returns boolean
language sql
security definer
stable
set search_path = public
as $$
  select exists (
    select 1 from public.profiles
    where id = auth.uid() and role = 'admin' and status = 'approved'
  );
$$;

drop policy "profiles_read_own_or_admin_all" on public.profiles;
create policy "profiles_read_own_or_admin_all" on public.profiles for select to authenticated
  using (
    id = auth.uid()
    or public.current_user_is_admin_approved()
  );
