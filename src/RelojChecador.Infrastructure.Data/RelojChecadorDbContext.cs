using Microsoft.EntityFrameworkCore;
using RelojChecador.Application.Common;
using RelojChecador.Domain.Branches;
using RelojChecador.Domain.Devices;
using RelojChecador.Domain.EmployeeDeviceMappings;
using RelojChecador.Domain.Employees;
using RelojChecador.Domain.Identity;

namespace RelojChecador.Infrastructure.Data;

/// <summary>
/// Base de datos local (SQLite) del equipo. Cada sucursal/instalación tiene su propio
/// archivo; el motor de sincronización (Fase 4, tarea posterior) es lo único que habla
/// con Supabase — este DbContext no sabe nada de la nube.
/// </summary>
public sealed class RelojChecadorDbContext : DbContext, IUnitOfWork
{
    public RelojChecadorDbContext(DbContextOptions<RelojChecadorDbContext> options) : base(options)
    {
    }

    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<EmployeeDeviceMapping> EmployeeDeviceMappings => Set<EmployeeDeviceMapping>();
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RelojChecadorDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
