-- Espejo de RelojChecador.Domain.Payroll.PayrollDeduction — deducciones de nómina
-- (ISR/IMSS/otras) capturadas MANUALMENTE por el usuario, semana por semana. El sistema
-- NUNCA calcula estos montos: decisión explícita del usuario tras preguntarle por el
-- régimen fiscal y las tablas a usar ("todo se pondrá manualmente") — las tasas fiscales
-- cambian cada año y un error aquí tiene consecuencias reales para el negocio.
--
-- Se sincroniza igual que el resto del dominio (Branch/Employee/Device/Attendance): push
-- completo cada ciclo desde la app de escritorio (service_role). Mantiene la puerta
-- abierta, barata, para cuando el usuario quiera ver nómina en el Dashboard — esa UI no
-- se construye en esta entrega.
create table public.payroll_deductions (
  id uuid primary key,
  employee_id uuid not null references public.employees(id),
  week_start date not null,
  isr_amount numeric(10, 2) not null default 0,
  imss_amount numeric(10, 2) not null default 0,
  other_amount numeric(10, 2) not null default 0,
  other_label text,
  notes text,
  created_at_utc timestamptz not null,
  updated_at_utc timestamptz not null,
  concurrency_token uuid not null
);
comment on table public.payroll_deductions is 'Espejo de RelojChecador.Domain.Payroll.PayrollDeduction — ISR/IMSS/otros descuentos capturados manualmente por semana, el sistema nunca los calcula.';

create unique index payroll_deductions_employee_week_idx on public.payroll_deductions(employee_id, week_start);

-- RLS: mismo criterio que el resto del esquema — la app de escritorio (service_role)
-- ignora RLS por diseño; el Dashboard (authenticated) solo puede leer, nunca escribir.
alter table public.payroll_deductions enable row level security;
create policy "authenticated_read_payroll_deductions" on public.payroll_deductions for select to authenticated using (true);
