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
import {
  addDaysIso, getWeekStartIso, dayLabel, shortDate, longDate, calculateWeek, dayBadgeText,
  formatHoursAndMinutes, formatMoney, netPay, buildAttendanceReportCsv,
} from './payroll-calc.js';
import { APP_VERSION } from './version.js';

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
// Ver placeSearchFieldForViewport() más abajo — reubica el campo de búsqueda principal
// justo arriba de la tabla en móvil.
const searchFieldWrap = searchInput.closest('.filter-field');
const filtersSection = document.querySelector('.filters');
const tableSection = document.querySelector('.table-section');
const mobileSearchQuery = window.matchMedia('(max-width: 640px)');
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

// ---- Reporte de asistencia — ventana como la de la PC ----
// Pedidos explícitos del usuario: primero "quiero tener un botón que se llame reporte de
// asistencia ... que lo pueda filtrar de tal fecha a tal fecha"; después "que me salgan las
// marcaciones de la semana" (hora de entrada y salida de cada día); y por último "en Reporte de
// Asistencia se abra una ventana y que el formato sea como el de la PC" — con sueldos, decidido
// explícitamente por el usuario.
//
// La ventana (weekly-report-modal) replica la pantalla Reportes de la app de escritorio: navegación
// por semana (lunes a domingo), buscador y filtros, una fila por empleado ACTIVO con sus 7 días
// (insignia de color con las horas y, debajo, entrada–salida), Faltas, Horas normales/extra y las
// columnas de nómina. El cálculo es el de la PC, portado a payroll-calc.js (con pruebas en
// tests/dashboard). Los MONTOS (sueldo, deducciones, neto) solo se piden a Supabase y se muestran a
// cuentas Admin — Supabase no restringe esas columnas por rol, así que la web ni siquiera las
// descarga para una cuenta 'user'.

const reportButton = document.getElementById('report-button');

const weeklyReportModal = document.getElementById('weekly-report-modal');
const wrClose = document.getElementById('wr-close');
const wrPrev = document.getElementById('wr-prev');
const wrNext = document.getElementById('wr-next');
const wrRefresh = document.getElementById('wr-refresh');
const wrCsv = document.getElementById('wr-csv');
const wrPreview = document.getElementById('wr-preview');
const wrRange = document.getElementById('wr-range');
const wrSearch = document.getElementById('wr-search');
const wrBranch = document.getElementById('wr-branch');
const wrDept = document.getElementById('wr-dept');
const wrStatus = document.getElementById('wr-status');
const wrSyncNote = document.getElementById('wr-sync-note');
const wrThead = document.getElementById('wr-thead');
const wrTbody = document.getElementById('wr-tbody');

// Hoja Carta horizontal (imprimir / Excel / PDF) — la "Vista previa e imprimir" de la PC.
const reportPreviewModal = document.getElementById('report-preview-modal');
const reportPreviewClose = document.getElementById('report-preview-close');
const reportPreviewPage = document.getElementById('report-preview-page');
// Ver fitReportPreviewToViewport() más abajo — este marco es lo que de verdad se
// redimensiona; report-preview-page solo se transforma visualmente dentro de él.
const reportPreviewPageFrame = document.getElementById('report-preview-page-frame');
const previewRangeText = document.getElementById('preview-range-text');
const previewThead = document.getElementById('preview-thead');
const previewTbody = document.getElementById('preview-tbody');
const previewEmptyText = document.getElementById('preview-empty-text');
const previewGeneratedText = document.getElementById('preview-generated-text');
const previewPrintButton = document.getElementById('preview-print-button');
const previewExcelButton = document.getElementById('preview-excel-button');
const previewPdfButton = document.getElementById('preview-pdf-button');

let reportWeekStart = null; // lunes de la semana mostrada, "YYYY-MM-DD"
let reportCanSeePay = false; // solo cuentas Admin ven (y descargan) sueldos y deducciones
let reportAllRows = []; // todos los empleados activos con su semana ya calculada (sin filtrar)
let reportVisibleRows = []; // lo que se ve ahora (filtrado) — CSV, Excel, hoja y PDF salen de aquí
let reportUnlinkedCount = 0; // marcaciones de la semana cuyo PIN no pertenece a un empleado vigente
let reportLoadToken = 0; // descarta respuestas viejas si se cambia de semana mientras carga
let reportScaleBeforePrint = null; // ver beforeprint/afterprint en init() — restaura el escalado móvil tras imprimir

let autoRefreshTimer = null;
let lastLoadedRows = []; // guarda la última carga ya enriquecida, para exportar sin repetir el fetch

// ---- Arranque: ¿ya hay sesión? ----
init();

async function init() {
  // Versión del sitio en los pies de login / cuenta pendiente / Dashboard (ver version.js).
  document.querySelectorAll('[data-app-version]').forEach(el => { el.textContent = 'v' + APP_VERSION; });
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

  // Reubicación del buscador en móvil — pedido explícito del usuario: "que el buscador en
  // la version movil quede arriba de la lista de empleados". El buscador vive dentro de
  // .filters (junto a Sucursal/Desde/Hasta) porque ahí tiene sentido en escritorio, pero
  // en móvil queda lejos de la tabla (separado por Sucursal/Desde/Hasta arriba, y por
  // Relojes checadores + las 4 tarjetas KPI + los botones de acción abajo). En vez de
  // duplicarlo (dos <input id="search-input"> confundirían al resto del código), se MUEVE
  // el mismo nodo del DOM justo antes de la tabla cuando el ancho es de móvil, y se
  // regresa a .filters cuando deja de serlo — placeSearchFieldForViewport() se llama una
  // vez al inicio y cada vez que se cruza el punto de quiebre de 640px (matchMedia
  // 'change', no un listener de resize genérico: solo dispara al cruzar el umbral, no en
  // cada pixel). En escritorio el resultado es idéntico a como estaba siempre.
  placeSearchFieldForViewport();
  mobileSearchQuery.addEventListener('change', placeSearchFieldForViewport);

  reportButton.addEventListener('click', openAttendanceReport);
  wrClose.addEventListener('click', closeWeeklyReport);
  weeklyReportModal.addEventListener('click', (event) => {
    if (event.target === weeklyReportModal) closeWeeklyReport();
  });
  wrPrev.addEventListener('click', () => shiftReportWeek(-1));
  wrNext.addEventListener('click', () => shiftReportWeek(1));
  wrRefresh.addEventListener('click', onWeeklyReportRefreshClick);
  wrCsv.addEventListener('click', onExportReportCsvClick);
  wrPreview.addEventListener('click', openReportSheet);
  wrSearch.addEventListener('input', debounce(applyReportFilters, 150));
  wrBranch.addEventListener('change', applyReportFilters);
  wrDept.addEventListener('change', applyReportFilters);

  reportPreviewClose.addEventListener('click', closeReportPreview);
  reportPreviewModal.addEventListener('click', (event) => {
    if (event.target === reportPreviewModal) closeReportPreview();
  });
  // Pedido explícito del usuario: "al presionar la tecla ESC lo que quiero es que se
  // salga" — cierra el modal que esté abierto en ese momento, el de más arriba primero
  // (la hoja de impresión va encima de la ventana del reporte). No cierra nada si el foco
  // está en un <select> desplegado (ese caso el propio navegador ya usa Escape para cerrar el
  // desplegable, no para nuestro modal) ni si hay un diálogo nativo (alert/confirm) esperando.
  document.addEventListener('keydown', (event) => {
    if (event.key !== 'Escape') return;
    if (!reportPreviewModal.hidden) {
      closeReportPreview();
    } else if (!weeklyReportModal.hidden) {
      closeWeeklyReport();
    } else if (!usersModal.hidden) {
      closeUsersModal();
    }
  });
  previewPrintButton.addEventListener('click', () => window.print());
  previewExcelButton.addEventListener('click', onExportReportExcelClick);
  previewPdfButton.addEventListener('click', onExportReportPdfClick);
  // Recalcula el escalado de la hoja al girar el teléfono/tablet o cambiar de tamaño de
  // ventana — fitReportPreviewToViewport ya se sale sola si el modal está cerrado.
  window.addEventListener('resize', debounce(fitReportPreviewToViewport, 150));
  window.addEventListener('orientationchange', () => setTimeout(fitReportPreviewToViewport, 200));
  // El escalado es solo para la VISTA en pantalla — imprimir (Ctrl+P, el botón Imprimir, o
  // el propio menú del navegador) debe salir siempre a tamaño real, sin importar qué tan
  // achicada se estuviera viendo la hoja en un teléfono. beforeprint/afterprint cubren
  // cualquier forma de imprimir, no solo el botón.
  window.addEventListener('beforeprint', () => {
    reportScaleBeforePrint = resetReportPreviewScale();
  });
  window.addEventListener('afterprint', () => {
    restoreReportPreviewScale(reportScaleBeforePrint);
  });

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

    // data-label en cada <td> — mismo criterio de tarjetas en móvil que la tabla de
    // asistencia (ver styles.css, sección "Móvil y tablet").
    tr.innerHTML = `
      <td data-label="Nombre">${nameCell}</td>
      <td data-label="Correo">${escapeHtml(user.email ?? '')}</td>
      <td data-label="Rol">${roleCell}</td>
      <td data-label="Último acceso">${lastSignIn}</td>
      <td data-label="Acciones" style="text-align:right; white-space:nowrap;">
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
  const details = [];
  for (const device of data) {
    const lastCommMs = device.last_communication_at_utc ? new Date(device.last_communication_at_utc).getTime() : null;
    const minutesAgo = lastCommMs ? (now - lastCommMs) / 60_000 : null;
    const isOnline = minutesAgo !== null && minutesAgo <= DEVICE_ONLINE_THRESHOLD_MINUTES;
    if (isOnline) anyOnline = true;
    details.push(`${device.name}: ${isOnline ? 'conectado' : describeOffline(minutesAgo).toLowerCase()}`);

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
  // El detalle (al pasar el ratón) dice el estado de cada reloj y, si está desconectado, hace cuánto
  // que no se comunica — "Conectado" exige una comunicación de la PC con el reloj de hace ≤ 5 min
  // (ver DEVICE_ONLINE_THRESHOLD_MINUTES); la app de escritorio la refresca con cada descarga.
  setConnectionBadge(anyOnline, details.join(' · '));
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
      if (!weeklyReportModal.hidden) loadWeeklyReport(); // el reporte abierto se actualiza solo al terminar la PC
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
  wrSyncNote.textContent = text; // misma nota dentro de la ventana del reporte
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
  // Horario esperado + "horario especial" (ver PunctualityClassifier del repo principal,
  // RelojChecador.Application.Attendances) — pedido explícito del usuario: "verde
  // asistencia puntual, amarillo retardo (tolerancia 10 mns), rojo falta" también en el
  // Dashboard web. Recién sincronizado a Supabase (ver migración add_employee_schedule),
  // antes ni siquiera el horario llegaba aquí.
  const employeeScheduleById = new Map();
  if (employeeIdsToResolve.size > 0) {
    const { data: employees } = await supabase
      .from('employees')
      .select('id, full_name, number, department, scheduled_start_time, scheduled_end_time, has_special_schedule')
      .in('id', [...employeeIdsToResolve]);
    for (const e of employees ?? []) {
      employeeNameById.set(e.id, e.full_name);
      employeeNumberById.set(e.id, e.number);
      employeeDepartmentById.set(e.id, e.department);
      employeeScheduleById.set(e.id, {
        scheduledStartTime: e.scheduled_start_time, // "HH:mm:ss" o null
        scheduledEndTime: e.scheduled_end_time,
        hasSpecialSchedule: e.has_special_schedule === true,
      });
    }
  }

  return attendances.map(a => {
    const resolvedEmployeeId = a.employee_id ?? mappingByDeviceAndPin.get(`${a.device_id}|${a.device_user_pin}`) ?? null;
    const employeeName = resolvedEmployeeId ? employeeNameById.get(resolvedEmployeeId) : null;
    const schedule = resolvedEmployeeId ? employeeScheduleById.get(resolvedEmployeeId) : null;
    return {
      ...a,
      branchName: branchNameById.get(a.branch_id) ?? '—',
      deviceName: deviceNameById.get(a.device_id) ?? '—',
      employeeName: employeeName ?? null,
      resolvedEmployeeId: resolvedEmployeeId,
      employeeNumber: resolvedEmployeeId ? (employeeNumberById.get(resolvedEmployeeId) ?? null) : null,
      employeeDepartment: resolvedEmployeeId ? (employeeDepartmentById.get(resolvedEmployeeId) ?? null) : null,
      employeeScheduledStartTime: schedule?.scheduledStartTime ?? null,
      employeeScheduledEndTime: schedule?.scheduledEndTime ?? null,
      employeeHasSpecialSchedule: schedule?.hasSpecialSchedule ?? false,
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
    // Sucursal, Dispositivo — mismo orden en index.html (encabezado <thead>). data-label
    // en cada <td> — optimización móvil: en pantallas angostas la tabla se convierte en
    // una lista de tarjetas (ver styles.css, sección "Móvil y tablet") y cada valor
    // necesita su propia etiqueta ahí, porque el <thead> se oculta.
    tr.innerHTML = `
      <td data-label="PIN">${escapeHtml(row.device_user_pin)}</td>
      <td data-label="Empleado">${employeeCell}</td>
      <td data-label="Tipo">${punchLabel}</td>
      <td data-label="Fecha y hora">${formatAttendanceDateTime(row.timestamp_utc)}</td>
      <td data-label="Método">${escapeHtml(mapVerifyMethod(row.verify_method))}</td>
      <td data-label="Sucursal">${escapeHtml(row.branchName)}</td>
      <td data-label="Dispositivo">${escapeHtml(row.deviceName)}</td>
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
    // Generada sola por el sistema cuando un empleado marcó Entrada pero nunca volvió a
    // checar — ver AttendanceAutoCloser en el repo principal ("si el empleado no checa a
    // su hora de salida esta se marca automáticamente para que no sigan corriendo las
    // horas"). Distinta de 'Manual': esa es una corrección del administrador, esta la
    // generó el sistema solo.
    case 'Automatic': return 'Automático';
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

/// Mueve el <div class="filter-field"> del buscador principal (#search-input) entre
/// .filters (su lugar de siempre, en escritorio) y justo antes de la tabla (en móvil) —
/// ver el comentario junto a mobileSearchQuery.addEventListener en init(). Idempotente:
/// no hace nada si el campo ya está donde debe.
function placeSearchFieldForViewport() {
  if (mobileSearchQuery.matches) {
    if (tableSection.firstElementChild !== searchFieldWrap) {
      tableSection.insertBefore(searchFieldWrap, tableSection.firstElementChild);
    }
  } else if (searchFieldWrap.parentElement !== filtersSection) {
    filtersSection.appendChild(searchFieldWrap);
  }
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

/// "Ahora" con el mismo criterio que timestamp_utc en toda la base: NO es UTC real, es la
/// hora de pared del negocio sin convertir — así que para compararlo con eso "ahora" se arma con
/// los componentes de la hora LOCAL DEL NAVEGADOR (no Date.now()/toISOString(), que sí son UTC
/// real y se desfasarían por el huso horario de quien vea el Dashboard). Asume que quien lo ve
/// está en el mismo huso que el negocio — mismo supuesto que el resto de la app.
function nowAsFakeUtcIso() {
  const now = new Date();
  const pad = (n) => String(n).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}` +
    `T${pad(now.getHours())}:${pad(now.getMinutes())}:${pad(now.getSeconds())}.000Z`;
}

function todayIso() {
  return nowAsFakeUtcIso().slice(0, 10);
}

/// Abre la ventana del reporte en la semana en curso.
function openAttendanceReport() {
  reportCanSeePay = currentProfile?.role === 'admin' && currentProfile?.status === 'approved';
  reportWeekStart = getWeekStartIso(todayIso());

  wrSearch.value = '';
  wrDept.value = '';
  wrBranch.innerHTML = '<option value="">Todas las sucursales</option>';
  for (const option of branchSelect.options) {
    if (!option.value) continue;
    const copy = document.createElement('option');
    copy.value = option.value;
    copy.textContent = option.textContent;
    wrBranch.appendChild(copy);
  }

  weeklyReportModal.hidden = false;
  loadWeeklyReport();
  ensureExportLibrariesLoaded(); // en segundo plano — Excel/PDF no bloquean la ventana
}

function closeWeeklyReport() {
  weeklyReportModal.hidden = true;
}

function shiftReportWeek(weeks) {
  reportWeekStart = addDaysIso(reportWeekStart, weeks * 7);
  loadWeeklyReport();
}

/// Trae TODAS las filas de una consulta paginando de 1000 en 1000 (PostgREST recorta cada
/// respuesta a 1000). buildQuery debe devolver una consulta NUEVA en cada llamada y con orden
/// determinista, para que las páginas no se traslapen.
async function fetchAllPages(buildQuery) {
  const pageSize = 1000;
  const all = [];
  for (let from = 0; ; from += pageSize) {
    const { data, error } = await buildQuery().range(from, from + pageSize - 1);
    if (error) throw new Error(error.message);
    all.push(...(data ?? []));
    if (!data || data.length < pageSize) return all;
  }
}

async function loadWeeklyReport() {
  const token = ++reportLoadToken;
  const weekStart = reportWeekStart;
  const weekEnd = addDaysIso(weekStart, 6);

  wrRange.textContent = `${longDate(weekStart)} – ${longDate(weekEnd)}`;
  wrStatus.textContent = 'Calculando…';
  wrThead.innerHTML = '';
  wrTbody.innerHTML = '';

  try {
    const data = await fetchWeeklyReportData(weekStart, weekEnd, reportCanSeePay);
    if (token !== reportLoadToken) return; // ya se pidió otra semana
    const built = buildWeeklyReportRows(data, weekStart);
    reportAllRows = built.rows;
    reportUnlinkedCount = built.unlinkedCount;
    populateReportDepartmentOptions(reportAllRows);
    applyReportFilters();
  } catch (error) {
    if (token !== reportLoadToken) return;
    reportAllRows = [];
    reportVisibleRows = [];
    wrStatus.textContent = 'No se pudo cargar el reporte: ' + error.message;
  }
}

/// Todo lo que necesita el reporte de UNA semana. Los montos solo se piden si la cuenta es Admin.
async function fetchWeeklyReportData(weekStart, weekEnd, canSeePay) {
  const employeeColumns = 'id, number, full_name, branch_id, department, scheduled_start_time, has_special_schedule' +
    (canSeePay ? ', weekly_salary, overtime_hourly_rate' : '');
  // timestamp_utc se compara tal cual está guardado (hora de pared con sufijo "Z"), sin convertir.
  const fromTs = `${weekStart}T00:00:00.000Z`;
  const toTs = `${weekEnd}T23:59:59.999Z`;

  const [employees, attendances, mappings, branches, deductions] = await Promise.all([
    // Igual que la PC: todos menos los dados de baja.
    fetchAllPages(() => supabase.from('employees').select(employeeColumns).neq('status', 'Terminated').order('full_name').order('id')),
    fetchAllPages(() => supabase.from('attendances')
      .select('id, device_id, employee_id, device_user_pin, timestamp_utc, punch_type')
      .gte('timestamp_utc', fromTs).lte('timestamp_utc', toTs).order('timestamp_utc').order('id')),
    fetchAllPages(() => supabase.from('employee_device_mappings').select('id, device_id, device_user_pin, employee_id').order('id')),
    fetchAllPages(() => supabase.from('branches').select('id, name').order('id')),
    canSeePay
      ? fetchAllPages(() => supabase.from('payroll_deductions')
        .select('id, employee_id, isr_amount, imss_amount, other_amount, other_label, notes').eq('week_start', weekStart).order('id'))
      : Promise.resolve([]),
  ]);
  return { employees, attendances, mappings, branches, deductions };
}

/// Arma una fila por empleado activo con su semana calculada (payroll-calc.js). Resuelve a
/// quién es cada marcación igual que la PC: primero su EmployeeId, y si ese empleado ya está dado
/// de baja (catálogo reemplazado) o no tiene, por el vínculo (dispositivo, PIN) → empleado vigente.
function buildWeeklyReportRows(data, weekStart) {
  const { employees, attendances, mappings, branches, deductions } = data;
  const branchNameById = new Map(branches.map(b => [b.id, b.name]));
  const activeById = new Map(employees.map(e => [e.id, e]));
  const mappedEmployee = new Map(mappings.map(m => [`${m.device_id}|${m.device_user_pin}`, m.employee_id]));

  const punchesByEmployee = new Map();
  let unlinkedCount = 0;
  for (const a of attendances) {
    const viaMapping = mappedEmployee.get(`${a.device_id}|${a.device_user_pin}`);
    const owner = activeById.has(a.employee_id) ? a.employee_id : (activeById.has(viaMapping) ? viaMapping : null);
    if (!owner) {
      unlinkedCount++;
      continue;
    }
    if (!punchesByEmployee.has(owner)) punchesByEmployee.set(owner, []);
    punchesByEmployee.get(owner).push({ timestamp_utc: a.timestamp_utc, punch_type: a.punch_type });
  }

  const deductionsByEmployee = new Map(deductions.map(d => [d.employee_id, {
    isr: Number(d.isr_amount ?? 0), imss: Number(d.imss_amount ?? 0), other: Number(d.other_amount ?? 0),
    label: d.other_label ?? null, notes: d.notes ?? null,
  }]));

  const today = todayIso();
  const rows = employees.map(e => {
    const week = calculateWeek({
      scheduledStartTime: e.scheduled_start_time ?? null,
      hasSpecialSchedule: e.has_special_schedule === true,
      // undefined = esta cuenta no ve montos (no se descargaron); null = sueldo pendiente de captura.
      weeklySalary: reportCanSeePay ? (e.weekly_salary === null || e.weekly_salary === undefined ? null : Number(e.weekly_salary)) : undefined,
      overtimeHourlyRate: e.overtime_hourly_rate === null || e.overtime_hourly_rate === undefined ? null : Number(e.overtime_hourly_rate),
    }, weekStart, punchesByEmployee.get(e.id) ?? [], today);
    const employeeDeductions = deductionsByEmployee.get(e.id) ?? { isr: 0, imss: 0, other: 0, label: null, notes: null };
    return {
      id: e.id,
      number: e.number ?? '',
      name: e.full_name,
      branchId: e.branch_id,
      branchName: branchNameById.get(e.branch_id) ?? '(sucursal desconocida)',
      department: e.department ?? '',
      week,
      deductions: employeeDeductions,
      net: reportCanSeePay ? netPay(week.totalPay, employeeDeductions) : null,
    };
  });
  return { rows, unlinkedCount };
}

function populateReportDepartmentOptions(rows) {
  const previous = wrDept.value;
  const departments = [...new Set(rows.map(r => r.department).filter(Boolean))].sort((a, b) => a.localeCompare(b, 'es-MX'));
  wrDept.innerHTML = '<option value="">Todos los deptos.</option>';
  for (const department of departments) {
    const option = document.createElement('option');
    option.value = department;
    option.textContent = department;
    wrDept.appendChild(option);
  }
  wrDept.value = departments.includes(previous) ? previous : '';
}

/// Búsqueda por número o nombre + Sucursal + Departamento, en memoria (sin volver a consultar).
function applyReportFilters() {
  const term = wrSearch.value.trim().toLowerCase();
  reportVisibleRows = reportAllRows.filter(r =>
    (!term || r.name.toLowerCase().includes(term) || r.number.toLowerCase().includes(term)) &&
    (!wrBranch.value || r.branchId === wrBranch.value) &&
    (!wrDept.value || r.department === wrDept.value));
  renderWeeklyReport();
}

function reportWeekRangeText() {
  return `${longDate(reportWeekStart)} – ${longDate(addDaysIso(reportWeekStart, 6))}`;
}

function reportBranchFilterText() {
  return wrBranch.value ? (wrBranch.options[wrBranch.selectedIndex]?.textContent ?? '') : 'Todas las sucursales';
}

/// Insignia de un día, como la de la PC: color del semáforo con las horas del día (o Descanso /
/// Falta / —) y, debajo, la hora de entrada y de salida.
function dayBadgeHtml(day) {
  const sub = day.status === 'worked' ? `${day.entry}–${day.exit ?? '…'}` : '';
  const title = day.status === 'worked'
    ? `${dayLabel(day.date)} ${shortDate(day.date)} — marcaciones: ${day.allPunches.join(', ')}`
    : `${dayLabel(day.date)} ${shortDate(day.date)}`;
  return `<div class="wr-day wr-day--${day.color}" title="${escapeHtml(title)}">` +
    `<div class="wr-day-head"><b>${dayLabel(day.date)}</b> ${escapeHtml(dayBadgeText(day))}</div>` +
    `<div class="wr-day-sub">${escapeHtml(sub)}</div></div>`;
}

function salaryText(week) {
  return week.weeklySalary === null ? 'Pendiente' : formatMoney(week.weeklySalary);
}

function renderWeeklyReport() {
  const admin = reportCanSeePay;
  wrThead.innerHTML = `<tr>
    <th>Empleado</th><th>Sucursal / Depto.</th><th>Checadas de la semana</th>
    <th class="ctr">Faltas</th><th>Horas normales</th><th>Horas extra</th>
    ${admin ? '<th class="num">Sueldo semanal</th><th class="num">Pago horas extra</th><th class="num">Total a pagar</th><th class="num">Deducciones</th><th class="num">Neto a pagar</th>' : ''}
    <th>Advertencias</th></tr>`;

  const withWarnings = reportVisibleRows.filter(r => r.week.warnings.length > 0).length;
  const hidden = reportAllRows.length - reportVisibleRows.length;
  let status = `<strong>${reportVisibleRows.length}</strong> empleado(s)` +
    (hidden > 0 ? ` de ${reportAllRows.length} (${hidden} oculto(s) por los filtros)` : '') +
    ` — ${withWarnings} con advertencias en su cálculo de horas (ver columna "Advertencias").`;
  if (reportUnlinkedCount > 0) {
    status += ` ⚠ ${reportUnlinkedCount} marcación(es) de esta semana con PIN sin vincular a un empleado vigente (no se cuentan).`;
  }
  wrStatus.innerHTML = status;

  if (reportVisibleRows.length === 0) {
    wrTbody.innerHTML = `<tr><td colspan="${admin ? 12 : 7}" class="weekly-report-empty">Sin empleados para estos filtros.</td></tr>`;
    return;
  }

  wrTbody.innerHTML = reportVisibleRows.map(r => {
    const week = r.week;
    const deductionTotal = r.deductions.isr + r.deductions.imss + r.deductions.other;
    const deductionTitle = `ISR ${formatMoney(r.deductions.isr)} · IMSS ${formatMoney(r.deductions.imss)} · Otro ${formatMoney(r.deductions.other)}` +
      (r.deductions.label ? ` (${r.deductions.label})` : '');
    return `<tr>
      <td><div class="wr-emp-name">${escapeHtml(r.name)}</div><div class="wr-emp-number">${escapeHtml(r.number)}</div></td>
      <td><div class="wr-branch">${escapeHtml(r.branchName)}</div><div class="wr-dept">${escapeHtml(r.department)}</div></td>
      <td><div class="wr-days">${week.days.map(dayBadgeHtml).join('')}</div></td>
      <td class="ctr">${week.absenceCount}</td>
      <td>${formatHoursAndMinutes(week.totalRegularMs)}</td>
      <td>${formatHoursAndMinutes(week.totalOvertimeMs)}</td>
      ${admin ? `<td class="num">${salaryText(week)}</td>
      <td class="num">${formatMoney(week.overtimePay)}</td>
      <td class="num">${formatMoney(week.totalPay)}</td>
      <td class="num" title="${escapeHtml(deductionTitle)}">${formatMoney(deductionTotal)}</td>
      <td class="num">${formatMoney(r.net)}</td>` : ''}
      <td><div class="wr-warn" title="${escapeHtml(week.warnings.join('\n'))}">${escapeHtml(week.warnings.join(' | '))}</div></td>
    </tr>`;
  }).join('');
}

// ---------------------------------------------------------------- columnas de nómina (hoja / Excel)

/// Las mismas columnas que la "Vista previa e imprimir" y el Excel de la PC (PayrollReportDocumentBuilder
/// y PayrollExcelExporter) más Faltas. `width` es el % de ancho en la hoja Carta horizontal;
/// `money` marca las columnas de importe (número real en Excel, alineadas a la derecha).
function payrollColumns() {
  const base = [
    { header: 'Empleado', width: 13, text: r => r.name, value: r => r.name },
    { header: 'Sucursal', width: 8, text: r => r.branchName, value: r => r.branchName },
    { header: 'Departamento', width: 9, text: r => r.department || '—', value: r => r.department },
    { header: 'Faltas', width: 5, num: true, text: r => String(r.week.absenceCount), value: r => r.week.absenceCount },
    { header: 'Horas normales', width: 8, text: r => formatHoursAndMinutes(r.week.totalRegularMs), value: r => formatHoursAndMinutes(r.week.totalRegularMs) },
    { header: 'Horas extra', width: 7, text: r => formatHoursAndMinutes(r.week.totalOvertimeMs), value: r => formatHoursAndMinutes(r.week.totalOvertimeMs) },
  ];
  if (!reportCanSeePay) {
    // Sin montos: se reparte el ancho entre las 6 columnas.
    const widths = [30, 18, 20, 8, 12, 12];
    return base.map((c, i) => ({ ...c, width: widths[i] }));
  }
  return [
    ...base,
    { header: 'Sueldo semanal', width: 9, money: true, text: r => salaryText(r.week), value: r => r.week.weeklySalary === null ? 'Pendiente' : r.week.weeklySalary },
    { header: 'Pago horas extra', width: 8, money: true, text: r => formatMoney(r.week.overtimePay), value: r => r.week.overtimePay },
    { header: 'Total a pagar', width: 9, money: true, text: r => formatMoney(r.week.totalPay), value: r => r.week.totalPay },
    { header: 'ISR', width: 6, money: true, text: r => formatMoney(r.deductions.isr), value: r => r.deductions.isr },
    { header: 'IMSS', width: 6, money: true, text: r => formatMoney(r.deductions.imss), value: r => r.deductions.imss },
    { header: 'Otro', width: 6, money: true, text: r => formatMoney(r.deductions.other), value: r => r.deductions.other },
    { header: 'Neto a pagar', width: 9, money: true, text: r => formatMoney(r.net), value: r => r.net },
  ];
}

/// "Vista previa e imprimir": abre la hoja Carta horizontal con lo que esté filtrado.
function openReportSheet() {
  if (reportVisibleRows.length === 0) {
    alert('No hay empleados en este reporte para mostrar.');
    return;
  }

  const columns = payrollColumns();
  previewRangeText.textContent = `Semana ${reportWeekRangeText()} — ${reportBranchFilterText()}`;
  previewGeneratedText.textContent = `Generado el ${formatDateTime(new Date().toISOString())}`;
  previewThead.innerHTML = `<tr>${columns.map(c =>
    `<th class="${c.money || c.num ? 'num' : ''}" style="width:${c.width}%">${escapeHtml(c.header)}</th>`).join('')}</tr>`;
  previewTbody.innerHTML = reportVisibleRows.map(r =>
    `<tr>${columns.map(c => `<td class="${c.money || c.num ? 'num' : ''}">${escapeHtml(c.text(r))}</td>`).join('')}</tr>`).join('');
  previewEmptyText.hidden = true;

  reportPreviewModal.hidden = false;
  fitReportPreviewToViewport();
  ensureExportLibrariesLoaded();
}

/// Optimización móvil/tablet — pedido explícito del usuario: "evitar scroll horizontal
/// innecesario". La hoja del reporte SIEMPRE mide 8.5in (816px) de ancho real (necesario
/// para que se imprima/exporte a Carta tal cual — ver .report-preview-page), pero en una
/// pantalla angosta eso obligaba a hacer scroll horizontal para ver el resto de la tabla.
///
/// Usa transform:scale (NO la propiedad CSS "zoom") sobre report-preview-page, dentro de
/// report-preview-page-frame — un marco al que se le fija a mano el ancho/alto YA
/// escalados. Motivo del cambio (bug real reportado por el usuario, con captura: "sale
/// desfasado el reporte de asistencia"): report-preview-page es hijo directo de un
/// contenedor flex (report-preview-scroll, display:flex; justify-content:center) —
/// "zoom" cambia cómo se VE el elemento pero, aplicado a un hijo flex, algunos
/// navegadores (Safari/iOS entre ellos) NO recalculan bien cuánto espacio ocupa para el
/// centrado/scroll del contenedor, dejando la hoja ya achicada "flotando" desfasada
/// dentro del hueco de 816px sin escalar. transform:scale nunca tiene ese problema porque
/// NUNCA afecta el layout — por eso hace falta el marco: sin él, el contenedor seguiría
/// centrando el tamaño SIN escalar (el mismo bug, solo que con otra propiedad).
///
/// Nunca agranda por encima de 100% (min con 1) — en pantallas anchas la hoja se ve a su
/// tamaño real, igual que siempre.
function fitReportPreviewToViewport() {
  if (reportPreviewModal.hidden) return;
  const scrollContainer = reportPreviewPageFrame.parentElement;
  if (!scrollContainer) return;

  // Mide SIEMPRE a tamaño real primero — si no, un cálculo anterior (por ejemplo, antes
  // de rotar el teléfono) contaminaría la medición de este.
  reportPreviewPage.style.transform = '';
  reportPreviewPageFrame.style.width = '';
  reportPreviewPageFrame.style.height = '';

  const style = getComputedStyle(scrollContainer);
  const paddingX = parseFloat(style.paddingLeft || '0') + parseFloat(style.paddingRight || '0');
  const available = scrollContainer.clientWidth - paddingX;
  const naturalWidth = reportPreviewPage.offsetWidth;
  const naturalHeight = reportPreviewPage.offsetHeight;
  if (naturalWidth <= 0) return;

  const scale = Math.min(1, available / naturalWidth);
  if (scale >= 1) return; // tamaño real — nada que ajustar (ya se dejó así arriba)

  reportPreviewPage.style.transform = `scale(${scale})`;
  reportPreviewPage.style.transformOrigin = 'top left';
  reportPreviewPageFrame.style.width = `${naturalWidth * scale}px`;
  reportPreviewPageFrame.style.height = `${naturalHeight * scale}px`;
}

/// Reset a tamaño real antes de imprimir/exportar (ver beforeprint/afterprint en init() y
/// onExportReportPdfClick) — devuelve {transform, width, height} para poder restaurarlo
/// después con restoreReportPreviewScale().
function resetReportPreviewScale() {
  const previous = {
    transform: reportPreviewPage.style.transform,
    width: reportPreviewPageFrame.style.width,
    height: reportPreviewPageFrame.style.height,
  };
  reportPreviewPage.style.transform = '';
  reportPreviewPageFrame.style.width = '';
  reportPreviewPageFrame.style.height = '';
  return previous;
}

function restoreReportPreviewScale(previous) {
  if (!previous) return;
  reportPreviewPage.style.transform = previous.transform;
  reportPreviewPageFrame.style.width = previous.width;
  reportPreviewPageFrame.style.height = previous.height;
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

/// "Actualizar" dentro del reporte: recarga los datos y, a la vez, le pide a la PC del negocio que
/// baje las marcaciones del reloj físico (el MISMO pedido del botón "🔄 Actualizar asistencias" —
/// ver onSyncRequestClick). Cuando la PC termina, pollSyncRequest vuelve a cargar esta tabla.
async function onWeeklyReportRefreshClick() {
  loadWeeklyReport();
  try {
    await onSyncRequestClick();
  } catch (error) {
    wrSyncNote.textContent = 'No se pudo pedir la actualización al reloj: ' + error.message;
  }
}

/// CSV de lo que se ve (ver buildAttendanceReportCsv en payroll-calc.js).
function onExportReportCsvClick() {
  if (reportVisibleRows.length === 0) return;
  const csv = buildAttendanceReportCsv(reportVisibleRows, reportCanSeePay);
  const blob = new Blob(['﻿' + csv], { type: 'text/csv;charset=utf-8;' });
  saveBlobWithPicker(blob, `reporte-asistencia-semana-${reportWeekStart}.csv`, 'Archivo CSV', 'text/csv', '.csv');
}

/// Excel de lo que se ve — mismas columnas que la hoja y que el Excel de la PC (PayrollExcelExporter).
async function onExportReportExcelClick() {
  const originalLabel = previewExcelButton.textContent;
  previewExcelButton.disabled = true;
  previewExcelButton.textContent = 'Preparando…';
  try {
    await ensureExportLibrariesLoaded();

    const columns = payrollColumns();
    const sheetRows = [
      ['Drive In Car Wash — Reporte de Asistencia'],
      [`Semana ${reportWeekRangeText()} — ${reportBranchFilterText()}`],
      [],
      columns.map(c => c.header),
      ...reportVisibleRows.map(r => columns.map(c => {
        const value = c.value(r);
        return typeof value === 'number' && c.money ? Number(value.toFixed(2)) : value;
      })),
    ];
    const worksheet = XLSX.utils.aoa_to_sheet(sheetRows);
    worksheet['!cols'] = columns.map(c => ({ wch: Math.max(12, Math.round(c.width * 1.6)) }));
    const workbook = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(workbook, worksheet, 'Asistencia');

    const xlsxBytes = XLSX.write(workbook, { type: 'array', bookType: 'xlsx' });
    const blob = new Blob([xlsxBytes], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
    await saveBlobWithPicker(
      blob, `reporte-asistencia-semana-${reportWeekStart}.xlsx`,
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
  // Igual que con imprimir: el escalado es solo para verse completa en pantallas angostas
  // (ver fitReportPreviewToViewport) — html2canvas debe capturar la hoja a tamaño real,
  // si no el PDF saldría con todo achicado en vez de a tamaño Carta real.
  const scaleBeforeExport = resetReportPreviewScale();
  try {
    await ensureExportLibrariesLoaded();

    const canvas = await html2canvas(reportPreviewPage, { scale: 2, backgroundColor: '#FFFFFF' });
    const imgData = canvas.toDataURL('image/png');

    // Tamaño Carta HORIZONTAL (11in x 8.5in): una columna por día de la semana no cabe en
    // vertical.
    const { jsPDF } = window.jspdf;
    const pdf = new jsPDF({ unit: 'in', format: 'letter', orientation: 'landscape' });
    const pageWidthIn = 11;
    const pageHeightIn = 8.5;
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
    restoreReportPreviewScale(scaleBeforeExport);
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
