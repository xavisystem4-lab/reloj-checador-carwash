import "jsr:@supabase/functions-js/edge-runtime.d.ts";
import { createClient } from "jsr:@supabase/supabase-js@2";

// Edge Function para administrar las cuentas que pueden entrar al Dashboard web.
// Corre en el servidor (Deno, dentro de la infraestructura de Supabase) y usa la
// service_role key SOLO aqui -- nunca se expone en el navegador. El Dashboard (sitio
// estatico, sin backend propio) llama a esta funcion via supabase.functions.invoke(),
// que adjunta automaticamente el JWT de la sesion actual en el header Authorization.
//
// verify_jwt=true (fijado al desplegar) hace que Supabase rechace la peticion ANTES de
// que este codigo corra si no trae un JWT valido de una sesion iniciada -- por eso no hay
// verificacion manual de sesion aqui, ya la hizo la plataforma. El modelo de confianza es
// deliberadamente simple: cualquier persona que ya tiene acceso de lectura al Dashboard
// (autenticada, ver RLS de las demas tablas) puede tambien invitar/editar otras cuentas --
// coherente con que es un solo negocio pequeno, no un sistema multi-tenant con roles.
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
    if (action === "list") {
      const { data, error } = await adminClient.auth.admin.listUsers();
      if (error) throw error;

      const users = data.users
        .map((u) => ({
          id: u.id,
          email: u.email,
          full_name: (u.user_metadata as Record<string, unknown> | null)?.full_name ?? null,
          created_at: u.created_at,
          last_sign_in_at: u.last_sign_in_at,
        }))
        .sort((a, b) => (a.created_at < b.created_at ? -1 : 1));

      return jsonResponse({ users });
    }

    if (action === "invite") {
      const email = typeof body.email === "string" ? body.email.trim() : "";
      const fullName = typeof body.full_name === "string" ? body.full_name.trim() : "";

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

      return jsonResponse({
        user: { id: data.user.id, email: data.user.email, full_name: fullName || null },
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
