import "jsr:@supabase/functions-js/edge-runtime.d.ts";
import { createClient } from "jsr:@supabase/supabase-js@2";

// Edge Function para administrar las cuentas que pueden entrar al Dashboard web.
// Corre en el servidor (Deno, dentro de la infraestructura de Supabase) y usa la
// service_role key SOLO aqui -- nunca se expone en el navegador. El Dashboard (sitio
// estatico, sin backend propio) llama a esta funcion via supabase.functions.invoke(),
// que adjunta automaticamente el JWT de la sesion actual en el header Authorization.
//
// verify_jwt=true (fijado al desplegar) hace que Supabase rechace la peticion ANTES de
// que este codigo corra si no trae un JWT valido de una sesion iniciada -- pero eso solo
// confirma que HAY una sesion valida, no quien es. Existe ademas una tabla
// public.profiles (role admin/user, status siempre 'approved' para cuentas nuevas -- ya
// NO hay flujo de aprobacion manual, se quito a pedido del usuario tras un bug real, ver
// migracion auto_approve_new_users). Toda accion de esta funcion exige que quien llama
// sea admin+approved -- ver requireAdmin() -- porque cualquier cuenta puede LEER reportes
// (RLS de las demas tablas), pero solo un admin puede dar de alta, invitar, editar
// rol/contrasena/nombre o quitar acceso a otras cuentas.
//
// SUPABASE_URL y SUPABASE_SERVICE_ROLE_KEY los inyecta Supabase automaticamente en el
// entorno de cada Edge Function -- no hace falta configurarlos a mano.
//
// Copia versionada de lo desplegado — ver dashboard/README.md para el comando de deploy
// (npx supabase functions deploy manage-users --project-ref vkvlucpjgvqrlvevcimq).
const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const SERVICE_ROLE_KEY = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;

const adminClient = createClient(SUPABASE_URL, SERVICE_ROLE_KEY);

const corsHeaders = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers": "authorization, x-client-info, apikey, content-type",
  "Access-Control-Allow-Methods": "POST, OPTIONS",
};

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { ...corsHeaders, "Content-Type": "application/json" },
  });
}

// Resuelve quien llama a partir del JWT que Supabase ya valido, y exige que sea
// admin+approved en public.profiles. Se llama al inicio de CADA accion de este archivo
// salvo que se documente explicitamente lo contrario.
async function requireAdmin(req: Request): Promise<{ caller: { id: string; email?: string } } | { error: Response }> {
  const authHeader = req.headers.get("Authorization") ?? "";
  const jwt = authHeader.replace(/^Bearer\s+/i, "");
  if (!jwt) {
    return { error: jsonResponse({ error: "No autenticado." }, 401) };
  }

  const { data: userData, error: userError } = await adminClient.auth.getUser(jwt);
  if (userError || !userData?.user) {
    return { error: jsonResponse({ error: "No autenticado." }, 401) };
  }

  const { data: profile, error: profileError } = await adminClient
    .from("profiles")
    .select("role, status")
    .eq("id", userData.user.id)
    .single();

  if (profileError || !profile || profile.role !== "admin" || profile.status !== "approved") {
    return { error: jsonResponse({ error: "Solo un administrador puede hacer esto." }, 403) };
  }

  return { caller: { id: userData.user.id, email: userData.user.email } };
}

// Fija el rol elegido en el momento de dar de alta una cuenta (invite/
// create_with_password) -- status ya es 'approved' por defecto (ver
// handle_new_auth_user), esto solo asegura el rol y deja el registro de quien la dio de
// alta.
async function approveProfile(userId: string, role: string, approvedBy: string) {
  const { error } = await adminClient
    .from("profiles")
    .update({ role, status: "approved", approved_by: approvedBy, approved_at: new Date().toISOString() })
    .eq("id", userId);
  if (error) throw error;
}

function normalizeRole(value: unknown): string {
  return value === "admin" ? "admin" : "user";
}

Deno.serve(async (req: Request) => {
  if (req.method === "OPTIONS") {
    return new Response("ok", { headers: corsHeaders });
  }

  let body: Record<string, unknown>;
  try {
    body = await req.json();
  } catch {
    return jsonResponse({ error: "Cuerpo de la peticion invalido (se esperaba JSON)." }, 400);
  }

  const action = body.action;

  try {
    const auth = await requireAdmin(req);
    if ("error" in auth) return auth.error;
    const { caller } = auth;

    if (action === "list") {
      const { data, error } = await adminClient.auth.admin.listUsers();
      if (error) throw error;

      const { data: profiles, error: profilesError } = await adminClient
        .from("profiles")
        .select("id, role, status");
      if (profilesError) throw profilesError;
      const profileById = new Map((profiles ?? []).map((p) => [p.id, p]));

      const users = data.users
        .map((u) => ({
          id: u.id,
          email: u.email,
          full_name: (u.user_metadata as Record<string, unknown> | null)?.full_name ?? null,
          created_at: u.created_at,
          last_sign_in_at: u.last_sign_in_at,
          role: profileById.get(u.id)?.role ?? "user",
          status: profileById.get(u.id)?.status ?? "pending",
        }))
        // Toda cuenta nueva ya nace 'approved' (ver handle_new_auth_user) -- este filtro
        // es defensivo, por si algun dia se reactivara el concepto de status distinto.
        .filter((u) => u.status === "approved")
        .sort((a, b) => (a.created_at < b.created_at ? -1 : 1));

      return jsonResponse({ users });
    }

    if (action === "invite") {
      const email = typeof body.email === "string" ? body.email.trim() : "";
      const fullName = typeof body.full_name === "string" ? body.full_name.trim() : "";
      const role = normalizeRole(body.role);

      if (!email || !email.includes("@")) {
        return jsonResponse({ error: "El correo no es valido." }, 400);
      }

      // inviteUserByEmail crea la cuenta y hace que Supabase le mande a esa persona un
      // correo con un enlace para que ELLA misma defina su contrasena -- nunca se
      // genera ni se maneja una contrasena desde aqui.
      const { data, error } = await adminClient.auth.admin.inviteUserByEmail(email, {
        data: { full_name: fullName || null },
      });
      if (error) throw error;

      // El trigger on_auth_user_created ya creo la fila en profiles ('approved' por
      // defecto) -- esto solo fija el rol elegido y deja registro de quien invito.
      await approveProfile(data.user.id, role, caller.id);

      return jsonResponse({
        user: { id: data.user.id, email: data.user.email, full_name: fullName || null, role },
      });
    }

    if (action === "create_with_password") {
      const email = typeof body.email === "string" ? body.email.trim() : "";
      const fullName = typeof body.full_name === "string" ? body.full_name.trim() : "";
      const password = typeof body.password === "string" ? body.password : "";
      const role = normalizeRole(body.role);

      if (!email || !email.includes("@")) {
        return jsonResponse({ error: "El correo no es valido." }, 400);
      }
      if (password.length < 6) {
        return jsonResponse({ error: "La contrasena debe tener al menos 6 caracteres." }, 400);
      }

      // Alta manual sin invitacion: se le asigna la contrasena directamente aqui (el
      // admin la decide) y email_confirm:true evita que Supabase le mande cualquier
      // correo de confirmacion -- puede entrar de inmediato con esa contrasena.
      const { data, error } = await adminClient.auth.admin.createUser({
        email,
        password,
        email_confirm: true,
        user_metadata: { full_name: fullName || null },
      });
      if (error) throw error;

      await approveProfile(data.user.id, role, caller.id);

      return jsonResponse({
        user: { id: data.user.id, email: data.user.email, full_name: fullName || null, role },
      });
    }

    if (action === "update_name") {
      const userId = typeof body.user_id === "string" ? body.user_id : "";
      const fullName = typeof body.full_name === "string" ? body.full_name.trim() : "";

      if (!userId) {
        return jsonResponse({ error: "Falta user_id." }, 400);
      }

      const { error } = await adminClient.auth.admin.updateUserById(userId, {
        user_metadata: { full_name: fullName || null },
      });
      if (error) throw error;

      return jsonResponse({ ok: true });
    }

    if (action === "update_role") {
      const userId = typeof body.user_id === "string" ? body.user_id : "";
      const role = normalizeRole(body.role);
      if (!userId) {
        return jsonResponse({ error: "Falta user_id." }, 400);
      }

      // A diferencia de approve/create_with_password/invite, esto NO toca status --
      // solo cambia el rol de una cuenta que ya estaba approved. Quien llama ya paso por
      // requireAdmin(), pero no se valida aqui que no se este degradando a si mismo (el
      // Dashboard oculta ese control para la propia fila, ver app.js) -- si algun dia
      // hiciera falta, aqui es donde se agregaria esa validacion server-side.
      const { error } = await adminClient.from("profiles").update({ role }).eq("id", userId);
      if (error) throw error;

      return jsonResponse({ ok: true });
    }

    if (action === "update_password") {
      const userId = typeof body.user_id === "string" ? body.user_id : "";
      const password = typeof body.password === "string" ? body.password : "";

      if (!userId) {
        return jsonResponse({ error: "Falta user_id." }, 400);
      }
      if (password.length < 6) {
        return jsonResponse({ error: "La contrasena debe tener al menos 6 caracteres." }, 400);
      }

      // Reset de contrasena para una cuenta YA existente -- distinto de
      // create_with_password (que crea la cuenta). El Admin decide la nueva contrasena
      // directamente, sin pasar por un correo de recuperacion.
      const { error } = await adminClient.auth.admin.updateUserById(userId, { password });
      if (error) throw error;

      return jsonResponse({ ok: true });
    }

    if (action === "delete") {
      const userId = typeof body.user_id === "string" ? body.user_id : "";
      if (!userId) {
        return jsonResponse({ error: "Falta user_id." }, 400);
      }

      const { error } = await adminClient.auth.admin.deleteUser(userId);
      if (error) throw error;

      return jsonResponse({ ok: true });
    }

    return jsonResponse({ error: `Accion desconocida: ${String(action)}` }, 400);
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    return jsonResponse({ error: message }, 500);
  }
});
