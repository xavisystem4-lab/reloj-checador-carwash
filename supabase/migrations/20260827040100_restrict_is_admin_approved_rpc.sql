-- current_user_is_admin_approved() SI necesita ser ejecutable por 'authenticated' (la usa
-- la politica RLS de profiles al evaluarse para ese rol) -- ese WARN del advisor de
-- seguridad es esperado y aceptado: la funcion no recibe parametro, solo evalua auth.uid()
-- del propio llamador, asi que ni siquiera invocada directo via RPC expone nada sobre
-- otras cuentas. 'anon' en cambio nunca la necesita (sin sesion auth.uid() es null, y de
-- todas formas ninguna politica para 'anon' la usa) -- se le revoca el acceso directo.
revoke execute on function public.current_user_is_admin_approved() from public, anon;
