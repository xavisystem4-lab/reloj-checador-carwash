// Cálculo semanal de horas e insumo de nómina — PORT a JavaScript de la lógica de la app de
// escritorio, para que el Reporte de asistencia de la web muestre EXACTAMENTE lo mismo que la
// pantalla Reportes de la PC:
//   - src/RelojChecador.Application/Payroll/WorkedHoursCalculator.cs  (horas, descanso/falta,
//     advertencias, sueldo + pago de horas extra)
//   - src/RelojChecador.Application/Attendances/PunctualityClassifier.cs  (verde / amarillo /
//     rojo / neutral por día)
// Módulo puro (sin DOM ni red) a propósito: se prueba con Node (tests/dashboard/) contra los
// mismos casos que las pruebas de C#. Si cambia una regla en C#, cambia aquí también.
//
// timestamp_utc NO es UTC real: es la hora de pared del reloj checador, sin convertir (ver
// formatAttendanceDateTime en app.js) — por eso el día y la hora se leen del TEXTO del
// timestamp ("YYYY-MM-DDTHH:mm...") y nunca con getHours()/toLocale*, que la correrían por el
// huso del navegador.

export const PUNCH = Object.freeze({
  IN: 0,
  OUT: 1,
  BREAK_OUT: 2,
  BREAK_IN: 3,
  OVERTIME_IN: 4,
  OVERTIME_OUT: 5,
});

/// Tolerancia de puntualidad — pedido explícito del usuario ("tolerancia 10 mns").
export const PUNCTUALITY_TOLERANCE_MINUTES = 10;

const DAY_LABELS = ['Dom', 'Lun', 'Mar', 'Mié', 'Jue', 'Vie', 'Sáb'];

const MS_PER_MINUTE = 60_000;

// ---------------------------------------------------------------- fechas (texto ISO)

/// "YYYY-MM-DD" de un día + n días. Se calcula con UTC puro (Date.UTC) para no depender del
/// huso horario del navegador.
export function addDaysIso(dayIso, days) {
  const [y, m, d] = dayIso.split('-').map(Number);
  const date = new Date(Date.UTC(y, m - 1, d + days));
  return date.toISOString().slice(0, 10);
}

/// Lunes de la semana de ese día (la semana va de lunes a domingo, igual que WeekBoundary.cs).
export function getWeekStartIso(dayIso) {
  const [y, m, d] = dayIso.split('-').map(Number);
  const dow = (new Date(Date.UTC(y, m - 1, d)).getUTCDay() + 6) % 7; // 0=lunes ... 6=domingo
  return addDaysIso(dayIso, -dow);
}

/// Etiqueta corta del día de la semana ("Lun", "Mar"…) de un "YYYY-MM-DD".
export function dayLabel(dayIso) {
  const [y, m, d] = dayIso.split('-').map(Number);
  return DAY_LABELS[new Date(Date.UTC(y, m - 1, d)).getUTCDay()];
}

/// "dd/MM" de un "YYYY-MM-DD".
export function shortDate(dayIso) {
  return `${dayIso.slice(8, 10)}/${dayIso.slice(5, 7)}`;
}

/// "dd/MM/yyyy" de un "YYYY-MM-DD".
export function longDate(dayIso) {
  return `${dayIso.slice(8, 10)}/${dayIso.slice(5, 7)}/${dayIso.slice(0, 4)}`;
}

/// "HH:mm" de un timestamp (24 h, hora de pared del reloj).
export function timeLabel(timestamp) {
  return timestamp.slice(11, 16);
}

function minutesOfDay(hhmm) {
  const [h, m] = hhmm.slice(0, 5).split(':').map(Number);
  return h * 60 + m;
}

// ---------------------------------------------------------------- formato

/// "H:MM" — igual que PayrollRow.FormatHoursAndMinutes (horas totales sin tope de 24 y
/// minutos truncados, no redondeados).
export function formatHoursAndMinutes(ms) {
  const totalMinutes = Math.floor(Math.max(0, ms) / MS_PER_MINUTE);
  return `${Math.floor(totalMinutes / 60)}:${String(totalMinutes % 60).padStart(2, '0')}`;
}

/// "Xh Ym" — igual que WorkedHoursCalculator.FormatHours (se usa dentro de las advertencias).
function formatHoursText(ms) {
  const totalMinutes = Math.floor(Math.max(0, ms) / MS_PER_MINUTE);
  return `${Math.floor(totalMinutes / 60)}h ${totalMinutes % 60}m`;
}

const moneyFormat = new Intl.NumberFormat('es-MX', { style: 'currency', currency: 'MXN' });

/// "$3,500.00" — igual que decimal.ToString("C") de la PC en es-MX.
export function formatMoney(amount) {
  return moneyFormat.format(amount ?? 0);
}

// ---------------------------------------------------------------- horas de un día

/// Empareja cronológicamente cada marcación "abre" (openType) con la siguiente "cierra"
/// (closeType) y suma la diferencia. Mismo criterio defensivo que WorkedHoursCalculator.PairAndSum:
/// cualquier desbalance se reporta en `warnings`, nunca se inventa la pareja faltante.
///
/// DOBLE CHECADA (decisión explícita del usuario, 18/09/2026): con dos aperturas seguidas se
/// conserva la PRIMERA (la llegada real) y se ignora la repetida.
export function pairAndSum(sortedPunches, openType, closeType, label, warnings) {
  let total = 0;
  let openAt = null; // timestamp del texto
  for (const punch of sortedPunches) {
    if (punch.punch_type === openType) {
      if (openAt !== null) {
        warnings.push(
          `Dos marcaciones de inicio de ${label} seguidas sin su cierre (se tomó la de las ${timeLabel(openAt)} y se ignoró la de las ${timeLabel(punch.timestamp_utc)}).`);
        continue;
      }
      openAt = punch.timestamp_utc;
    } else if (punch.punch_type === closeType) {
      if (openAt === null) {
        warnings.push(`Marcación de cierre de ${label} a las ${timeLabel(punch.timestamp_utc)} sin su inicio correspondiente — se ignoró.`);
        continue;
      }
      total += Date.parse(punch.timestamp_utc) - Date.parse(openAt);
      openAt = null;
    }
  }

  if (openAt !== null) {
    warnings.push(`Quedó un ${label} sin cerrar (inició a las ${timeLabel(openAt)}) — ese tramo no se contó.`);
  }
  return total;
}

/// Horas de UN día — WorkedHoursCalculator.CalculateDay.
export function calculateDay(dayPunches) {
  const warnings = [];
  const sorted = [...dayPunches].sort((a, b) => Date.parse(a.timestamp_utc) - Date.parse(b.timestamp_utc));

  const regular = pairAndSum(sorted, PUNCH.IN, PUNCH.OUT, 'turno normal', warnings);
  const breakTime = pairAndSum(sorted, PUNCH.BREAK_OUT, PUNCH.BREAK_IN, 'descanso', warnings);
  const overtime = pairAndSum(sorted, PUNCH.OVERTIME_IN, PUNCH.OVERTIME_OUT, 'tiempo extra', warnings);

  // El descanso nunca resta más de lo que se trabajó.
  let netRegular = regular - breakTime;
  if (netRegular < 0) {
    warnings.push(
      `El tiempo de descanso (${formatHoursText(breakTime)}) es mayor que el turno normal (${formatHoursText(regular)}) — se registró 0h en vez de un valor negativo.`);
    netRegular = 0;
  }

  return { regularMs: netRegular, overtimeMs: overtime, warnings };
}

// ---------------------------------------------------------------- puntualidad

/// 'green' | 'yellow' | 'red' | 'neutral' — PunctualityClassifier.Classify.
/// status: 'worked' | 'rest' | 'absent' | 'pending'.
export function classifyDay(status, hasSpecialSchedule, scheduledStartTime, firstPunchTimestamp) {
  if (hasSpecialSchedule) {
    // Horario especial: se ve si trabajó (verde) pero nunca se juzga la hora.
    return status === 'worked' ? 'green' : 'neutral';
  }
  if (status === 'rest' || status === 'pending') return 'neutral';
  if (status === 'absent') return 'red';

  // Trabajado, pero sin horario capturado o sin marcación que comparar: no se inventa un juicio.
  if (!scheduledStartTime || !firstPunchTimestamp) return 'neutral';

  const arrival = minutesOfDay(timeLabel(firstPunchTimestamp));
  const latestOnTime = minutesOfDay(scheduledStartTime) + PUNCTUALITY_TOLERANCE_MINUTES;
  return arrival <= latestOnTime ? 'green' : 'yellow';
}

// ---------------------------------------------------------------- semana completa

/// Resumen semanal de UN empleado — WorkedHoursCalculator.CalculateWeek.
///
/// employee: { scheduledStartTime, hasSpecialSchedule, weeklySalary, overtimeHourlyRate }
///   (weeklySalary === undefined = los montos no se cargaron — cuenta sin permiso — y no se
///   agregan advertencias ni pagos; null = sueldo pendiente de captura.)
/// punches: marcaciones del empleado en la semana, { timestamp_utc, punch_type }.
/// todayIso: "hoy" como "YYYY-MM-DD" — parámetro explícito para que sea 100 % probable.
export function calculateWeek(employee, weekStartIso, punches, todayIso) {
  const payVisible = employee.weeklySalary !== undefined;
  const warnings = [];
  let totalRegular = 0;
  let totalOvertime = 0;

  const byDay = new Map();
  for (const punch of punches) {
    const day = punch.timestamp_utc.slice(0, 10);
    if (!byDay.has(day)) byDay.set(day, []);
    byDay.get(day).push(punch);
  }

  const days = [];
  let restDayTaken = false;
  for (let offset = 0; offset < 7; offset++) {
    const date = addDaysIso(weekStartIso, offset);
    const dayPunches = byDay.get(date);

    if (dayPunches && dayPunches.length > 0) {
      const summary = calculateDay(dayPunches);
      totalRegular += summary.regularMs;
      totalOvertime += summary.overtimeMs;
      warnings.push(...summary.warnings.map(w => `${shortDate(date)}: ${w}`));

      const sorted = [...dayPunches].sort((a, b) => Date.parse(a.timestamp_utc) - Date.parse(b.timestamp_utc));
      days.push({
        date,
        status: 'worked',
        regularMs: summary.regularMs,
        overtimeMs: summary.overtimeMs,
        warnings: summary.warnings,
        color: classifyDay('worked', employee.hasSpecialSchedule, employee.scheduledStartTime, sorted[0].timestamp_utc),
        entry: timeLabel(sorted[0].timestamp_utc),
        exit: sorted.length > 1 ? timeLabel(sorted[sorted.length - 1].timestamp_utc) : null,
        allPunches: sorted.map(p => timeLabel(p.timestamp_utc)),
      });
      continue;
    }

    // Sin ninguna marcación ese día. Hoy en adelante todavía puede llegar una: pendiente.
    if (date >= todayIso) {
      days.push({ date, status: 'pending', regularMs: 0, overtimeMs: 0, warnings: [], color: 'neutral', entry: null, exit: null, allPunches: [] });
      continue;
    }

    const status = restDayTaken ? 'absent' : 'rest';
    restDayTaken = true;
    days.push({
      date, status, regularMs: 0, overtimeMs: 0, warnings: [],
      color: classifyDay(status, employee.hasSpecialSchedule, employee.scheduledStartTime, null),
      entry: null, exit: null, allPunches: [],
    });
  }

  let overtimePay = 0;
  if (payVisible && totalOvertime > 0) {
    if (employee.overtimeHourlyRate === null || employee.overtimeHourlyRate === undefined) {
      warnings.push(
        `Hubo ${formatHoursText(totalOvertime)} de tiempo extra pero el empleado no tiene tarifa de hora extra capturada — no se calculó su pago.`);
    } else {
      overtimePay = (totalOvertime / 3_600_000) * employee.overtimeHourlyRate;
    }
  }

  // Sueldo pendiente de captura (null): NUNCA se trata como $0 — se advierte explícitamente.
  let totalPay = null;
  if (payVisible) {
    if (employee.weeklySalary === null) {
      warnings.push('Sueldo semanal pendiente de captura — no se incluyó en el total.');
      totalPay = overtimePay;
    } else {
      totalPay = employee.weeklySalary + overtimePay;
    }
  }

  return {
    weekStart: weekStartIso,
    weekEnd: addDaysIso(weekStartIso, 6),
    totalRegularMs: totalRegular,
    totalOvertimeMs: totalOvertime,
    weeklySalary: payVisible ? employee.weeklySalary : null,
    overtimePay,
    totalPay,
    warnings,
    days,
    absenceCount: days.filter(d => d.status === 'absent').length,
  };
}

/// Texto de la insignia de un día — igual que PayrollRow.ToBadge en la PC.
export function dayBadgeText(day) {
  switch (day.status) {
    case 'worked': return formatHoursAndMinutes(day.regularMs + day.overtimeMs);
    case 'rest': return 'Descanso';
    case 'absent': return 'Falta';
    default: return '—';
  }
}

/// Neto a pagar = total menos las tres deducciones capturadas a mano (puede salir negativo:
/// el usuario capturó los montos, no hay nada que corregir — igual que PayrollRow.NetPay).
export function netPay(totalPay, deductions) {
  return (totalPay ?? 0) - (deductions?.isr ?? 0) - (deductions?.imss ?? 0) - (deductions?.other ?? 0);
}

// ---------------------------------------------------------------- CSV

/// Escapa un valor para CSV (comillas si trae coma, comillas o salto de línea).
export function csvEscape(value) {
  const text = String(value ?? '');
  return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

/// CSV del reporte semanal: las mismas columnas que el de la PC (PayrollViewModel.BuildCsv) más
/// Número y las horas de Entrada–Salida de cada día. Sin sueldos ni deducciones si `admin` es false.
/// rows: { name, number, branchName, department, week, deductions, net } (ver buildWeeklyReportRows).
/// Devuelve el texto SIN el BOM (quien lo descarga lo agrega para que Excel respete los acentos).
export function buildAttendanceReportCsv(rows, admin) {
  const header = ['Empleado', 'Número', 'Sucursal', 'Departamento', 'Checadas de la semana', 'Entrada – Salida por día', 'Faltas',
    'Horas normales', 'Horas extra'];
  if (admin) {
    header.push('Sueldo semanal', 'Pago horas extra', 'Total a pagar', 'ISR', 'IMSS', 'Otro (monto)', 'Otro (concepto)', 'Neto a pagar',
      'Notas de deducciones');
  }
  header.push('Advertencias');

  const lines = [header, ...rows.map(r => {
    const week = r.week;
    const fields = [
      r.name, r.number, r.branchName, r.department,
      week.days.map(d => `${dayLabel(d.date)} ${dayBadgeText(d)}`).join(' | '),
      week.days.map(d => `${dayLabel(d.date)} ${d.status === 'worked' ? `${d.entry}-${d.exit ?? '?'}` : dayBadgeText(d)}`).join(' | '),
      week.absenceCount, formatHoursAndMinutes(week.totalRegularMs), formatHoursAndMinutes(week.totalOvertimeMs),
    ];
    if (admin) {
      fields.push(
        week.weeklySalary === null ? 'Pendiente' : week.weeklySalary.toFixed(2),
        week.overtimePay.toFixed(2), (week.totalPay ?? 0).toFixed(2),
        r.deductions.isr.toFixed(2), r.deductions.imss.toFixed(2), r.deductions.other.toFixed(2),
        r.deductions.label ?? '', (r.net ?? 0).toFixed(2), r.deductions.notes ?? '');
    }
    fields.push(week.warnings.join(' | '));
    return fields;
  })];

  return lines.map(row => row.map(csvEscape).join(',')).join('\r\n');
}
