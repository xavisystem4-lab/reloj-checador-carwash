using RelojChecador.Domain.Attendances;
using RelojChecador.Domain.Branches;
using RelojChecador.Domain.Devices;
using RelojChecador.Domain.EmployeeDeviceMappings;
using RelojChecador.Domain.Employees;
using RelojChecador.Domain.Identity;
using RelojChecador.Domain.Payroll;

namespace RelojChecador.Infrastructure.Cloud.Dtos;

// Un DTO por tabla de Supabase (ver la migración initial_schema, project vkvlucpjgvqrlvevcimq).
// Los nombres de propiedad son PascalCase a propósito — SupabaseRestClient serializa con
// JsonNamingPolicy.SnakeCaseLower, así que "TimeZoneId" sale como "time_zone_id" sin
// tener que escribir cada nombre de columna a mano ni arriesgar una desalineación.

public sealed record BranchDto(
    Guid Id, string Code, string Name, string? LegalEntityName, string? Address, string TimeZoneId,
    Guid? ManagerEmployeeId, bool IsActive, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, Guid ConcurrencyToken)
{
    public static BranchDto FromDomain(Branch branch) => new(
        branch.Id, branch.Code, branch.Name, branch.LegalEntityName, branch.Address, branch.TimeZoneId,
        branch.ManagerEmployeeId, branch.IsActive, branch.CreatedAtUtc, branch.UpdatedAtUtc, branch.ConcurrencyToken);
}

public sealed record EmployeeDto(
    Guid Id, string Number, string FullName, Guid BranchId, string? Department, string? Position,
    DateOnly HireDate, string Status, string? Phone, string? Email, string? Rfc, string? Curp, string? Nss,
    decimal? WeeklySalary, decimal? OvertimeHourlyRate, string? Notes,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc, Guid ConcurrencyToken)
{
    public static EmployeeDto FromDomain(Employee employee) => new(
        employee.Id, employee.Number.Value, employee.FullName, employee.BranchId, employee.Department, employee.Position,
        employee.HireDate, employee.Status.ToString(), employee.Phone, employee.Email, employee.Rfc, employee.Curp, employee.Nss,
        employee.WeeklySalary, employee.OvertimeHourlyRate, employee.Notes,
        employee.CreatedAtUtc, employee.UpdatedAtUtc, employee.ConcurrencyToken);
}

public sealed record DeviceDto(
    Guid Id, string Name, string Brand, string Model, string? SerialNumber, string? MacAddress, string IpAddress,
    int TcpPort, string? MachineNumber, Guid BranchId, string TimeZoneId, string Status,
    DateTime? LastCommunicationAtUtc, DateTime? LastSyncAtUtc, string? FirmwareVersion, int Capabilities,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc, Guid ConcurrencyToken)
{
    // CredentialReference NO se incluye a propósito: es una clave hacia Windows Credential
    // Manager de la máquina de origen, sin significado fuera de ella — ver la migración.
    public static DeviceDto FromDomain(Device device) => new(
        device.Id, device.Name, device.Brand, device.Model, device.SerialNumber, device.MacAddress, device.IpAddress,
        device.TcpPort, device.MachineNumber, device.BranchId, device.TimeZoneId, device.Status.ToString(),
        device.LastCommunicationAtUtc, device.LastSyncAtUtc, device.FirmwareVersion, (int)device.Capabilities,
        device.CreatedAtUtc, device.UpdatedAtUtc, device.ConcurrencyToken);
}

public sealed record EmployeeDeviceMappingDto(
    Guid Id, Guid EmployeeId, Guid DeviceId, string DeviceUserPin, DateTime EnrolledAtUtc)
{
    public static EmployeeDeviceMappingDto FromDomain(EmployeeDeviceMapping mapping) => new(
        mapping.Id, mapping.EmployeeId, mapping.DeviceId, mapping.DeviceUserPin, mapping.EnrolledAtUtc);
}

public sealed record AttendanceDto(
    Guid Id, Guid DeviceId, Guid BranchId, Guid? EmployeeId, string DeviceUserPin, DateTime TimestampUtc,
    string VerifyMethod, int? PunchType, string RawPayload,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc, Guid ConcurrencyToken)
{
    public static AttendanceDto FromDomain(Attendance attendance) => new(
        attendance.Id, attendance.DeviceId, attendance.BranchId, attendance.EmployeeId, attendance.DeviceUserPin,
        attendance.TimestampUtc, attendance.VerifyMethod.ToString(), attendance.PunchType, attendance.RawPayload,
        attendance.CreatedAtUtc, attendance.UpdatedAtUtc, attendance.ConcurrencyToken);
}

public sealed record AppUserDto(
    Guid Id, string Username, string? FullName, string? Email, string Role, bool IsActive, Guid[] BranchIds,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc, Guid ConcurrencyToken)
{
    public static AppUserDto FromDomain(User user) => new(
        user.Id, user.Username, user.FullName, user.Email, user.Role.ToString(), user.IsActive,
        [.. user.BranchIds], user.CreatedAtUtc, user.UpdatedAtUtc, user.ConcurrencyToken);
}

public sealed record PayrollDeductionDto(
    Guid Id, Guid EmployeeId, DateOnly WeekStart, decimal IsrAmount, decimal ImssAmount, decimal OtherAmount,
    string? OtherLabel, string? Notes, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, Guid ConcurrencyToken)
{
    public static PayrollDeductionDto FromDomain(PayrollDeduction deduction) => new(
        deduction.Id, deduction.EmployeeId, deduction.WeekStart, deduction.IsrAmount, deduction.ImssAmount,
        deduction.OtherAmount, deduction.OtherLabel, deduction.Notes,
        deduction.CreatedAtUtc, deduction.UpdatedAtUtc, deduction.ConcurrencyToken);
}
