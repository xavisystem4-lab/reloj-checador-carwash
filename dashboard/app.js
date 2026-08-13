// Dashboard de reportes de Reloj Checador — sitio estático, sin backend propio: lee
// directo de Supabase (proyecto dedicado reloj-checador-carwash) usando la AnonKey
// (segura de embeber aquí — está pensada para ser pública, protegida por RLS: la
// migración initial_schema solo permite SELECT a usuarios autenticados, nunca INSERT/
// UPDATE/DELETE desde el navegador — ver src/RelojChecador.Infrastructure.Cloud/README.md
// del repo principal para el detalle completo de la arquitectura de sincronización).
//
// A propósito NO hay flujo de "crear cuenta" en este archivo: las cuentas del Dashboard
// se crean directo en el panel de Supabase (Authentication → Users → Add user) — así
// nunca hay una vía de auto-registro abierta para leer datos de asistencia del negocio.
import { createClient } from 'https://esm.sh/@supabase/supabase-js@2';

const SUPABASE_URL = 'https://vkvlucpjgvqrlvevcimq.supabase.co';
const SUPABASE_ANON_KEY = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InZrdmx1Y3BqZ3Zxcmx2ZXZjaW1xIiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODY2MDQ1MTQsImV4cCI6MjEwMjE4MDUxNH0.RWTJLCXhsPbSJLNpO2V2HNkhKqstqWgx33rkLekUxFI';

const supabase = createClient(SUPABASE_URL, SUPABASE_ANON_KEY);

const REFRESH_INTERVAL_MS = 5_000;
const MAX_ROWS = 2000;

// La app de escritorio solo actualiza LastCommunicationAtUtc cuando alguien le da
// "Conectar" con éxito en la pantalla de Dispositivos — no en cada ciclo de
// sincronización (ese ciclo solo reenvía el valor ya guardado). Por eso "Conectado" aquí
// se basa en qué tan RECIENTE es esa marca, no en un simple booleano — evita que un
// dispositivo quede "Conectado" para siempre solo porque una vez funcionó.
const DEVICE_ONLINE_THRESHOLD_MINUTES = 5;

// ---- Referencias al DOM ----
const loginScreen = document.getElementById('login-screen');
const dashboardScreen = document.getElementById('dashboard-screen');
const loginForm = document.getElementById('login-form');
const emailInput = document.getElementById('email-input');
const passwordInput = document.getElementById('password-input');
const loginError = document.getElementById('login-error');
const loginButton = document.getElementById('login-button');
const userNameButton = document.getElementById('user-name-button');
const logoutButton = document.getElementById('logout-button');

const usersButton = document.getElementById('users-button');
const usersModal = document.getElementById('users-modal');
const usersModalClose = document.getElementById('users-modal-close');
const inviteForm = document.getElementById('invite-form');
const inviteNameInput = document.getElementById('invite-name-input');
const inviteEmailInput = document.getElementById('invite-email-input');
const inviteButton = document.getElementById('invite-button');
const inviteError = document.getElementById('invite-error');
const inviteSuccess = document.getElementById('invite-success');
const usersListStatus = document.getElementById('users-list-status');
const usersTbody = document.getElementById('users-tbody');

const branchSelect = document.getElementById('branch-select');
const fromInput = document.getElementById('from-input');
const toInput = document.getElementById('to-input');
const searchInput = document.getElementById('search-input');
const refreshButton = document.getElementById('refresh-button');
const exportButton = document.getElementById('export-button');

const kpiTotal = document.getElementById('kpi-total');
const kpiEmployees = document.getElementById('kpi-employees');
const kpiDevices = document.getElementById('kpi-devices');
const kpiUnlinked = document.getElementById('kpi-unlinked');

const tableStatus = document.getElementById('table-status');
const tableBody = document.getElementById('attendance-tbody');
const devicesStatusRow = document.getElementById('devices-status-row');

let autoRefreshTimer = null;
let lastLoadedRows = []; // guarda la última carga ya enriquecida, para exportar sin repetir el fetch

// ---- Arranque: ¿ya hay sesión? ----
init();

async function init() {
  const { data: { session } } = await supabase.auth.getSession();
  applySessionState(session);

  supabase.auth.onAuthStateChange((_event, session) => {
    applySessionState(session);
  });

  const today = new Date();
  const weekAgo = new Date(today);
  weekAgo.setDate(weekAgo.getDate() - 7);
  fromInput.value = toDateInputValue(weekAgo);
  toInput.value = toDateInputValue(today);

  loginForm.addEventListener('submit', onLoginSubmit);
  logoutButton.addEventListener('click', onLogoutClick);
  refreshButton.addEventListener('click', () => { loadReport(); loadDevicesStatus(); });
  exportButton.addEventListener('click', onExportClick);
  branchSelect.addEventListener('change', () => loadReport());
  fromInput.addEventListener('change', () => loadReport());
  toInput.addEventListener('change', () => loadReport());
  searchInput.addEventListener('input', debounce(() => renderTable(lastLoadedRows), 200));

  userNameButton.addEventListener('click', onEditOwnNameClick);
  usersButton.addEventListener('click', openUsersModal);
  usersModalClose.addEventListener('click', closeUsersModal);
  usersModal.addEventListener('click', (event) => {
    if (event.target === usersModal) closeUsersModal(); // clic fuera de la tarjeta
  });
  inviteForm.addEventListener('submit', onInviteSubmit);
}

let currentSession = null;

function applySessionState(session) {
  currentSession = session;
  if (session) {
    loginScreen.hidden = true;
    dashboardScreen.hidden = false;
    userNameButton.textContent = displayNameFor(session.user);
    startAutoRefresh();
    loadBranches().then(() => loadReport());
    loadDevicesStatus();
  } else {
    loginScreen.hidden = false;
    dashboardScreen.hidden = true;
    stopAutoRefresh();
    closeUsersModal();
  }
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
}

function mapAuthError(error) {
  const message = error?.message ?? '';
  if (message.includes('Invalid login credentials')) {
    return 'Correo o contraseña incorrectos.';
  }
  if (message.includes('Email not confirmed')) {
    return 'Esta cuenta todavía no confirma su correo.';
  }
  return 'No se pudo iniciar sesión: ' + message;
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
  userNameButton.textContent = displayNameFor(data.user);
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

    tr.innerHTML = `
      <td>${nameCell}</td>
      <td>${escapeHtml(user.email ?? '')}</td>
      <td>${lastSignIn}</td>
      <td style="text-align:right; white-space:nowrap;">
        <button class="btn-icon" data-action="edit" data-id="${user.id}" data-name="${escapeHtml(user.full_name ?? '')}" title="Editar nombre">✏️</button>
        ${isSelf ? '' : `<button class="btn-icon danger" data-action="delete" data-id="${user.id}" data-email="${escapeHtml(user.email ?? '')}" title="Eliminar acceso">🗑️</button>`}
      </td>
    `;
    fragment.appendChild(tr);
  }

  usersTbody.appendChild(fragment);
  usersTbody.querySelectorAll('button[data-action="edit"]').forEach(btn =>
    btn.addEventListener('click', () => onEditOtherUserName(btn.dataset.id, btn.dataset.name)));
  usersTbody.querySelectorAll('button[data-action="delete"]').forEach(btn =>
    btn.addEventListener('click', () => onDeleteUser(btn.dataset.id, btn.dataset.email)));
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
  const { data, error } = await supabase
    .from('devices')
    .select('id, name, last_communication_at_utc')
    .order('name');

  if (error) {
    console.error('No se pudo cargar el estado de los dispositivos:', error);
    return;
  }

  if (!data || data.length === 0) {
    devicesStatusRow.innerHTML = '<div class="devices-status-empty">Sin relojes checadores registrados.</div>';
    return;
  }

  const now = Date.now();
  devicesStatusRow.innerHTML = '';
  for (const device of data) {
    const lastCommMs = device.last_communication_at_utc ? new Date(device.last_communication_at_utc).getTime() : null;
    const minutesAgo = lastCommMs ? (now - lastCommMs) / 60_000 : null;
    const isOnline = minutesAgo !== null && minutesAgo <= DEVICE_ONLINE_THRESHOLD_MINUTES;

    const pill = document.createElement('div');
    pill.className = 'device-status-pill';
    pill.innerHTML = `
      <div class="device-status-dot ${isOnline ? 'online' : 'offline'}"></div>
      <div class="device-status-name">${escapeHtml(device.name)}</div>
      <div class="device-status-label">${isOnline ? 'Conectado' : describeOffline(minutesAgo)}</div>
    `;
    devicesStatusRow.appendChild(pill);
  }
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

// ---- Carga del reporte principal ----
async function loadReport() {
  tableStatus.textContent = 'Cargando…';

  const fromUtc = new Date(fromInput.value + 'T00:00:00').toISOString();
  const toUtc = new Date(toInput.value + 'T23:59:59.999').toISOString();

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

/// Resuelve nombres de sucursal, dispositivo y empleado para cada marcación. La
/// resolución de empleado tiene dos vías, en este orden: (1) Attendance.employee_id ya
/// vinculado directamente, (2) EmployeeDeviceMapping (device_id + pin) — la app de
/// escritorio todavía no resuelve esto automáticamente al guardar cada marcación (ver
/// DevicesViewModel.PersistAttendanceAsync en el repo principal), así que hoy casi
/// siempre caerá en la vía 2, o en "sin vincular" si tampoco hay mapeo.
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
  if (employeeIdsToResolve.size > 0) {
    const { data: employees } = await supabase
      .from('employees').select('id, full_name').in('id', [...employeeIdsToResolve]);
    for (const e of employees ?? []) {
      employeeNameById.set(e.id, e.full_name);
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
    tr.innerHTML = `<td colspan="6" style="text-align:center; color:var(--muted); padding:32px;">Sin marcaciones para estos filtros.</td>`;
    tableBody.appendChild(tr);
    return;
  }

  const fragment = document.createDocumentFragment();
  for (const row of filtered) {
    const tr = document.createElement('tr');

    const employeeCell = row.employeeName
      ? escapeHtml(row.employeeName)
      : `<span class="pill pill-unlinked">PIN ${escapeHtml(row.device_user_pin)} · sin vincular</span>`;

    const punchLabel = row.punch_type === 0
      ? '<span class="pill pill-in">Entrada</span>'
      : row.punch_type === 1
        ? '<span class="pill pill-out">Salida</span>'
        : '—';

    tr.innerHTML = `
      <td>${formatDateTime(row.timestamp_utc)}</td>
      <td>${employeeCell}</td>
      <td>${escapeHtml(row.branchName)}</td>
      <td>${escapeHtml(row.deviceName)}</td>
      <td>${escapeHtml(mapVerifyMethod(row.verify_method))}</td>
      <td>${punchLabel}</td>
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
    default: return 'Desconocido';
  }
}

function formatDateTime(isoUtc) {
  return new Date(isoUtc).toLocaleString('es-MX', { dateStyle: 'medium', timeStyle: 'short' });
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
    formatDateTime(r.timestamp_utc),
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

// ---- Auto-actualización: la app de escritorio sube cada ~5s (ver
// SupabaseSyncOptions.IntervalSeconds en el repo principal), así que refrescar aquí
// cada 5s también mantiene el Dashboard prácticamente al día. ----
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
