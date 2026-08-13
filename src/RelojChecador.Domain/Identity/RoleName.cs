namespace RelojChecador.Domain.Identity;

/// <summary>
/// Roles definidos en la Fase 1 del proyecto, ajustados al modelo single-tenant
/// (sin "Administrador de empresa" porque no hay multi-empresa que administrar).
/// </summary>
public enum RoleName
{
    Administrador = 0,
    ResponsableSucursal = 1,
    RecursosHumanos = 2,
    Nomina = 3,
    Supervisor = 4,
    Consulta = 5,
    Auditor = 6,
}
