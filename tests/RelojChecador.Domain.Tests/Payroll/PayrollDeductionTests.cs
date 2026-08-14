using RelojChecador.Domain.Common;
using RelojChecador.Domain.Payroll;

namespace RelojChecador.Domain.Tests.Payroll;

public class PayrollDeductionTests
{
    private static DateOnly SampleWeekStart => new(2026, 8, 10); // lunes

    [Fact]
    public void Create_ConEmployeeIdValido_AsignaCampos()
    {
        var employeeId = Guid.NewGuid();

        var deduction = PayrollDeduction.Create(employeeId, SampleWeekStart);

        Assert.Equal(employeeId, deduction.EmployeeId);
        Assert.Equal(SampleWeekStart, deduction.WeekStart);
        // Recién creada, sin montos capturados todavía — todo en cero, sin etiqueta ni notas.
        Assert.Equal(0m, deduction.IsrAmount);
        Assert.Equal(0m, deduction.ImssAmount);
        Assert.Equal(0m, deduction.OtherAmount);
        Assert.Null(deduction.OtherLabel);
        Assert.Null(deduction.Notes);
    }

    [Fact]
    public void Create_ConEmployeeIdVacio_LanzaDomainException()
    {
        Assert.Throws<DomainException>(() => PayrollDeduction.Create(Guid.Empty, SampleWeekStart));
    }

    [Fact]
    public void UpdateAmounts_ConMontosValidos_ActualizaCampos()
    {
        var deduction = PayrollDeduction.Create(Guid.NewGuid(), SampleWeekStart);

        deduction.UpdateAmounts(350.50m, 210.75m, 100m, "Préstamo", "Confirmado por el contador");

        Assert.Equal(350.50m, deduction.IsrAmount);
        Assert.Equal(210.75m, deduction.ImssAmount);
        Assert.Equal(100m, deduction.OtherAmount);
        Assert.Equal("Préstamo", deduction.OtherLabel);
        Assert.Equal("Confirmado por el contador", deduction.Notes);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void UpdateAmounts_ConCualquierMontoNegativo_LanzaDomainException(decimal isr, decimal imss, decimal other)
    {
        var deduction = PayrollDeduction.Create(Guid.NewGuid(), SampleWeekStart);

        Assert.Throws<DomainException>(() => deduction.UpdateAmounts(isr, imss, other, null, null));
    }

    [Fact]
    public void UpdateAmounts_ConEtiquetaYNotasEnBlanco_LasDejaEnNull()
    {
        var deduction = PayrollDeduction.Create(Guid.NewGuid(), SampleWeekStart);

        deduction.UpdateAmounts(0m, 0m, 0m, "   ", "");

        Assert.Null(deduction.OtherLabel);
        Assert.Null(deduction.Notes);
    }

    [Fact]
    public void UpdateAmounts_RecortaEspaciosDeEtiquetaYNotas()
    {
        var deduction = PayrollDeduction.Create(Guid.NewGuid(), SampleWeekStart);

        deduction.UpdateAmounts(0m, 0m, 0m, "  INFONAVIT  ", "  nota  ");

        Assert.Equal("INFONAVIT", deduction.OtherLabel);
        Assert.Equal("nota", deduction.Notes);
    }

    [Fact]
    public void UpdateAmounts_ActualizaUpdatedAtUtcYConcurrencyToken()
    {
        var deduction = PayrollDeduction.Create(Guid.NewGuid(), SampleWeekStart);
        var originalCreatedAt = deduction.CreatedAtUtc;
        var originalUpdatedAt = deduction.UpdatedAtUtc;
        var originalToken = deduction.ConcurrencyToken;

        deduction.UpdateAmounts(50m, 0m, 0m, null, null);

        Assert.True(deduction.UpdatedAtUtc >= originalUpdatedAt);
        Assert.NotEqual(originalToken, deduction.ConcurrencyToken);
        // CreatedAtUtc nunca cambia tras la creación, solo UpdatedAtUtc/ConcurrencyToken.
        Assert.Equal(originalCreatedAt, deduction.CreatedAtUtc);
    }

    [Fact]
    public void UpdateAmounts_LlamadoDosVeces_CorrigeElMismoRegistro()
    {
        // Caso real: el usuario captura un monto y luego lo corrige la misma semana —
        // nunca se crea una fila nueva para eso (ver PayrollViewModel.UpdateDeductionsAsync).
        var deduction = PayrollDeduction.Create(Guid.NewGuid(), SampleWeekStart);
        deduction.UpdateAmounts(100m, 0m, 0m, null, null);

        deduction.UpdateAmounts(150m, 0m, 0m, null, null);

        Assert.Equal(150m, deduction.IsrAmount);
    }
}
