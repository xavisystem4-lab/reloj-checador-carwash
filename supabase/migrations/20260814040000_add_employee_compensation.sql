-- Insumo de nómina (Fases 5-6, sin ningún cálculo fiscal — ver comentario de clase de
-- RelojChecador.Domain.Employees.Employee): sueldo semanal fijo y tarifa opcional de
-- hora extra, ambos capturados a mano por el usuario, nunca calculados por el sistema.
alter table public.employees
  add column weekly_salary numeric(10, 2) not null default 0,
  add column overtime_hourly_rate numeric(10, 2);
