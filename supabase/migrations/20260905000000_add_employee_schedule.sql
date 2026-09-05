-- Horario esperado por empleado (Employee.ScheduledStartTime/ScheduledEndTime, agregado a
-- la app de escritorio en una fase previa) y "horario especial" (Employee.HasSpecialSchedule,
-- pedido explícito del usuario: "excluirlos de retardo/falta automáticos" para roles con
-- horario flexible o nocturno como Velador/Gerente) — hasta ahora NUNCA se habían
-- sincronizado a Supabase, así que el Dashboard web no tenía con qué calcular el semáforo
-- de puntualidad (verde/amarillo/rojo) en el reporte de asistencia. Mismos nombres/tipos
-- que la migración EF Core equivalente (AddEmployeeScheduledTimes / AddEmployeeHasSpecialSchedule).
alter table public.employees
  add column scheduled_start_time time,
  add column scheduled_end_time time,
  add column has_special_schedule boolean not null default false;
