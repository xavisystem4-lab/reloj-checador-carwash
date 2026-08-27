-- handle_new_auth_user es una función de TRIGGER (retorna trigger, usa NEW) -- solo debe
-- correr disparada por on_auth_user_created, nunca invocada directo. Por defecto PostgREST
-- expone cualquier función del schema public como RPC (/rest/v1/rpc/handle_new_auth_user),
-- y al ser SECURITY DEFINER el advisor de seguridad de Supabase la marca (WARN,
-- "anon_security_definer_function_executable"/"authenticated_security_definer_function_executable")
-- como ejecutable por anon/authenticated. Se revoca ese acceso directo; el trigger sigue
-- funcionando igual porque no pasa por permisos de rol de PostgREST, solo por ser dueño
-- de la función.
revoke execute on function public.handle_new_auth_user() from public, anon, authenticated;
