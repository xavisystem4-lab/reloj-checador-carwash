// Dashboard de reportes de Reloj Checador — sitio estático, sin backend propio: lee
// directo de Supabase (proyecto dedicado reloj-checador-carwash) usando la AnonKey
// (segura de embeber aquí — está pensada para ser pública, protegida por RLS: la
// migración initial_schema solo permite SELECT a usuarios autenticados, nunca INSERT/
// UPDATE/DELETE desde el navegador — ver src/RelojChecador.Infrastructure.Cloud/README.md
// del repo principal para el detalle completo de la arquitectura de sincronización).
//
// SÍ hay auto-registro público (signup-form) — cada cuenta nueva nace con una fila espejo
// en public.profiles (role='user' por defecto, status='approved' siempre, ver migración
// auto_approve_new_users) y entra directo, sin aprobación manual: ver
// dashboard/README.md, sección "Roles de cuentas (ya NO hay aprobación manual)", para la
// historia completa (incluye dos bugs reales de producción que se corrigieron en el
// camino) y applySessionState()/pending-screen más abajo para el único caso que sí sigue
// bloqueando el acceso (status='rejected', o un error técnico real al consultar el
// perfil).
import { createClient } from 'https://esm.sh/@supabase/supabase-js@2';

const SUPABASE_URL = 'https://vkvlucpjgvqrlvevcimq.supabase.co';
const SUPABASE_ANON_KEY = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InZrdmx1Y3BqZ3Zxcmx2ZXZjaW1xIiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODY2MDQ1MTQsImV4cCI6MjEwMjE4MDUxNH0.RWTJLCXhsPbSJLNpO2V2HNkhKqstqWgx33rkLekUxFI';

const supabase = createClient(SUPABASE_URL, SUPABASE_ANON_KEY);

const REFRESH_INTERVAL_MS = 10_000;
const MAX_ROWS = 2000;

// La app de escritorio actualiza LastCommunicationAtUtc al presionar "Conectar" con éxito
// en Dispositivos, y desde v1.10.7 también con cada marcación que llega por el monitoreo
// en tiempo real (antes solo con "Conectar" manual — un reloj que seguía funcionando
// normalmente podía mostrarse "Desconectado" aquí sin estarlo de verdad). Por eso
// "Conectado" se basa en qué tan RECIENTE es esa marca, no en un simple booleano — evita
// que un dispositivo quede "Conectado" para siempre solo porque una vez funcionó.
const DEVICE_ONLINE_THRESHOLD_MINUTES = 5;

// ---- Referencias al DOM ----
const loginScreen = document.getElementById('login-screen');
const dashboardScreen = document.getElementById('dashboard-screen');
const loginForm = document.getElementById('login-form');
const emailInput = document.getElementById('email-input');
const passwordInput = document.getElementById('password-input');
const passwordToggle = document.getElementById('password-toggle');
const loginError = document.getElementById('login-error');
const loginButton = document.getElementById('login-button');
const userNameButton = document.getElementById('user-name-button');
const logoutButton = document.getElementById('logout-button');

// Registro público (ver applySessionState/onSignupSubmit) — login-form y signup-form
// viven dentro del mismo login-screen, se alternan mostrando/ocultando cada <form>, nunca
// cambiando de pantalla.
const showSignupButton = document.getElementById('show-signup-button');
const showLoginButton = document.getElementById('show-login-button');
const signupForm = document.getElementById('signup-form');
const signupNameInput = document.getElementById('signup-name-input');
const signupEmailInput = document.getElementById('signup-email-input');
const signupPasswordInput = document.getElementById('signup-password-input');
const signupPasswordConfirmInput = document.getElementById('signup-password-confirm-input');
const signupError = document.getElementById('signup-error');
const signupSuccess = document.getElementById('signup-success');
const signupButton = document.getElementById('signup-button');

// Pantalla de cuenta rechazada / error técnico al consultar el perfil — ver
// applySessionState. NO se usa para "pending": ver comentario de esa función.
const pendingScreen = document.getElementById('pending-screen');
const pendingTitle = document.getElementById('pending-title');
const pendingMessage = document.getElementById('pending-message');
const pendingRetryButton = document.getElementById('pending-retry-button');
const pendingLogoutButton = document.getElementById('pending-logout-button');

const themeToggle = document.getElementById('theme-toggle');

const usersButton = document.getElementById('users-button');
const usersModal = document.getElementById('users-modal');
const usersModalClose = document.getElementById('users-modal-close');
const inviteForm = document.getElementById('invite-form');
const inviteNameInput = document.getElementById('invite-name-input');
const inviteEmailInput = document.getElementById('invite-email-input');
const inviteRoleSelect = document.getElementById('invite-role-select');
const inviteButton = document.getElementById('invite-button');
const inviteError = document.getElementById('invite-error');
const inviteSuccess = document.getElementById('invite-success');

// "Dar de alta con contraseña" — alternativa a "Invitar por correo" que no manda ningún
// correo, la cuenta queda lista para entrar de inmediato (ver onCreatePasswordSubmit).
const createPasswordForm = document.getElementById('create-password-form');
const createPasswordNameInput = document.getElementById('create-password-name-input');
const createPasswordEmailInput = document.getElementById('create-password-email-input');
const createPasswordPasswordInput = document.getElementById('create-password-password-input');
const createPasswordRoleSelect = document.getElementById('create-password-role-select');
const createPasswordButton = document.getElementById('create-password-button');
const createPasswordError = document.getElementById('create-password-error');
const createPasswordSuccess = document.getElementById('create-password-success');

const usersListStatus = document.getElementById('users-list-status');
const usersTbody = document.getElementById('users-tbody');

const branchSelect = document.getElementById('branch-select');
const fromInput = document.getElementById('from-input');
const toInput = document.getElementById('to-input');
const searchInput = document.getElementById('search-input');
const refreshButton = document.getElementById('refresh-button');
const exportButton = document.getElementById('export-button');
const syncRequestButton = document.getElementById('sync-request-button');
const syncRequestStatusEl = document.getElementById('sync-request-status');

const kpiTotal = document.getElementById('kpi-total');
const kpiEmployees = document.getElementById('kpi-employees');
const kpiDevices = document.getElementById('kpi-devices');
const kpiUnlinked = document.getElementById('kpi-unlinked');

const tableStatus = document.getElementById('table-status');
const tableBody = document.getElementById('attendance-tbody');
const devicesStatusRow = document.getElementById('devices-status-row');
const connectionBadge = document.getElementById('connection-badge');
const connectionBadgeText = document.getElementById('connection-badge-text');

// ---- Reporte de asistencia (horas trabajadas) — ver openAttendanceReport/computeEmployeeHours.
// Pedido explícito del usuario: "quiero que al darle clic al reporte de asistencia
// automáticamente se abra la imagen que se acabo de apuntar. No quiero la otra, quiero esa
// imagen" — "Reporte de asistencia" abre la previsualización DIRECTO, ya no hay una
// pantalla intermedia con solo la tabla. ----
const reportButton = document.getElementById('report-button');

const reportPreviewModal = document.getElementById('report-preview-modal');
const reportPreviewClose = document.getElementById('report-preview-close');
const reportPreviewPage = document.getElementById('report-preview-page');
const previewRangeText = document.getElementById('preview-range-text');
const previewTbody = document.getElementById('preview-tbody');
const previewEmptyText = document.getElementById('preview-empty-text');
const previewGeneratedText = document.getElementById('preview-generated-text');
const previewPrintButton = document.getElementById('preview-print-button');
const previewExcelButton = document.getElementById('preview-excel-button');
const previewPdfButton = document.getElementById('preview-pdf-button');
// Buscador de la previsualización — pedido explícito del usuario: "regálele también un
// buscador que busque por número, PIN, empleado, en cuanto vaya escribiendo, se vaya
// autorrellenando" — filtra EN VIVO (sin botón "buscar"), mismo criterio que el buscador
// principal del Dashboard (ver searchInput más abajo).
const previewSearchInput = document.getElementById('preview-search-input');

let lastReportRows = []; // última tabla de horas ya calculada (SIN filtrar) — la búsqueda parte de aquí
let currentPreviewRows = []; // lo que está renderizado AHORA en la hoja (ya filtrado) — Excel/PDF/Imprimir exportan esto, no lastReportRows

let autoRefreshTimer = null;
let lastLoadedRows = []; // guarda la última carga ya enriquecida, para exportar sin repetir el fetch

// ---- Arranque: ¿ya hay sesión? ----
init();

async function init() {
  applyThemeIcons(currentTheme()); // sincroniza el ícono con lo que ya fijó el <script> del <head>

  const { data: { session } } = await supabase.auth.getSession();
  await applySessionState(session);

  supabase.auth.onAuthStateChange((_event, session) => {
    // Bug real de producción (ver dashboard/README.md, "Bug real que motivó todo esto"):
    // onAuthStateChange dispara este callback DENTRO de una sección crítica interna del
    // cliente de Auth (GoTrueClient) — hacer aquí mismo una consulta que necesita el
    // token de sesión (como el select a profiles dentro de applySessionState) puede
    // quedarse colgada/fallar. setTimeout(..., 0) la saca de esa sección crítica.
    setTimeout(() => applySessionState(session), 0);
  });

  const today = new Date();
  const weekAgo = new Date(today);
  weekAgo.setDate(weekAgo.getDate() - 7);
  fromInput.value = toDateInputValue(weekAgo);
  toInput.value = toDateInputValue(today);

  loginForm.addEventListener('submit', onLoginSubmit);
  passwordToggle.addEventListener('click', onPasswordToggleClick);
  logoutButton.addEventListener('click', onLogoutClick);
  refreshButton.addEventListener('click', () => { loadReport(); loadDevicesStatus(); });
  exportButton.addEventListener('click', onExportClick);
  syncRequestButton.addEventListener('click', onSyncRequestClick);
  branchSelect.addEventListener('change', () => loadReport());
  fromInput.addEventListener('change', () => loadReport());
  toInput.addEventListener('change', () => loadReport());
  searchInput.addEventListener('input', debounce(() => renderTable(lastLoadedRows), 200));

  reportButton.addEventListener('click', openAttendanceReport);
  reportPreviewClose.addEventListener('click', closeReportPreview);
  reportPreviewModal.addEventListener('click', (event) => {
    if (event.target === reportPreviewModal) closeReportPreview();
  });
  previewPrintButton.addEventListener('click', () => window.print());
  previewExcelButton.addEventListener('click', onExportReportExcelClick);
  previewPdfButton.addEventListener('click', onExportReportPdfClick);
  previewSearchInput.addEventListener('input', debounce(() => renderPreviewTable(filterReportRows(lastReportRows, previewSearchInput.value)), 150));

  showSignupButton.addEventListener('click', showSignupFormView);
  showLoginButton.addEventListener('click', showLoginFormView);
  signupForm.addEventListener('submit', onSignupSubmit);

  pendingRetryButton.addEventListener('click', () => applySessionState(currentSession));
  pendingLogoutButton.addEventListener('click', onLogoutClick);

  themeToggle.addEventListener('click', toggleTheme);

  userNameButton.addEventListener('click', onEditOwnNameClick);
  usersButton.addEventListener('click', openUsersModal);
  usersModalClose.addEventListener('click', closeUsersModal);
  usersModal.addEventListener('click', (event) => {
    if (event.target === usersModal) closeUsersModal(); // clic fuera de la tarjeta
  });
  inviteForm.addEventListener('submit', onInviteSubmit);
  createPasswordForm.addEventListener('submit', onCreatePasswordSubmit);
}

let currentSession = null;
let currentProfile = null; // { role, status } de la PROPIA cuenta — null sin sesión

/// Ver dashboard/README.md, sección "Roles de cuentas (ya NO hay aprobación manual)", para
/// la historia completa. Resumen de lo que esta función decide hoy:
/// - Sin sesión → pantalla de login (con el enlace a signup-form).
/// - status='rejected' (un Admin quitó el acceso explícitamente) → pending-screen,
///   bloqueado, sin botón "Reintentar" (no es un error, es un estado real).
/// - Error TÉCNICO real al consultar el perfil (red, etc.) → pending-screen con
///   "Reintentar" visible — se distingue a propósito del caso anterior para no repetir el
///   bug real de producción documentado en el README (un deadlock de supabase-js se
///   disfrazaba de "cuenta pendiente").
/// - Cualquier otro caso (status='approved', o 'pending' — que hoy ya no debería ocurrir
///   para cuentas nuevas, ver migración auto_approve_new_users) → entra normal. NO se
///   bloquea por 'pending': fue una decisión explícita del usuario, no un olvido.
async function applySessionState(session) {
  currentSession = session;

  if (!session) {
    currentProfile = null;
    stopPollingSyncRequest();
    pendingScreen.hidden = true;
    dashboardScreen.hidden = true;
    loginScreen.hidden = false;
    showLoginFormView();
    stopAutoRefresh();
    closeUsersModal();
    return;
  }

  pendingRetryButton.hidden = true;

  let profile;
  try {
    const { data, error } = await supabase
      .from('profiles')
      .select('role, status')
      .eq('id', session.user.id)
      .single();
    if (error) throw error;
    profile = data;
  } catch (err) {
    console.error('No se pudo consultar el perfil de la cuenta:', err);
    loginScreen.hidden = true;
    dashboardScreen.hidden = true;
    pendingScreen.hidden = false;
    pendingTitle.textContent = 'No se pudo verificar tu cuenta';
    pendingMessage.textContent = 'Hubo un problema técnico al consultar tu perfil — no significa que tu acceso haya sido rechazado. Intenta de nuevo.';
    pendingRetryButton.hidden = false;
    return;
  }

  currentProfile = profile;

  if (profile.status === 'rejected') {
    loginScreen.hidden = true;
    dashboardScreen.hidden = true;
    pendingScreen.hidden = false;
    pendingTitle.textContent = 'Acceso rechazado';
    pendingMessage.textContent = 'Un administrador quitó el acceso de esta cuenta al Dashboard.';
    pendingRetryButton.hidden = true;
    return;
  }

  pendingScreen.hidden = true;
  loginScreen.hidden = true;
  dashboardScreen.hidden = false;
  // "👤 " adentro del texto a propósito: era el botón que se confundía con
  // "⚙️ Administrar usuarios" de al lado — con el ícono explícito en el nombre queda
  // claro que este es "tú", no la administración de otras cuentas.
  userNameButton.textContent = '👤 ' + displayNameFor(session.user);
  // Solo una cuenta admin+approved ve el panel — la Edge Function rechaza del lado del
  // servidor cualquier acción si no se cumple esto, ocultar el botón es solo UX.
  usersButton.hidden = !(profile.role === 'admin' && profile.status === 'approved');
  startAutoRefresh();
  loadBranches().then(() => loadReport());
  loadDevicesStatus();
  resumeActiveSyncRequestIfAny();
}

// ---- Alternar login-form / signup-form dentro de login-screen ----
function showSignupFormView() {
  loginForm.hidden = true;
  loginError.hidden = true;
  signupForm.hidden = false;
}

function showLoginFormView() {
  signupForm.hidden = true;
  signupError.hidden = true;
  signupSuccess.hidden = true;
  loginForm.hidden = false;
}

// ---- Registro público ----
async function onSignupSubmit(event) {
  event.preventDefault();
  signupError.hidden = true;
  signupSuccess.hidden = true;

  const password = signupPasswordInput.value;
  if (password !== signupPasswordConfirmInput.value) {
    signupError.textContent = 'Las contraseñas no coinciden.';
    signupError.hidden = false;
    return;
  }
  if (password.length < 6) {
    signupError.textContent = 'La contraseña debe tener al menos 6 caracteres.';
    signupError.hidden = false;
    return;
  }

  signupButton.disabled = true;
  signupButton.textContent = 'Creando cuenta…';

  const fullName = signupNameInput.value.trim();
  const { error } = await supabase.auth.signUp({
    email: signupEmailInput.value.trim(),
    password,
    options: { data: { full_name: fullName || null } },
  });

  signupButton.disabled = false;
  signupButton.textContent = 'Crear cuenta';

  if (error) {
    signupError.textContent = mapAuthError(error);
    signupError.hidden = false;
    return;
  }

  // Si "Confirmar correo" está desactivado en Supabase (lo normal para este proyecto),
  // signUp ya deja la sesión iniciada de inmediato — onAuthStateChange se encarga solo de
  // aplicar el estado nuevo (el trigger on_auth_user_created ya creó su fila en profiles,
  // status='approved'). No hace falta nada más aquí más que limpiar el formulario.
  signupForm.reset();
}

// ---- Pestillo de tema día/noche ----
const THEME_STORAGE_KEY = 'theme-preference';

function currentTheme() {
  return document.documentElement.getAttribute('data-theme'); // 'light' | 'dark' | null (sigue al sistema)
}

function isDarkModeActive(theme) {
  return theme === 'dark' || (theme === null && window.matchMedia('(prefers-color-scheme: dark)').matches);
}

function applyThemeIcons(theme) {
  const dark = isDarkModeActive(theme);
  // Sol visible invita a volver a modo día (o sea, se muestra EN modo oscuro); luna
  // visible invita a pasar a modo noche — ver los <svg> en index.html.
  themeToggle.querySelector('.icon-sun').style.display = dark ? '' : 'none';
  themeToggle.querySelector('.icon-moon').style.display = dark ? 'none' : '';
}

function toggleTheme() {
  const next = isDarkModeActive(currentTheme()) ? 'light' : 'dark';
  document.documentElement.setAttribute('data-theme', next);
  try {
    localStorage.setItem(THEME_STORAGE_KEY, next);
  } catch (e) {
    // Almacenamiento no disponible (navegación privada, etc.) — el tema igual se aplica
    // para esta sesión de navegación, solo no se recuerda la próxima vez.
  }
  applyThemeIcons(next);
}

/// El nombre visible viene de user_metadata.full_name (lo edita la propia persona con el
/// botón de su nombre, o se lo pone un admin al invitarla). Si todavía no tiene uno
/// definido (p. ej. la primera cuenta, creada directo en Supabase sin pasar por el panel
/// de invitación), en vez de mostrar el correo completo (se ve a "plomería interna", no a
/// un nombre) se deriva algo más parecido a un nombre de usuario a partir de la parte
/// antes de la "@" — sigue siendo un botón clickeable para poner el nombre real cuando
/// quieran.
function displayNameFor(user) {
  const fullName = user?.user_metadata?.full_name;
  if (fullName && fullName.trim()) {
    return fullName.trim();
  }

  const email = user?.email ?? '';
  const localPart = email.split('@')[0] ?? '';
  if (!localPart) {
    return email;
  }

  return localPart
    .split(/[._-]+/)
    .filter(Boolean)
    .map(part => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ');
}

// ---- Inicio de sesión ----
async function onLoginSubmit(event) {
  event.preventDefault();
  loginError.hidden = true;
  loginButton.disabled = true;
  loginButton.textContent = 'Entrando…';

  const { error } = await supabase.auth.signInWithPassword({
    email: emailInput.value.trim(),
    password: passwordInput.value,
  });

  loginButton.disabled = false;
  loginButton.textContent = 'Iniciar sesión';

  if (error) {
    loginError.textContent = mapAuthError(error);
    loginError.hidden = false;
    return;
  }

  passwordInput.value = '';
  setPasswordVisible(false); // por si quedó "mostrar" activo, no dejar el campo visible al volver a entrar
}

// ---- "Ojito" de mostrar/ocultar contraseña ----
function onPasswordToggleClick() {
  setPasswordVisible(passwordInput.type === 'password');
}

function setPasswordVisible(visible) {
  passwordInput.type = visible ? 'text' : 'password';
  passwordToggle.setAttribute('aria-pressed', String(visible));
  passwordToggle.setAttribute('aria-label', visible ? 'Ocultar contraseña' : 'Mostrar contraseña');
  passwordToggle.title = visible ? 'Ocultar contraseña' : 'Mostrar contraseña';
  passwordToggle.querySelector('.icon-eye').style.display = visible ? 'none' : '';
  passwordToggle.querySelector('.icon-eye-off').style.display = visible ? '' : 'none';
}

/// Compartida entre login (onLoginSubmit) y registro (onSignupSubmit) — ambos son
/// llamadas de auth.
function mapAuthError(error) {
  const message = error?.message ?? '';
  if (message.includes('Invalid login credentials')) {
    return 'Correo o contraseña incorrectos.';
  }
  if (message.includes('Email not confirmed')) {
    return 'Esta cuenta todavía no confirma su correo.';
  }
  if (message.includes('already registered') || message.includes('already exists')) {
    return 'Ya existe una cuenta con ese correo — intenta iniciar sesión.';
  }
  if (message.toLowerCase().includes('signups not allowed') || message.toLowerCase().includes('signup is disabled')) {
    // "Allow new users to sign up" apagado en Authentication → Providers → Email del
    // panel de Supabase — ver dashboard/README.md.
    return 'El registro público está desactivado por ahora. Pide a un administrador que te dé de alta.';
  }
  if (message.includes('Password should be at least')) {
    return 'La contraseña debe tener al menos 6 caracteres.';
  }
  return 'No se pudo completar la operación: ' + message;
}

async function onLogoutClick() {
  await supabase.auth.signOut();
}

// ---- Editar mi propio nombre ----
// A propósito NO usa la Edge Function: cambiar tu PROPIO nombre es una operación segura de
// hacer directo contra tu propia sesión (anon key + tu JWT), sin necesitar la service_role
// key del lado del servidor — eso solo hace falta para tocar la cuenta de alguien MÁS
// (invitar, editar el nombre de otra persona, eliminar), ver callManageUsers().
async function onEditOwnNameClick() {
  const current = currentSession?.user?.user_metadata?.full_name ?? '';
  const next = window.prompt('¿Cómo quieres que aparezca tu nombre en el Dashboard?', current);
  if (next === null) return; // canceló
  const trimmed = next.trim();
  if (!trimmed) return;

  const { data, error } = await supabase.auth.updateUser({ data: { full_name: trimmed } });
  if (error) {
    window.alert('No se pudo actualizar tu nombre: ' + error.message);
    return;
  }

  currentSession = { ...currentSession, user: data.user };
  userNameButton.textContent = '👤 ' + displayNameFor(data.user);
}

// ---- Panel "Usuarios del Dashboard" (manage-users Edge Function) ----
// Cualquier acción que toque la cuenta de OTRA persona (listar todas, invitar, editar el
// nombre de alguien más, eliminar) necesita la service_role key del lado del servidor —
// nunca se expone en este archivo. supabase.functions.invoke() adjunta automáticamente el
// JWT de la sesión actual, y la Edge Function (verify_jwt=true) rechaza la llamada si esa
// sesión no es válida.
async function callManageUsers(action, payload) {
  const { data, error } = await supabase.functions.invoke('manage-users', {
    body: { action, ...payload },
  });
  if (error) {
    // supabase-js no siempre expone el cuerpo del error de la función directamente —
    // se intenta leer la respuesta real para mostrar el mensaje útil ("correo ya
    // registrado", etc.) en vez de un genérico "Edge Function returned a non-2xx status".
    const detail = await error.context?.json?.().catch(() => null);
    throw new Error(detail?.error ?? error.message ?? 'Error desconocido.');
  }
  if (data?.error) {
    throw new Error(data.error);
  }
  return data;
}

function openUsersModal() {
  usersModal.hidden = false;
  inviteError.hidden = true;
  inviteSuccess.hidden = true;
  inviteForm.reset();
  createPasswordError.hidden = true;
  createPasswordSuccess.hidden = true;
  createPasswordForm.reset();
  loadUsersList();
}

function closeUsersModal() {
  usersModal.hidden = true;
}

async function loadUsersList() {
  usersListStatus.textContent = 'Cargando…';
  usersTbody.innerHTML = '';
  try {
    const { users } = await callManageUsers('list', {});
    usersListStatus.textContent = `${users.length} usuario(s) con acceso al Dashboard.`;
    renderUsersTable(users);
  } catch (err) {
    usersListStatus.textContent = 'No se pudo cargar la lista: ' + err.message;
  }
}

function renderUsersTable(users) {
  usersTbody.innerHTML = '';
  const fragment = document.createDocumentFragment();

  for (const user of users) {
    const tr = document.createElement('tr');
    const nameCell = user.full_name
      ? escapeHtml(user.full_name)
      : '<span class="user-name-cell-empty">(sin nombre)</span>';
    const lastSignIn = user.last_sign_in_at
      ? formatDateTime(user.last_sign_in_at)
      : '<span class="user-name-cell-empty">nunca entró</span>';
    const isSelf = user.id === currentSession?.user?.id;

    // El selector de rol (y el botón de eliminar) NO aparece sobre la propia fila — para
    // no poder degradarte o auto-eliminarte por accidente, ver dashboard/README.md.
    const roleCell = isSelf
      ? escapeHtml(user.role === 'admin' ? 'Admin' : 'Usuario')
      : `<select class="user-role-select" data-id="${user.id}">
          <option value="user" ${user.role === 'admin' ? '' : 'selected'}>Usuario</option>
          <option value="admin" ${user.role === 'admin' ? 'selected' : ''}>Admin</option>
        </select>`;

    tr.innerHTML = `
      <td>${nameCell}</td>
      <td>${escapeHtml(user.email ?? '')}</td>
      <td>${roleCell}</td>
      <td>${lastSignIn}</td>
      <td style="text-align:right; white-space:nowrap;">
        <button class="btn-icon" data-action="edit" data-id="${user.id}" data-name="${escapeHtml(user.full_name ?? '')}" title="Editar nombre">✏️</button>
        <button class="btn-icon" data-action="password" data-id="${user.id}" data-email="${escapeHtml(user.email ?? '')}" title="Cambiar contraseña">🔑</button>
        ${isSelf ? '' : `<button class="btn-icon danger" data-action="delete" data-id="${user.id}" data-email="${escapeHtml(user.email ?? '')}" title="Eliminar acceso">🗑️</button>`}
      </td>
    `;
    fragment.appendChild(tr);
  }

  usersTbody.appendChild(fragment);
  usersTbody.querySelectorAll('button[data-action="edit"]').forEach(btn =>
    btn.addEventListener('click', () => onEditOtherUserName(btn.dataset.id, btn.dataset.name)));
  usersTbody.querySelectorAll('button[data-action="password"]').forEach(btn =>
    btn.addEventListener('click', () => onResetUserPassword(btn.dataset.id, btn.dataset.email)));
  usersTbody.querySelectorAll('button[data-action="delete"]').forEach(btn =>
    btn.addEventListener('click', () => onDeleteUser(btn.dataset.id, btn.dataset.email)));
  usersTbody.querySelectorAll('select.user-role-select').forEach(select =>
    select.addEventListener('change', () => onChangeUserRole(select.dataset.id, select.value, select)));
}

/// Reset de contraseña para una cuenta YA existente — el Admin decide la nueva contraseña
/// directamente aquí, sin pasar por un correo de recuperación (distinto de "Dar de alta
/// con contraseña", que crea la cuenta).
async function onResetUserPassword(userId, email) {
  const password = window.prompt(`Nueva contraseña para "${email}" (mínimo 6 caracteres):`);
  if (password === null) return; // canceló
  if (password.length < 6) {
    window.alert('La contraseña debe tener al menos 6 caracteres.');
    return;
  }

  try {
    await callManageUsers('update_password', { user_id: userId, password });
    window.alert('Contraseña actualizada.');
  } catch (err) {
    window.alert('No se pudo cambiar la contraseña: ' + err.message);
  }
}

async function onChangeUserRole(userId, newRole, selectEl) {
  const confirmed = window.confirm(
    `¿Cambiar el rol de esta cuenta a "${newRole === 'admin' ? 'Admin' : 'Usuario'}"?`);
  if (!confirmed) {
    loadUsersList(); // revierte el <select> a su valor real recargando desde el servidor
    return;
  }

  selectEl.disabled = true;
  try {
    await callManageUsers('update_role', { user_id: userId, role: newRole });
  } catch (err) {
    window.alert('No se pudo cambiar el rol: ' + err.message);
    loadUsersList();
  } finally {
    selectEl.disabled = false;
  }
}

async function onEditOtherUserName(userId, currentName) {
  const next = window.prompt('Nuevo nombre para esta cuenta:', currentName ?? '');
  if (next === null) return;
  const trimmed = next.trim();

  try {
    await callManageUsers('update_name', { user_id: userId, full_name: trimmed });
    loadUsersList();
  } catch (err) {
    window.alert('No se pudo actualizar el nombre: ' + err.message);
  }
}

async function onDeleteUser(userId, email) {
  const confirmed = window.confirm(
    `¿Quitar el acceso al Dashboard de "${email}"? Esta persona ya no podrá iniciar sesión.`);
  if (!confirmed) return;

  try {
    await callManageUsers('delete', { user_id: userId });
    loadUsersList();
  } catch (err) {
    window.alert('No se pudo eliminar el acceso: ' + err.message);
  }
}

async function onInviteSubmit(event) {
  event.preventDefault();
  inviteError.hidden = true;
  inviteSuccess.hidden = true;
  inviteButton.disabled = true;
  inviteButton.textContent = 'Invitando…';

  try {
    const result = await callManageUsers('invite', {
      email: inviteEmailInput.value.trim(),
      full_name: inviteNameInput.value.trim(),
      role: inviteRoleSelect.value,
    });
    inviteSuccess.textContent =
      `Se invitó a ${result.user.email} — le llegó un correo para que defina su propia contraseña.`;
    inviteSuccess.hidden = false;
    inviteForm.reset();
    loadUsersList();
  } catch (err) {
    inviteError.textContent = 'No se pudo invitar: ' + err.message;
    inviteError.hidden = false;
  } finally {
    inviteButton.disabled = false;
    inviteButton.textContent = '+ Invitar';
  }
}

/// Alternativa a "Invitar por correo": el Admin decide la contraseña directamente y la
/// cuenta queda lista para entrar de inmediato, sin pasar por ningún correo de invitación
/// (email_confirm:true del lado del servidor, ver manage-users/index.ts).
async function onCreatePasswordSubmit(event) {
  event.preventDefault();
  createPasswordError.hidden = true;
  createPasswordSuccess.hidden = true;
  createPasswordButton.disabled = true;
  createPasswordButton.textContent = 'Creando…';

  try {
    const result = await callManageUsers('create_with_password', {
      email: createPasswordEmailInput.value.trim(),
      full_name: createPasswordNameInput.value.trim(),
      password: createPasswordPasswordInput.value,
      role: createPasswordRoleSelect.value,
    });
    createPasswordSuccess.textContent =
      `Se creó la cuenta de ${result.user.email} — ya puede iniciar sesión con esa contraseña.`;
    createPasswordSuccess.hidden = false;
    createPasswordForm.reset();
    loadUsersList();
  } catch (err) {
    createPasswordError.textContent = 'No se pudo crear la cuenta: ' + err.message;
    createPasswordError.hidden = false;
  } finally {
    createPasswordButton.disabled = false;
    createPasswordButton.textContent = '+ Crear';
  }
}

// ---- Carga de sucursales (para el filtro) ----
async function loadBranches() {
  const { data, error } = await supabase.from('branches').select('id, name, code').order('name');
  if (error) {
    console.error('No se pudieron cargar las sucursales:', error);
    return;
  }

  const currentValue = branchSelect.value;
  branchSelect.innerHTML = '<option value="">Todas</option>';
  for (const branch of data ?? []) {
    const option = document.createElement('option');
    option.value = branch.id;
    option.textContent = branch.name;
    branchSelect.appendChild(option);
  }
  branchSelect.value = currentValue;
}

// ---- Estado de conexión de cada reloj checador ----
async function loadDevicesStatus() {
  // .neq('status', 'Disabled'): un dispositivo dado de baja desde la app de escritorio
  // ("🗑️ Eliminar" en Dispositivos — baja lógica, ver DevicesViewModel.DeleteDeviceAsync
  // del repo principal) sigue sincronizándose a esta tabla con status='Disabled' en vez de
  // borrarse — sin este filtro, el Dashboard lo seguía mostrando aquí para siempre aunque
  // ya no exista para nadie más en la app. Caso real: un dispositivo de prueba ("Susushi")
  // dado de baja hace días seguía apareciendo con "Sin comunicación registrada".
  const { data, error } = await supabase
    .from('devices')
    .select('id, name, last_communication_at_utc')
    .neq('status', 'Disabled')
    .order('name');

  if (error) {
    console.error('No se pudo cargar el estado de los dispositivos:', error);
    return;
  }

  if (!data || data.length === 0) {
    devicesStatusRow.innerHTML = '<div class="devices-status-empty">Sin relojes checadores registrados.</div>';
    setConnectionBadge(false, 'Sin relojes registrados');
    return;
  }

  const now = Date.now();
  devicesStatusRow.innerHTML = '';
  let anyOnline = false;
  for (const device of data) {
    const lastCommMs = device.last_communication_at_utc ? new Date(device.last_communication_at_utc).getTime() : null;
    const minutesAgo = lastCommMs ? (now - lastCommMs) / 60_000 : null;
    const isOnline = minutesAgo !== null && minutesAgo <= DEVICE_ONLINE_THRESHOLD_MINUTES;
    if (isOnline) anyOnline = true;

    const pill = document.createElement('div');
    pill.className = 'device-status-pill';
    pill.innerHTML = `
      <div class="device-status-dot ${isOnline ? 'online' : 'offline'}"></div>
      <div class="device-status-name">${escapeHtml(device.name)}</div>
      <div class="device-status-label">${isOnline ? 'Conectado' : describeOffline(minutesAgo)}</div>
    `;
    devicesStatusRow.appendChild(pill);
  }

  // Badge agregado del header (ver connection-badge en index.html): "Conectado" en verde
  // si AL MENOS un reloj checador está en vivo, para que se note de inmediato arriba —
  // sobre todo en móvil, donde la fila de pills por dispositivo puede quedar más abajo.
  setConnectionBadge(anyOnline, data.length === 1 ? data[0].name : `${data.length} relojes`);
}

function setConnectionBadge(isOnline, detail) {
  connectionBadge.classList.toggle('online', isOnline);
  connectionBadge.classList.toggle('offline', !isOnline);
  connectionBadgeText.textContent = isOnline ? 'Conectado' : 'Desconectado';
  connectionBadge.title = detail ?? '';
}

function describeOffline(minutesAgo) {
  if (minutesAgo === null) {
    return 'Sin comunicación registrada';
  }
  if (minutesAgo < 60) {
    return `Desconectado (hace ${Math.round(minutesAgo)} min)`;
  }
  const hoursAgo = Math.round(minutesAgo / 60);
  return `Desconectado (hace ${hoursAgo} h)`;
}

// ---- "Actualizar asistencias" (sincronización remota) ----
// Flujo: este botón INSERTA una fila en sync_requests (permitido por RLS solo para
// INSERT/SELECT, ver supabase/migrations/20260814060000_add_sync_requests.sql) — nunca
// llama directo a la PC del negocio, no hay ninguna conexión entrante hacia allá. La app
// de escritorio de la sucursal CONSULTA esta tabla periódicamente (cada ~10s, ver
// RemoteSyncRequestPollingService en el repo principal) y actualiza el estado; aquí solo
// se hace polling de esa misma fila cada 3s para reflejarlo en pantalla. Si la PC está
// apagada, la fila simplemente se queda "pending" hasta que la app vuelva a estar en
// línea — nada especial que manejar aparte de seguir mostrando ese estado con paciencia.
const SYNC_REQUEST_POLL_MS = 3_000;
const SYNC_REQUEST_SLOW_WARNING_MS = 120_000; // 2 min
let activeSyncRequestId = null;
let syncRequestPollTimer = null;
let syncRequestStartedAt = null;

async function onSyncRequestClick() {
  const active = await findActiveSyncRequest();
  if (active) {
    trackSyncRequest(active.id);
    return;
  }

  syncRequestButton.disabled = true;
  setSyncRequestStatus('Enviando solicitud…', 'pending');

  const { data, error } = await supabase
    .from('sync_requests')
    .insert({
      requested_by_user_id: currentSession.user.id,
      requested_by_email: currentSession.user.email,
    })
    .select('id')
    .single();

  if (error) {
    // 23505 = chocó con el índice único parcial (ya había una solicitud activa creada
    // justo antes — carrera entre dos clics o dos pestañas abiertas a la vez). Se
    // recupera y se engancha a esa en vez de mostrar un error real al usuario.
    if (error.code === '23505') {
      const winner = await findActiveSyncRequest();
      if (winner) {
        trackSyncRequest(winner.id);
        return;
      }
    }
    setSyncRequestStatus('No se pudo enviar la solicitud: ' + error.message, 'error');
    syncRequestButton.disabled = false;
    return;
  }

  trackSyncRequest(data.id);
}

async function findActiveSyncRequest() {
  const { data } = await supabase
    .from('sync_requests')
    .select('id, status')
    .in('status', ['pending', 'in_progress'])
    .order('requested_at_utc', { ascending: false })
    .limit(1)
    .maybeSingle();
  return data ?? null;
}

/// Se llama tanto tras crear/enganchar una solicitud como al cargar la página — así, si
/// alguien cerró la pestaña con una solicitud todavía pendiente (o la disparó otra
/// persona desde otra sesión), el estado se retoma solo sin tener que volver a hacer clic.
async function resumeActiveSyncRequestIfAny() {
  const active = await findActiveSyncRequest();
  if (active) {
    trackSyncRequest(active.id);
  }
}

function trackSyncRequest(id) {
  activeSyncRequestId = id;
  syncRequestStartedAt = Date.now();
  syncRequestButton.disabled = true;
  setSyncRequestStatus('Solicitud enviada — esperando al sistema local…', 'pending');
  pollSyncRequest();
}

function pollSyncRequest() {
  clearInterval(syncRequestPollTimer);
  syncRequestPollTimer = setInterval(async () => {
    const { data, error } = await supabase
      .from('sync_requests')
      .select('status, result_summary, error_message')
      .eq('id', activeSyncRequestId)
      .single();

    if (error || !data) {
      return; // hipo de red — se reintenta en el siguiente tick, no se trata como error
    }

    if (data.status === 'in_progress') {
      setSyncRequestStatus('Sincronizando con el reloj checador…', 'syncing');
    } else if (data.status === 'completed') {
      setSyncRequestStatus('✅ ' + (data.result_summary ?? 'Completado.'), 'completed');
      stopPollingSyncRequest();
      loadReport();
      loadDevicesStatus();
    } else if (data.status === 'failed') {
      setSyncRequestStatus('❌ ' + (data.error_message ?? 'Ocurrió un error.'), 'error');
      stopPollingSyncRequest();
    } else if (syncRequestStartedAt && Date.now() - syncRequestStartedAt > SYNC_REQUEST_SLOW_WARNING_MS) {
      // Sigue "pending" después de un buen rato — no es un error (puede que la PC del
      // negocio esté apagada), pero vale la pena avisar en vez de dejar el mensaje
      // genérico indefinidamente.
      setSyncRequestStatus(
        'Solicitud enviada — esto está tardando más de lo normal. Revisa que la computadora del negocio esté encendida y conectada.',
        'pending');
    }
  }, SYNC_REQUEST_POLL_MS);
}

function stopPollingSyncRequest() {
  clearInterval(syncRequestPollTimer);
  syncRequestPollTimer = null;
  activeSyncRequestId = null;
  syncRequestStartedAt = null;
  syncRequestButton.disabled = false;
}

function setSyncRequestStatus(text, kind) {
  syncRequestStatusEl.textContent = text;
  syncRequestStatusEl.className = `sync-request-status ${kind}`;
  syncRequestStatusEl.hidden = false;
}

// ---- Carga del reporte principal ----
async function loadReport() {
  tableStatus.textContent = 'Cargando…';

  // A propósito NO se usa `new Date(...).toISOString()` aquí: eso interpretaría
  // fromInput.value/toInput.value como hora LOCAL DEL NAVEGADOR y los convertiría a UTC
  // real, pero timestamp_utc en la base NO es UTC real (ver formatAttendanceDateTime más
  // abajo) — es la hora de pared del reloj checador, sin convertir. Construir el string
  // directo con sufijo "Z" evita esa conversión y compara contra el valor tal cual está
  // guardado.
  const fromUtc = `${fromInput.value}T00:00:00.000Z`;
  const toUtc = `${toInput.value}T23:59:59.999Z`;

  let query = supabase
    .from('attendances')
    .select('id, device_id, branch_id, employee_id, device_user_pin, timestamp_utc, verify_method, punch_type')
    .gte('timestamp_utc', fromUtc)
    .lte('timestamp_utc', toUtc)
    .order('timestamp_utc', { ascending: false })
    .limit(MAX_ROWS);

  if (branchSelect.value) {
    query = query.eq('branch_id', branchSelect.value);
  }

  const { data: attendances, error } = await query;
  if (error) {
    tableStatus.textContent = 'No se pudo cargar el reporte: ' + error.message;
    tableBody.innerHTML = '';
    resetKpis();
    return;
  }

  const enriched = await enrichAttendances(attendances ?? []);
  lastLoadedRows = enriched;
  renderKpis(enriched);
  renderTable(enriched);

  tableStatus.textContent = attendances.length === MAX_ROWS
    ? `Mostrando las primeras ${MAX_ROWS.toLocaleString('es-MX')} marcaciones — acota el rango de fechas para ver todo.`
    : `${attendances.length.toLocaleString('es-MX')} marcación(es) encontrada(s).`;
}

/// vinculado directamente — desde v1.31.0 la app de escritorio SÍ lo resuelve al guardar
/// cada marcación (ver DevicesViewModel.ResolveEmployeeAndBranchAsync en el repo
/// principal), así que hoy es la vía normal; (2) EmployeeDeviceMapping (device_id + pin)
/// queda como respaldo para marcaciones más viejas que se guardaron antes de ese cambio,
/// o en "sin vincular" si tampoco hay mapeo.
async function enrichAttendances(attendances) {
  if (attendances.length === 0) {
    return [];
  }

  const branchIds = [...new Set(attendances.map(a => a.branch_id).filter(Boolean))];
  const deviceIds = [...new Set(attendances.map(a => a.device_id).filter(Boolean))];
  const directEmployeeIds = [...new Set(attendances.map(a => a.employee_id).filter(Boolean))];

  const [branchesRes, devicesRes, mappingsRes] = await Promise.all([
    branchIds.length ? supabase.from('branches').select('id, name').in('id', branchIds) : { data: [] },
    deviceIds.length ? supabase.from('devices').select('id, name').in('id', deviceIds) : { data: [] },
    deviceIds.length ? supabase.from('employee_device_mappings').select('device_id, device_user_pin, employee_id').in('device_id', deviceIds) : { data: [] },
  ]);

  const branchNameById = new Map((branchesRes.data ?? []).map(b => [b.id, b.name]));
  const deviceNameById = new Map((devicesRes.data ?? []).map(d => [d.id, d.name]));
  const mappingByDeviceAndPin = new Map(
    (mappingsRes.data ?? []).map(m => [`${m.device_id}|${m.device_user_pin}`, m.employee_id]));

  const employeeIdsToResolve = new Set(directEmployeeIds);
  for (const a of attendances) {
    const mapped = mappingByDeviceAndPin.get(`${a.device_id}|${a.device_user_pin}`);
    if (mapped) employeeIdsToResolve.add(mapped);
  }

  const employeeNameById = new Map();
  // "number" (Número de negocio) — pedido explícito del usuario para el Reporte de
  // asistencia (ver openAttendanceReport/buildAttendanceReportRows), distinto del PIN del
  // dispositivo. "department" — pedido explícito: "el reporte de asistencia también agrega
  // departamento, quienes pertenecen a sus áreas. Por ejemplo, car wash, arábica café,
  // otros, plaza sabo" — conserva la sucursal/área original de alguien fusionado a
  // CAR-WASH (ver comentario de clase de EmployeesViewModel.ApplyCatalogReplaceAsync en el
  // repo principal).
  const employeeNumberById = new Map();
  const employeeDepartmentById = new Map();
  if (employeeIdsToResolve.size > 0) {
    const { data: employees } = await supabase
      .from('employees').select('id, full_name, number, department').in('id', [...employeeIdsToResolve]);
    for (const e of employees ?? []) {
      employeeNameById.set(e.id, e.full_name);
      employeeNumberById.set(e.id, e.number);
      employeeDepartmentById.set(e.id, e.department);
    }
  }

  return attendances.map(a => {
    const resolvedEmployeeId = a.employee_id ?? mappingByDeviceAndPin.get(`${a.device_id}|${a.device_user_pin}`) ?? null;
    const employeeName = resolvedEmployeeId ? employeeNameById.get(resolvedEmployeeId) : null;
    return {
      ...a,
      branchName: branchNameById.get(a.branch_id) ?? '—',
      deviceName: deviceNameById.get(a.device_id) ?? '—',
      employeeName: employeeName ?? null,
      resolvedEmployeeId: resolvedEmployeeId,
      employeeNumber: resolvedEmployeeId ? (employeeNumberById.get(resolvedEmployeeId) ?? null) : null,
      employeeDepartment: resolvedEmployeeId ? (employeeDepartmentById.get(resolvedEmployeeId) ?? null) : null,
      isUnlinked: !employeeName,
    };
  });
}

function renderKpis(rows) {
  kpiTotal.textContent = rows.length.toLocaleString('es-MX');

  const employeeKeys = new Set(rows.map(r => r.employeeName ?? `pin:${r.device_user_pin}`));
  kpiEmployees.textContent = employeeKeys.size.toLocaleString('es-MX');

  const deviceIds = new Set(rows.map(r => r.device_id));
  kpiDevices.textContent = deviceIds.size.toLocaleString('es-MX');

  const unlinkedCount = rows.filter(r => r.isUnlinked).length;
  kpiUnlinked.textContent = unlinkedCount.toLocaleString('es-MX');
}

function resetKpis() {
  kpiTotal.textContent = '—';
  kpiEmployees.textContent = '—';
  kpiDevices.textContent = '—';
  kpiUnlinked.textContent = '—';
}

function renderTable(rows) {
  const term = searchInput.value.trim().toLowerCase();
  const filtered = term
    ? rows.filter(r =>
        (r.employeeName ?? '').toLowerCase().includes(term) ||
        r.device_user_pin.toLowerCase().includes(term))
    : rows;

  tableBody.innerHTML = '';
  if (filtered.length === 0) {
    const tr = document.createElement('tr');
    tr.innerHTML = `<td colspan="7" style="text-align:center; color:var(--muted); padding:32px;">Sin marcaciones para estos filtros.</td>`;
    tableBody.appendChild(tr);
    return;
  }

  const fragment = document.createDocumentFragment();
  for (const row of filtered) {
    const tr = document.createElement('tr');

    const employeeCell = row.employeeName
      ? escapeHtml(row.employeeName)
      : `<span class="pill pill-unlinked">sin vincular</span>`;

    const punchLabel = row.punch_type === 0
      ? '<span class="pill pill-in">Entrada</span>'
      : row.punch_type === 1
        ? '<span class="pill pill-out">Salida</span>'
        : '—';

    // Orden pedido explícito del usuario: PIN, Empleado, Tipo, Fecha y hora, Método,
    // Sucursal, Dispositivo — mismo orden en index.html (encabezado <thead>).
    tr.innerHTML = `
      <td>${escapeHtml(row.device_user_pin)}</td>
      <td>${employeeCell}</td>
      <td>${punchLabel}</td>
      <td>${formatAttendanceDateTime(row.timestamp_utc)}</td>
      <td>${escapeHtml(mapVerifyMethod(row.verify_method))}</td>
      <td>${escapeHtml(row.branchName)}</td>
      <td>${escapeHtml(row.deviceName)}</td>
    `;
    fragment.appendChild(tr);
  }
  tableBody.appendChild(fragment);
}

function mapVerifyMethod(method) {
  switch (method) {
    case 'Fingerprint': return 'Huella';
    case 'Password': return 'Contraseña';
    case 'Card': return 'Tarjeta';
    case 'Face': return 'Rostro';
    // Capturada a mano desde "Marcar asistencia manual" (app de escritorio, pantalla
    // Asistencia) — nunca viene del reloj físico, ver AttendanceVerifyMethod.Manual del
    // repo principal.
    case 'Manual': return 'Manual';
    default: return 'Desconocido';
  }
}

function formatDateTime(isoUtc) {
  return new Date(isoUtc).toLocaleString('es-MX', { dateStyle: 'medium', timeStyle: 'short' });
}

/// Formatea SOLO marcaciones (attendances.timestamp_utc). A diferencia de formatDateTime
/// (que sí convierte de UTC real a la hora del navegador — correcto para last_sign_in_at,
/// que es UTC genuino de Supabase Auth), este valor NO es UTC real: el reloj checador
/// entrega su propia hora local (Mexicali) y todo el sistema la guarda tal cual, solo
/// etiquetada como UTC, sin convertirla (ver Attendance.Create en el repo principal — "todo
/// el negocio opera en una sola zona horaria, no hay conversión real"). timeZone: 'UTC'
/// fuerza a que se muestren los componentes crudos del valor guardado, ignorando el huso
/// horario del navegador — así coincide con lo que muestra la app de escritorio y con la
/// hora real del reloj, en vez de restarle el offset dos veces.
function formatAttendanceDateTime(isoUtc) {
  return new Date(isoUtc).toLocaleString('es-MX', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'UTC' });
}

function toDateInputValue(date) {
  return date.toISOString().slice(0, 10);
}

function escapeHtml(value) {
  const div = document.createElement('div');
  div.textContent = value ?? '';
  return div.innerHTML;
}

function debounce(fn, ms) {
  let timeoutId;
  return (...args) => {
    clearTimeout(timeoutId);
    timeoutId = setTimeout(() => fn(...args), ms);
  };
}

// ---- Exportar CSV ----
function onExportClick() {
  const term = searchInput.value.trim().toLowerCase();
  const rows = term
    ? lastLoadedRows.filter(r =>
        (r.employeeName ?? '').toLowerCase().includes(term) ||
        r.device_user_pin.toLowerCase().includes(term))
    : lastLoadedRows;

  if (rows.length === 0) {
    return;
  }

  const header = ['Fecha y hora', 'Empleado', 'PIN', 'Sucursal', 'Dispositivo', 'Método', 'Tipo'];
  const csvRows = [header, ...rows.map(r => [
    formatAttendanceDateTime(r.timestamp_utc),
    r.employeeName ?? '(sin vincular)',
    r.device_user_pin,
    r.branchName,
    r.deviceName,
    mapVerifyMethod(r.verify_method),
    r.punch_type === 0 ? 'Entrada' : r.punch_type === 1 ? 'Salida' : '',
  ])];

  const csv = csvRows.map(row => row.map(csvEscape).join(',')).join('\r\n');
  const blob = new Blob(['﻿' + csv], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `asistencias-${fromInput.value}-a-${toInput.value}.csv`;
  a.click();
  URL.revokeObjectURL(url);
}

function csvEscape(value) {
  const text = String(value ?? '');
  return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

// ---- Reporte de asistencia (horas trabajadas) — pedido explícito del usuario: "quiero
// tener un botón que se llame reporte de asistencia ... que me salgan las horas trabajadas
// del empleado, que lo pueda filtrar de tal fecha a tal fecha ... para que me ayude a hacer
// un reporte de nómina ... quiero que me aparezcan las horas trabajadas hasta el día de
// hoy" (ya cubierto: "Hasta" nace en el día de hoy, ver init()). Reutiliza el rango
// Desde/Hasta/Sucursal de los filtros principales — no duplica esos controles aquí. ----

const PUNCH_IN = 0;
const PUNCH_OUT = 1;
const BREAK_OUT = 2;
const BREAK_IN = 3;
const OVERTIME_IN = 4;
const OVERTIME_OUT = 5;

/// "Ahora" con el mismo criterio que timestamp_utc en toda la base: NO es UTC real, es la
/// hora de pared del negocio sin convertir (ver formatAttendanceDateTime más abajo) — así
/// que para comparar contra eso, "ahora" tiene que armarse con los componentes de la hora
/// LOCAL DEL NAVEGADOR (no Date.now()/toISOString(), que sí son UTC real y se desfasarían
/// por el huso horario de quien esté viendo el Dashboard). Asume que quien lo ve está en el
/// mismo huso horario que el negocio — mismo supuesto que ya usa el resto de la app.
function nowAsFakeUtcIso() {
  const now = new Date();
  const pad = (n) => String(n).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}` +
    `T${pad(now.getHours())}:${pad(now.getMinutes())}:${pad(now.getSeconds())}.000Z`;
}

/// Empareja cronológicamente cada marcación "abre" (openType) con la siguiente "cierra"
/// (closeType) dentro de un mismo día y suma la diferencia en milisegundos — mismo
/// criterio que WorkedHoursCalculator.PairAndSum del repo principal (RelojChecador.
/// Application.Payroll). Un desbalance (dos aperturas seguidas, un cierre sin apertura)
/// simplemente no se cuenta — este reporte no muestra advertencias por fila, a diferencia
/// del cálculo de nómina de la app de escritorio.
///
/// <paramref name="openUntilIso"/>: pedido explícito del usuario — "las horas trabajadas
/// cuéntalas en tiempo real desde que checaron hasta ahorita". Si al terminar de recorrer
/// las marcaciones queda una apertura SIN su cierre (el empleado sigue trabajando ahora
/// mismo) y se pasó este valor, se cuenta el tiempo hasta ahí en vez de dejarlo en 0 —
/// quien llama solo lo pasa para el DÍA DE HOY (ver computeEmployeeHours), nunca para un
/// día pasado con una salida realmente olvidada.
function pairAndSumMs(sortedDayRows, openType, closeType, openUntilIso = null) {
  let totalMs = 0;
  let openAtIso = null;
  for (const row of sortedDayRows) {
    if (row.punch_type === openType) {
      openAtIso = row.timestamp_utc;
    } else if (row.punch_type === closeType) {
      if (openAtIso) {
        totalMs += new Date(row.timestamp_utc) - new Date(openAtIso);
        openAtIso = null;
      }
    }
  }
  if (openAtIso && openUntilIso) {
    const elapsedMs = new Date(openUntilIso) - new Date(openAtIso);
    if (elapsedMs > 0) totalMs += elapsedMs;
  }
  return totalMs;
}

/// Horas normales + horas extra de UN empleado, agrupando primero por día calendario (el
/// prefijo "YYYY-MM-DD" del timestamp, sin conversión de huso horario — mismo criterio que
/// el resto del Dashboard) y emparejando Entrada/Salida SOLO dentro de cada día, para no
/// mezclar una entrada de un día con una salida de otro. Descanso (2/3) resta de las horas
/// normales, sin bajar de 0. Un turno de HOY que sigue abierto (sin Salida todavía) cuenta
/// en tiempo real hasta este momento — ver pairAndSumMs.
function computeEmployeeHours(employeeRows) {
  const nowIso = nowAsFakeUtcIso();
  const today = nowIso.slice(0, 10);

  const byDay = new Map();
  for (const row of employeeRows) {
    const day = row.timestamp_utc.slice(0, 10);
    if (!byDay.has(day)) byDay.set(day, []);
    byDay.get(day).push(row);
  }

  let regularMs = 0;
  let overtimeMs = 0;
  for (const [day, dayRows] of byDay) {
    const sorted = [...dayRows].sort((a, b) => a.timestamp_utc.localeCompare(b.timestamp_utc));
    const openUntilIso = day === today ? nowIso : null;
    const dayRegularMs = pairAndSumMs(sorted, PUNCH_IN, PUNCH_OUT, openUntilIso);
    const dayBreakMs = pairAndSumMs(sorted, BREAK_OUT, BREAK_IN);
    regularMs += Math.max(0, dayRegularMs - dayBreakMs);
    overtimeMs += pairAndSumMs(sorted, OVERTIME_IN, OVERTIME_OUT, openUntilIso);
  }
  return { regularMs, overtimeMs };
}

/// Arma una fila por empleado (agrupando por resolvedEmployeeId — o por PIN si nunca se
/// vinculó a nadie) a partir de lastLoadedRows, ya filtrado por Sucursal/Desde/Hasta. NO
/// aplica el buscador de texto libre de la tabla principal — el reporte parte siempre de
/// TODOS los empleados del rango; el buscador propio de la previsualización (ver
/// filterReportRows) filtra DESPUÉS, sobre esta misma lista ya calculada.
function buildAttendanceReportRows() {
  const byEmployee = new Map();
  for (const row of lastLoadedRows) {
    const key = row.resolvedEmployeeId ?? `unlinked:${row.device_user_pin}`;
    if (!byEmployee.has(key)) {
      byEmployee.set(key, {
        number: row.employeeNumber ?? null,
        name: row.employeeName ?? `PIN ${row.device_user_pin} · sin vincular`,
        // Departamento (área original de alguien fusionado a CAR-WASH) si está capturado;
        // si no, la propia Sucursal — pedido explícito del usuario: "el reporte de
        // asistencia también agrega departamento, quienes pertenecen a sus áreas. Por
        // ejemplo, car wash, arábica café, otros, plaza sabo" — así TODOS muestran algún
        // área, no solo quienes tienen Department capturado.
        department: row.employeeDepartment || row.branchName || '—',
        pins: new Set(),
        rows: [],
      });
    }
    const entry = byEmployee.get(key);
    entry.pins.add(row.device_user_pin);
    entry.rows.push(row);
  }

  const result = [];
  for (const entry of byEmployee.values()) {
    const { regularMs, overtimeMs } = computeEmployeeHours(entry.rows);
    result.push({
      number: entry.number,
      // Casi siempre un solo PIN — varios solo si el empleado se enroló en más de un
      // reloj/PIN dentro del mismo rango; se listan todos, separados por coma.
      pin: [...entry.pins].join(', '),
      name: entry.name,
      department: entry.department,
      regularHours: regularMs / 3_600_000,
      overtimeHours: overtimeMs / 3_600_000,
      totalHours: (regularMs + overtimeMs) / 3_600_000,
    });
  }

  result.sort((a, b) => a.name.localeCompare(b.name, 'es-MX'));
  return result;
}

/// Pedido explícito del usuario: "un buscador que busque por número, PIN, empleado, en
/// cuanto vaya escribiendo, se vaya autorrellenando" — substring, sin distinguir
/// mayúsculas, sobre cualquiera de los tres campos.
function filterReportRows(rows, term) {
  const normalized = term.trim().toLowerCase();
  if (!normalized) {
    return rows;
  }
  return rows.filter(r =>
    (r.number ?? '').toLowerCase().includes(normalized) ||
    r.pin.toLowerCase().includes(normalized) ||
    r.name.toLowerCase().includes(normalized));
}

function formatHours(hours) {
  return hours.toLocaleString('es-MX', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function reportRangeLabel() {
  const branchLabel = branchSelect.value
    ? (branchSelect.options[branchSelect.selectedIndex]?.textContent ?? '')
    : 'Todas las sucursales';
  return `Del ${fromInput.value} al ${toInput.value} — ${branchLabel}`;
}

/// "Reporte de asistencia" — pedido explícito del usuario: "quiero que al darle clic al
/// reporte de asistencia automáticamente se abra la imagen que se acabo de apuntar. No
/// quiero la otra, quiero esa imagen" — calcula las horas y abre la previsualización
/// DIRECTO, sin ninguna pantalla intermedia.
function openAttendanceReport() {
  lastReportRows = buildAttendanceReportRows();
  if (lastReportRows.length === 0) {
    alert('No hay marcaciones en este rango para generar el reporte.');
    return;
  }

  previewSearchInput.value = '';
  previewRangeText.textContent = reportRangeLabel();
  previewGeneratedText.textContent = `Generado el ${formatDateTime(new Date().toISOString())}`;
  renderPreviewTable(lastReportRows);

  reportPreviewModal.hidden = false;
  ensureExportLibrariesLoaded(); // en segundo plano — Excel/PDF no bloquean la vista previa
}

/// Dibuja la hoja con EXACTAMENTE estas filas (ya filtradas o no) — currentPreviewRows
/// guarda lo último dibujado, así Imprimir/Exportar Excel/Exportar PDF siempre reflejan lo
/// que se está viendo, sea el reporte completo o una búsqueda en curso.
function renderPreviewTable(rows) {
  currentPreviewRows = rows;

  previewTbody.innerHTML = '';
  previewEmptyText.hidden = rows.length > 0;
  if (rows.length === 0) {
    return;
  }

  const fragment = document.createDocumentFragment();
  let totalRegular = 0;
  let totalOvertime = 0;
  for (const row of rows) {
    totalRegular += row.regularHours;
    totalOvertime += row.overtimeHours;
    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td>${escapeHtml(row.number ?? '—')}</td>
      <td>${escapeHtml(row.pin || '—')}</td>
      <td>${escapeHtml(row.name)}</td>
      <td>${escapeHtml(row.department)}</td>
      <td>${formatHours(row.regularHours)} h</td>
      <td>${formatHours(row.overtimeHours)} h</td>
      <td>${formatHours(row.totalHours)} h</td>
    `;
    fragment.appendChild(tr);
  }
  const totalTr = document.createElement('tr');
  totalTr.innerHTML = `
    <td colspan="4">Total</td>
    <td>${formatHours(totalRegular)} h</td>
    <td>${formatHours(totalOvertime)} h</td>
    <td>${formatHours(totalRegular + totalOvertime)} h</td>
  `;
  fragment.appendChild(totalTr);
  previewTbody.appendChild(fragment);
}

function closeReportPreview() {
  reportPreviewModal.hidden = true;
}

// Excel (SheetJS) y PDF (html2canvas + jsPDF) se cargan solo cuando hacen falta — evita
// que cualquier visita al Dashboard pague ese peso extra solo por tener el botón
// disponible. loadScriptOnce no repite una carga si el <script> ya está en la página.
let exportLibrariesPromise = null;

function loadScriptOnce(src) {
  return new Promise((resolve, reject) => {
    if (document.querySelector(`script[src="${src}"]`)) {
      resolve();
      return;
    }
    const script = document.createElement('script');
    script.src = src;
    script.onload = () => resolve();
    script.onerror = () => reject(new Error('No se pudo cargar ' + src));
    document.head.appendChild(script);
  });
}

function ensureExportLibrariesLoaded() {
  if (!exportLibrariesPromise) {
    exportLibrariesPromise = Promise.all([
      loadScriptOnce('https://cdn.jsdelivr.net/npm/xlsx@0.18.5/dist/xlsx.full.min.js'),
      loadScriptOnce('https://cdn.jsdelivr.net/npm/jspdf@2.5.2/dist/jspdf.umd.min.js'),
      loadScriptOnce('https://cdn.jsdelivr.net/npm/html2canvas@1.4.1/dist/html2canvas.min.js'),
    ]);
  }
  return exportLibrariesPromise;
}

/// Pedido explícito del usuario: "que te pregunte en qué carpeta quieres guardar las
/// exportaciones ... no que luego luego lo mande a descargas". showSaveFilePicker (File
/// System Access API) SÍ pregunta dónde guardar — disponible en Chrome/Edge, no en Firefox/
/// Safari a la fecha. Donde no exista (o el usuario la cancele con Escape/"Cancelar", que
/// lanza AbortError — eso NO es un error real, simplemente no se guarda nada), cae de
/// vuelta a la descarga directa de siempre en vez de dejar al usuario sin poder exportar.
async function saveBlobWithPicker(blob, suggestedName, description, mimeType, extension) {
  if ('showSaveFilePicker' in window) {
    try {
      const handle = await window.showSaveFilePicker({
        suggestedName,
        types: [{ description, accept: { [mimeType]: [extension] } }],
      });
      const writable = await handle.createWritable();
      await writable.write(blob);
      await writable.close();
      return;
    } catch (error) {
      if (error.name === 'AbortError') {
        return; // el usuario cerró/canceló el diálogo — no es un error, no se guarda nada
      }
      // Cualquier otro error real (poco común) cae al respaldo de abajo en vez de fallar
      // la exportación por completo.
    }
  }

  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = suggestedName;
  a.click();
  URL.revokeObjectURL(url);
}

async function onExportReportExcelClick() {
  const originalLabel = previewExcelButton.textContent;
  previewExcelButton.disabled = true;
  previewExcelButton.textContent = 'Preparando…';
  try {
    await ensureExportLibrariesLoaded();

    const sheetRows = [
      ['Drive In Car Wash — Reporte de asistencia'],
      [reportRangeLabel()],
      [],
      ['Número', 'PIN', 'Empleado', 'Departamento', 'Horas normales', 'Horas extra', 'Total horas'],
      // currentPreviewRows, NO lastReportRows — exporta exactamente lo que está en pantalla
      // (respeta el buscador si hay uno en curso).
      ...currentPreviewRows.map(r => [r.number ?? '', r.pin, r.name, r.department, Number(r.regularHours.toFixed(2)), Number(r.overtimeHours.toFixed(2)), Number(r.totalHours.toFixed(2))]),
    ];
    const worksheet = XLSX.utils.aoa_to_sheet(sheetRows);
    worksheet['!cols'] = [{ wch: 10 }, { wch: 12 }, { wch: 32 }, { wch: 18 }, { wch: 16 }, { wch: 14 }, { wch: 14 }];
    const workbook = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(workbook, worksheet, 'Asistencia');

    const xlsxBytes = XLSX.write(workbook, { type: 'array', bookType: 'xlsx' });
    const blob = new Blob([xlsxBytes], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
    await saveBlobWithPicker(
      blob, `reporte-asistencia-${fromInput.value}-a-${toInput.value}.xlsx`,
      'Libro de Excel', 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', '.xlsx');
  } catch (error) {
    alert('No se pudo exportar a Excel: ' + error.message);
  } finally {
    previewExcelButton.disabled = false;
    previewExcelButton.textContent = originalLabel;
  }
}

async function onExportReportPdfClick() {
  const originalLabel = previewPdfButton.textContent;
  previewPdfButton.disabled = true;
  previewPdfButton.textContent = 'Preparando…';
  try {
    await ensureExportLibrariesLoaded();

    const canvas = await html2canvas(reportPreviewPage, { scale: 2, backgroundColor: '#FFFFFF' });
    const imgData = canvas.toDataURL('image/png');

    // Tamaño Carta (8.5in x 11in) — pedido explícito del usuario.
    const { jsPDF } = window.jspdf;
    const pdf = new jsPDF({ unit: 'in', format: 'letter', orientation: 'portrait' });
    const pageWidthIn = 8.5;
    const pageHeightIn = 11;
    const imgHeightIn = (canvas.height * pageWidthIn) / canvas.width;

    // La hoja puede ser más alta que una página Carta (muchos empleados) — se reparte en
    // varias páginas repitiendo la MISMA imagen alta, desplazada hacia arriba cada vez
    // (técnica estándar de html2canvas+jsPDF para HTML que no cabe en una sola página).
    let remainingHeightIn = imgHeightIn;
    let positionIn = 0;
    pdf.addImage(imgData, 'PNG', 0, positionIn, pageWidthIn, imgHeightIn);
    remainingHeightIn -= pageHeightIn;
    while (remainingHeightIn > 0) {
      positionIn = remainingHeightIn - imgHeightIn;
      pdf.addPage();
      pdf.addImage(imgData, 'PNG', 0, positionIn, pageWidthIn, imgHeightIn);
      remainingHeightIn -= pageHeightIn;
    }

    const blob = pdf.output('blob');
    await saveBlobWithPicker(
      blob, `reporte-asistencia-${fromInput.value}-a-${toInput.value}.pdf`,
      'Documento PDF', 'application/pdf', '.pdf');
  } catch (error) {
    alert('No se pudo exportar a PDF: ' + error.message);
  } finally {
    previewPdfButton.disabled = false;
    previewPdfButton.textContent = originalLabel;
  }
}

// ---- Auto-actualización: la app de escritorio sube cada ~10s como respaldo periódico
// (ver SupabaseSyncOptions.IntervalSeconds en el repo principal), y además dispara una
// sincronización inmediata en cuanto llega una marcación nueva (sin esperar ese ciclo,
// ver DevicesViewModel.PersistAndTriggerSyncAsync) — refrescar aquí cada 10s mantiene el
// Dashboard prácticamente al día con ambos caminos. ----
function startAutoRefresh() {
  stopAutoRefresh();
  autoRefreshTimer = setInterval(() => {
    if (!dashboardScreen.hidden) {
      loadReport();
      loadDevicesStatus();
    }
  }, REFRESH_INTERVAL_MS);
}

function stopAutoRefresh() {
  if (autoRefreshTimer) {
    clearInterval(autoRefreshTimer);
    autoRefreshTimer = null;
  }
}
