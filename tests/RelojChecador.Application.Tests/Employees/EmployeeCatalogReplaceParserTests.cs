using RelojChecador.Application.Employees;
using RelojChecador.Domain.Employees;

namespace RelojChecador.Application.Tests.Employees;

public class EmployeeCatalogReplaceParserTests
{
    private const string Header = "Number,FullName,Area,Position,HireDate,Status,WeeklySalary,OvertimeHourlyRate,Notes,Pin";

    [Fact]
    public void Parse_FilaCompletaYValida_DevuelveLaFilaConLosValoresCorrectos()
    {
        var lines = new[]
        {
            Header,
            "EMP-001,Adrian Uribe Garcia,Drive In Car Wash,Gerencia,2023-12-07,Activo,3800,135.71,Nota,7",
        };

        var result = EmployeeCatalogReplaceParser.Parse(lines);

        Assert.Empty(result.Errors);
        var row = Assert.Single(result.Rows);
        Assert.Equal("EMP-001", row.Number);
        Assert.Equal("Adrian Uribe Garcia", row.FullName);
        Assert.Equal("Drive In Car Wash", row.Area);
        Assert.Equal("Gerencia", row.Position);
        Assert.Equal(new DateOnly(2023, 12, 7), row.HireDate);
        Assert.Equal(EmploymentStatus.Active, row.Status);
        Assert.Equal(3800m, row.WeeklySalary);
        Assert.Equal(135.71m, row.OvertimeHourlyRate);
        Assert.Equal("Nota", row.Notes);
        Assert.Equal("7", row.Pin);
    }

    [Fact]
    public void Parse_ConHireDateVacio_DevuelveNull()
    {
        var lines = new[]
        {
            Header,
            "EMP-201,Javier Galaviz,Drive In Car Wash,,,Activo,,,,",
        };

        var result = EmployeeCatalogReplaceParser.Parse(lines);

        var row = Assert.Single(result.Rows);
        Assert.Null(row.HireDate);
    }

    [Fact]
    public void Parse_ConHireDateComoNumeroDeSerieDeExcel_LoConvierteALaFechaCorrecta()
    {
        // Caso real: Excel reinterpreta el texto ISO al abrir/guardar el CSV y, si la
        // celda quedó en formato "General", lo exporta como este entero — a diferencia de
        // un DD/MM/AAAA suelto (genuinamente ambiguo), esta conversión es exacta.
        var lines = new[]
        {
            Header,
            "EMP-001,Adrian Uribe,Drive In Car Wash,,45267,Activo,,,,",
        };

        var result = EmployeeCatalogReplaceParser.Parse(lines);

        Assert.Empty(result.Errors);
        var row = Assert.Single(result.Rows);
        Assert.Equal(new DateOnly(2023, 12, 7), row.HireDate);
    }

    [Fact]
    public void Parse_ConHireDateEnFormatoMexicano_LoInterpretaComoDiaMesAnio()
    {
        // Pedido explícito del usuario: "que la fecha la reconozca con el formato de
        // Español México dd/mm/aaaa". A propósito NO se acepta mm/dd/aaaa (inglés EE. UU.)
        // a la vez — "07/12/2023" solo puede significar 7 de diciembre bajo este único
        // formato de barras aceptado, sin ambigüedad.
        var lines = new[]
        {
            Header,
            "EMP-001,Adrian Uribe,Drive In Car Wash,,07/12/2023,Activo,,,,",
        };

        var result = EmployeeCatalogReplaceParser.Parse(lines);

        Assert.Empty(result.Errors);
        var row = Assert.Single(result.Rows);
        Assert.Equal(new DateOnly(2023, 12, 7), row.HireDate);
    }

    [Fact]
    public void Parse_ConHireDateInvalido_ReportaError()
    {
        var lines = new[]
        {
            Header,
            "EMP-001,Adrian Uribe,Drive In Car Wash,,32/13/2023,Activo,,,,",
        };

        var result = EmployeeCatalogReplaceParser.Parse(lines);

        Assert.Empty(result.Rows);
        Assert.Single(result.Errors);
        Assert.Contains("HireDate", result.Errors[0]);
    }

    [Theory]
    [InlineData("Activo", EmploymentStatus.Active)]
    [InlineData("activo", EmploymentStatus.Active)]
    [InlineData("", EmploymentStatus.Active)]
    [InlineData("Inactivo", EmploymentStatus.Inactive)]
    [InlineData("INACTIVO", EmploymentStatus.Inactive)]
    public void Parse_ConStatusVariasFormas_LoMapeaCorrectamente(string statusText, EmploymentStatus expected)
    {
        var lines = new[]
        {
            Header,
            $"EMP-001,Adrian Uribe,Drive In Car Wash,,,{statusText},,,,",
        };

        var result = EmployeeCatalogReplaceParser.Parse(lines);

        var row = Assert.Single(result.Rows);
        Assert.Equal(expected, row.Status);
    }

    [Fact]
    public void Parse_ConStatusInvalido_ReportaError()
    {
        var lines = new[]
        {
            Header,
            "EMP-001,Adrian Uribe,Drive In Car Wash,,,DeVacaciones,,,,",
        };

        var result = EmployeeCatalogReplaceParser.Parse(lines);

        Assert.Empty(result.Rows);
        Assert.Single(result.Errors);
        Assert.Contains("Status", result.Errors[0]);
    }

    [Fact]
    public void Parse_ConNumeroRepetidoEnElMismoArchivo_ReportaErrorEnLaSegundaAparicion()
    {
        var lines = new[]
        {
            Header,
            "EMP-001,Adrian Uribe,Drive In Car Wash,,,,,,,",
            "EMP-001,Otra Persona,Drive In Car Wash,,,,,,,",
        };

        var result = EmployeeCatalogReplaceParser.Parse(lines);

        var row = Assert.Single(result.Rows);
        Assert.Equal("Adrian Uribe", row.FullName);
        Assert.Single(result.Errors);
        Assert.Contains("ya aparece antes", result.Errors[0]);
    }

    [Fact]
    public void Parse_ConSueldoVacio_LoDejaEnNullYGeneraAlerta()
    {
        var lines = new[]
        {
            Header,
            "EMP-001,Adrian Uribe,Drive In Car Wash,,,,,,,",
        };

        var result = EmployeeCatalogReplaceParser.Parse(lines);

        var row = Assert.Single(result.Rows);
        Assert.Null(row.WeeklySalary);
        Assert.Contains(row.Alerts, a => a.Contains("pendiente de captura"));
    }

    [Theory]
    [InlineData(",Adrian Uribe,Drive In Car Wash,,,,,,,")] // Number vacío
    [InlineData("EMP-001,,Drive In Car Wash,,,,,,,")] // FullName vacío
    [InlineData("EMP-001,Adrian Uribe,,,,,,,,")] // Area vacía
    public void Parse_ConCampoObligatorioVacio_ReportaErrorYNoAgregaLaFila(string line)
    {
        var lines = new[] { Header, line };

        var result = EmployeeCatalogReplaceParser.Parse(lines);

        Assert.Empty(result.Rows);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Parse_ConPinValido_LoAsignaALaFila()
    {
        var lines = new[]
        {
            Header,
            "EMP-001,Adrian Uribe,Drive In Car Wash,,,,,,,42",
        };

        var result = EmployeeCatalogReplaceParser.Parse(lines);

        var row = Assert.Single(result.Rows);
        Assert.Equal("42", row.Pin);
    }

    [Fact]
    public void Parse_ConPinVacio_DevuelveNull()
    {
        var lines = new[]
        {
            Header,
            "EMP-001,Adrian Uribe,Drive In Car Wash,,,,,,,",
        };

        var result = EmployeeCatalogReplaceParser.Parse(lines);

        var row = Assert.Single(result.Rows);
        Assert.Null(row.Pin);
    }

    [Fact]
    public void Parse_ConPinConLetras_ReportaError()
    {
        var lines = new[]
        {
            Header,
            "EMP-001,Adrian Uribe,Drive In Car Wash,,,,,,,EMP-001",
        };

        var result = EmployeeCatalogReplaceParser.Parse(lines);

        Assert.Empty(result.Rows);
        Assert.Single(result.Errors);
        Assert.Contains("Pin", result.Errors[0]);
    }

    [Fact]
    public void Parse_ConEncabezadoDistinto_DevuelveErrorYNingunaFila()
    {
        var lines = new[]
        {
            "Number,FullName,Area,Position,WeeklySalary,OvertimeHourlyRate,Notes", // encabezado del formato viejo (sin HireDate/Status)
            "EMP-001,Adrian Uribe,Drive In Car Wash,,3800,,",
        };

        var result = EmployeeCatalogReplaceParser.Parse(lines);

        Assert.Empty(result.Rows);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Parse_ArchivoVacio_DevuelveError()
    {
        var result = EmployeeCatalogReplaceParser.Parse([]);

        Assert.Empty(result.Rows);
        Assert.Single(result.Errors);
    }
}
