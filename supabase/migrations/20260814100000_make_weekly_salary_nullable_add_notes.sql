-- weekly_salary deja de ser NOT NULL: null significa "sueldo pendiente de captura",
-- NUNCA se debe confundir con $0 — caso real que motivó el cambio: importación de un
-- catálogo de empleados donde el sueldo no estaba disponible en ninguna fuente para
-- varios de ellos. Ver Employee.WeeklySalary (RelojChecador.Domain) para el detalle.
--
-- notes: observaciones libres del empleado — pensado originalmente para conservar el
-- origen/las excepciones detectadas al importar un catálogo desde una fuente externa,
-- para auditoría futura.
alter table public.employees
  alter column weekly_salary drop not null,
  add column notes text;
