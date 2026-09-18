// Pruebas del port a JavaScript del cálculo semanal (dashboard/payroll-calc.js). Reproducen los
// casos de tests/RelojChecador.Application.Tests/Payroll/WorkedHoursCalculatorTests.cs y de
// PunctualityClassifierTests.cs — si una regla cambia en C#, cambia aquí también.
//
// Ejecutar:  node --test tests/dashboard
import test from 'node:test';
import assert from 'node:assert/strict';
import {
  addDaysIso, getWeekStartIso, dayLabel, calculateDay, calculateWeek, classifyDay,
  formatHoursAndMinutes, dayBadgeText, netPay, PUNCH,
} from '../../dashboard/payroll-calc.js';

const p = (ts, type) => ({ timestamp_utc: ts, punch_type: type });
const HOUR = 3_600_000;

// ---- fechas ----------------------------------------------------------------------------

test('la semana va de lunes a domingo (igual que WeekBoundary)', () => {
  assert.equal(getWeekStartIso('2026-08-10'), '2026-08-10'); // lunes
  assert.equal(getWeekStartIso('2026-08-11'), '2026-08-10'); // martes
  assert.equal(getWeekStartIso('2026-08-15'), '2026-08-10'); // sábado
  assert.equal(getWeekStartIso('2026-08-16'), '2026-08-10'); // domingo → lunes de ESA semana
  assert.equal(addDaysIso('2026-08-31', 1), '2026-09-01');
  assert.equal(dayLabel('2026-09-14'), 'Lun');
  assert.equal(dayLabel('2026-09-20'), 'Dom');
});

// ---- horas de un día -------------------------------------------------------------------

test('entrada y salida normales suman las horas', () => {
  const day = calculateDay([p('2026-08-10T08:00:00+00:00', PUNCH.IN), p('2026-08-10T17:00:00+00:00', PUNCH.OUT)]);
  assert.equal(day.regularMs, 9 * HOUR);
  assert.deepEqual(day.warnings, []);
});

test('el orden de llegada de las marcaciones no importa (se ordenan por hora)', () => {
  const day = calculateDay([p('2026-08-10T17:00:00+00:00', PUNCH.OUT), p('2026-08-10T08:00:00+00:00', PUNCH.IN)]);
  assert.equal(day.regularMs, 9 * HOUR);
});

test('entrada sin salida no suma y advierte', () => {
  const day = calculateDay([p('2026-08-10T08:00:00+00:00', PUNCH.IN)]);
  assert.equal(day.regularMs, 0);
  assert.equal(day.warnings.length, 1);
});

test('salida sin entrada no suma y advierte', () => {
  const day = calculateDay([p('2026-08-10T17:00:00+00:00', PUNCH.OUT)]);
  assert.equal(day.regularMs, 0);
  assert.equal(day.warnings.length, 1);
});

test('doble entrada: se usa la PRIMERA y se advierte cuál se ignoró', () => {
  const day = calculateDay([
    p('2026-08-10T08:00:00+00:00', PUNCH.IN),
    p('2026-08-10T09:00:00+00:00', PUNCH.IN),
    p('2026-08-10T17:00:00+00:00', PUNCH.OUT),
  ]);
  assert.equal(day.regularMs, 9 * HOUR);
  assert.equal(day.warnings.length, 1);
  assert.match(day.warnings[0], /08:00/);
  assert.match(day.warnings[0], /09:00/);
});

test('caso real de Adali (15/09): 07:53 E, 15:29 E, 15:53 S = 8:00 h', () => {
  const day = calculateDay([
    p('2026-09-15T07:53:00+00:00', PUNCH.IN), p('2026-09-15T15:29:00+00:00', PUNCH.IN), p('2026-09-15T15:53:00+00:00', PUNCH.OUT),
  ]);
  assert.equal(day.regularMs, 8 * HOUR);
  assert.equal(day.warnings.length, 1);
});

test('tres entradas seguidas: conserva la primera y advierte por cada repetida', () => {
  const day = calculateDay([
    p('2026-08-10T08:00:00+00:00', PUNCH.IN), p('2026-08-10T08:01:00+00:00', PUNCH.IN),
    p('2026-08-10T08:02:00+00:00', PUNCH.IN), p('2026-08-10T16:00:00+00:00', PUNCH.OUT),
  ]);
  assert.equal(day.regularMs, 8 * HOUR);
  assert.equal(day.warnings.length, 2);
});

test('el descanso resta del turno; nunca queda negativo', () => {
  const ok = calculateDay([
    p('2026-08-10T08:00:00+00:00', PUNCH.IN), p('2026-08-10T12:00:00+00:00', PUNCH.BREAK_OUT),
    p('2026-08-10T13:00:00+00:00', PUNCH.BREAK_IN), p('2026-08-10T17:00:00+00:00', PUNCH.OUT),
  ]);
  assert.equal(ok.regularMs, 8 * HOUR);

  const negative = calculateDay([
    p('2026-08-10T08:00:00+00:00', PUNCH.IN), p('2026-08-10T09:00:00+00:00', PUNCH.OUT),
    p('2026-08-10T09:10:00+00:00', PUNCH.BREAK_OUT), p('2026-08-10T12:10:00+00:00', PUNCH.BREAK_IN),
  ]);
  assert.equal(negative.regularMs, 0);
  assert.ok(negative.warnings.some(w => w.includes('descanso')));
});

test('el tiempo extra se suma aparte', () => {
  const day = calculateDay([
    p('2026-08-10T08:00:00+00:00', PUNCH.IN), p('2026-08-10T16:00:00+00:00', PUNCH.OUT),
    p('2026-08-10T18:00:00+00:00', PUNCH.OVERTIME_IN), p('2026-08-10T20:00:00+00:00', PUNCH.OVERTIME_OUT),
  ]);
  assert.equal(day.regularMs, 8 * HOUR);
  assert.equal(day.overtimeMs, 2 * HOUR);
});

test('una marcación sin tipo se ignora', () => {
  const day = calculateDay([
    p('2026-08-10T08:00:00+00:00', PUNCH.IN), p('2026-08-10T12:00:00+00:00', null), p('2026-08-10T17:00:00+00:00', PUNCH.OUT),
  ]);
  assert.equal(day.regularMs, 9 * HOUR);
});

// ---- formato ---------------------------------------------------------------------------

test('H:MM sin tope de 24 h y con minutos truncados', () => {
  assert.equal(formatHoursAndMinutes(36 * HOUR + 36 * 60_000), '36:36');
  assert.equal(formatHoursAndMinutes(0), '0:00');
  assert.equal(formatHoursAndMinutes(8 * HOUR + 59_999), '8:00'); // truncado, no redondeado
});

// ---- semana completa -------------------------------------------------------------------

const employee = (over = {}) => ({
  scheduledStartTime: '08:00:00', hasSpecialSchedule: false, weeklySalary: 2500, overtimeHourlyRate: null, ...over,
});
const workDay = (date, inTime = '08:00', outTime = '17:00') => [
  p(`${date}T${inTime}:00+00:00`, PUNCH.IN), p(`${date}T${outTime}:00+00:00`, PUNCH.OUT),
];
const WEEK = '2026-08-10'; // lunes
const PAST = '2026-09-30'; // todos los días de WEEK ya pasaron

test('semana con 6 días trabajados: el primer día sin marcación es descanso, sin faltas', () => {
  const punches = ['2026-08-10', '2026-08-11', '2026-08-12', '2026-08-13', '2026-08-14', '2026-08-15'].flatMap(d => workDay(d));
  const week = calculateWeek(employee(), WEEK, punches, PAST);
  assert.equal(week.days[6].status, 'rest'); // domingo
  assert.equal(week.absenceCount, 0);
  assert.equal(week.totalRegularMs, 6 * 9 * HOUR);
});

test('el segundo día sin marcación ya es falta', () => {
  const punches = ['2026-08-10', '2026-08-11', '2026-08-12', '2026-08-13'].flatMap(d => workDay(d));
  const week = calculateWeek(employee(), WEEK, punches, PAST);
  assert.deepEqual(week.days.map(d => d.status), ['worked', 'worked', 'worked', 'worked', 'rest', 'absent', 'absent']);
  assert.equal(week.absenceCount, 2);
});

test('hoy y los días futuros quedan pendientes: nunca se marca una falta antes de tiempo', () => {
  const week = calculateWeek(employee(), WEEK, workDay('2026-08-10'), '2026-08-12');
  assert.deepEqual(week.days.map(d => d.status), ['worked', 'rest', 'pending', 'pending', 'pending', 'pending', 'pending']);
  assert.equal(week.absenceCount, 0);
});

test('el sueldo semanal sale completo; sueldo null = pendiente y NO se toma como cero', () => {
  const paid = calculateWeek(employee({ weeklySalary: 3500 }), WEEK, [], PAST);
  assert.equal(paid.totalPay, 3500);

  const pending = calculateWeek(employee({ weeklySalary: null }), WEEK, [], PAST);
  assert.equal(pending.totalPay, 0);
  assert.ok(pending.warnings.includes('Sueldo semanal pendiente de captura — no se incluyó en el total.'));
});

test('horas extra: pago = horas × tarifa; sin tarifa advierte y no paga', () => {
  const punches = [
    ...workDay('2026-08-10', '08:00', '16:00'),
    p('2026-08-10T18:00:00+00:00', PUNCH.OVERTIME_IN), p('2026-08-10T20:00:00+00:00', PUNCH.OVERTIME_OUT),
  ];
  const paid = calculateWeek(employee({ weeklySalary: 2000, overtimeHourlyRate: 50 }), WEEK, punches, PAST);
  assert.equal(paid.overtimePay, 100);
  assert.equal(paid.totalPay, 2100);

  const noRate = calculateWeek(employee({ weeklySalary: 2000, overtimeHourlyRate: null }), WEEK, punches, PAST);
  assert.equal(noRate.overtimePay, 0);
  assert.ok(noRate.warnings.some(w => w.includes('no tiene tarifa de hora extra')));
});

test('sin permiso para ver montos (weeklySalary indefinido) no hay pago ni advertencias de pago', () => {
  const week = calculateWeek({ scheduledStartTime: '08:00:00', hasSpecialSchedule: false }, WEEK, [], PAST);
  assert.equal(week.totalPay, null);
  assert.ok(!week.warnings.some(w => w.includes('Sueldo')));
});

test('la advertencia lleva la fecha del día (dd/MM: …)', () => {
  const week = calculateWeek(employee(), WEEK, [p('2026-08-11T08:00:00+00:00', PUNCH.IN)], PAST);
  assert.ok(week.warnings[0].startsWith('11/08: Quedó un turno normal sin cerrar'));
});

test('cada día lleva su entrada (primera) y salida (última)', () => {
  const week = calculateWeek(employee(), WEEK, [
    p('2026-08-10T07:58:00+00:00', PUNCH.IN), p('2026-08-10T12:00:00+00:00', PUNCH.OUT),
    p('2026-08-10T12:30:00+00:00', PUNCH.IN), p('2026-08-10T16:05:00+00:00', PUNCH.OUT),
  ], PAST);
  const monday = week.days[0];
  assert.equal(monday.entry, '07:58');
  assert.equal(monday.exit, '16:05');
  assert.deepEqual(monday.allPunches, ['07:58', '12:00', '12:30', '16:05']);

  const single = calculateWeek(employee(), WEEK, [p('2026-08-10T08:00:00+00:00', PUNCH.IN)], PAST).days[0];
  assert.equal(single.exit, null);
});

test('insignia de cada día como en la PC: horas / Descanso / Falta / —', () => {
  const week = calculateWeek(employee(), WEEK, workDay('2026-08-10'), '2026-08-13');
  assert.deepEqual(week.days.slice(0, 5).map(dayBadgeText), ['9:00', 'Descanso', 'Falta', '—', '—']);
});

// ---- semáforo (PunctualityClassifier) --------------------------------------------------

test('verde a tiempo (tolerancia 10 min), amarillo con retardo', () => {
  assert.equal(classifyDay('worked', false, '08:00:00', '2026-08-10T08:10:00+00:00'), 'green');
  assert.equal(classifyDay('worked', false, '08:00:00', '2026-08-10T08:11:00+00:00'), 'yellow');
  assert.equal(classifyDay('worked', false, '08:00:00', '2026-08-10T07:00:00+00:00'), 'green');
});

test('falta roja; descanso y pendiente neutrales', () => {
  assert.equal(classifyDay('absent', false, '08:00:00', null), 'red');
  assert.equal(classifyDay('rest', false, '08:00:00', null), 'neutral');
  assert.equal(classifyDay('pending', false, '08:00:00', null), 'neutral');
});

test('sin horario capturado no se juzga la puntualidad', () => {
  assert.equal(classifyDay('worked', false, null, '2026-08-10T12:00:00+00:00'), 'neutral');
});

test('horario especial: verde si trabajó, nunca amarillo ni rojo', () => {
  assert.equal(classifyDay('worked', true, '08:00:00', '2026-08-10T12:00:00+00:00'), 'green');
  assert.equal(classifyDay('absent', true, '08:00:00', null), 'neutral');
});

// ---- neto ------------------------------------------------------------------------------

test('neto = total - ISR - IMSS - otro (puede salir negativo)', () => {
  assert.equal(netPay(3500, { isr: 100, imss: 50, other: 25 }), 3325);
  assert.equal(netPay(100, { isr: 200, imss: 0, other: 0 }), -100);
  assert.equal(netPay(3500, undefined), 3500);
});

// ---- CSV -------------------------------------------------------------------------------

import { buildAttendanceReportCsv, csvEscape } from '../../dashboard/payroll-calc.js';

const sampleRow = () => {
  const week = calculateWeek(employee({ weeklySalary: 3500, overtimeHourlyRate: 60 }), WEEK, workDay('2026-08-10', '07:53', '15:53'), '2026-08-12');
  const deductions = { isr: 100, imss: 50, other: 25, label: 'Préstamo', notes: 'nota, con coma' };
  return { name: 'Adali "La" Ramos', number: 'EMP-037', branchName: 'CAR-WASH', department: 'CARWASH', week, deductions, net: netPay(week.totalPay, deductions) };
};

test('csvEscape: comillas cuando hay coma, comilla o salto de línea', () => {
  assert.equal(csvEscape('simple'), 'simple');
  assert.equal(csvEscape('a,b'), '"a,b"');
  assert.equal(csvEscape('di "hola"'), '"di ""hola"""');
  assert.equal(csvEscape(null), '');
});

test('CSV admin: mismas columnas que la PC + número y entrada–salida; montos con 2 decimales', () => {
  const [header, line] = buildAttendanceReportCsv([sampleRow()], true).split('\r\n');
  assert.match(header, /^Empleado,Número,Sucursal,Departamento,Checadas de la semana,Entrada – Salida por día,Faltas,Horas normales,Horas extra,Sueldo semanal,/);
  assert.match(header, /Neto a pagar,Notas de deducciones,Advertencias$/);
  assert.ok(line.startsWith('"Adali ""La"" Ramos",EMP-037,CAR-WASH,CARWASH,'));
  assert.ok(line.includes('Lun 8:00 | Mar Descanso'), 'insignias como la PC');
  assert.ok(line.includes('Lun 07:53-15:53'), 'entrada–salida por día');
  assert.ok(line.includes(',3500.00,0.00,3500.00,100.00,50.00,25.00,Préstamo,3325.00,"nota, con coma"'), line);
});

test('CSV de una cuenta sin permiso: no trae sueldos, deducciones ni neto', () => {
  const week = calculateWeek({ scheduledStartTime: '08:00:00', hasSpecialSchedule: false }, WEEK, workDay('2026-08-10'), '2026-08-12');
  const csv = buildAttendanceReportCsv([{ name: 'X', number: '1', branchName: 'B', department: '', week, deductions: { isr: 0, imss: 0, other: 0 }, net: null }], false);
  const [header] = csv.split('\r\n');
  assert.ok(!/Sueldo|ISR|IMSS|Neto/.test(header));
  assert.ok(header.endsWith('Horas extra,Advertencias'));
});

test('CSV: sueldo pendiente sale como "Pendiente", no como 0', () => {
  const week = calculateWeek(employee({ weeklySalary: null }), WEEK, [], PAST);
  const csv = buildAttendanceReportCsv([{ name: 'Ana', number: '5', branchName: 'B', department: '', week, deductions: { isr: 0, imss: 0, other: 0 }, net: 0 }], true);
  assert.ok(csv.includes(',Pendiente,'));
});
