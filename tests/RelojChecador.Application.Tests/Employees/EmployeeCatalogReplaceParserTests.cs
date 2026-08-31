using RelojChecador.Application.Employees;
using RelojChecador.Domain.Employees;

namespace RelojChecador.Application.Tests.Employees;

public class EmployeeCatalogReplaceParserTests
{
    private const string Header = "Number,FullName,Area,Position,HireDate,Status,WeeklySalary,OvertimeHourlyRate,Notes,Pin,Department";

    [Fact]
    public void Parse_FilaCompletaYValida_DevuelveLaFilaConLosValoresCorrectos()
    {
        var lines = new[]
        {
            Header,
            "EMP-001,Adrian Uribe Garcia,CAR-WASH,Gerencia,2023-12-07,Activo,3800,135.71,Nota,7,Plaza Sabo",
        };

        var result = EmployeeCatalogReplaceParser.Parse(lines);

        Assert.Empty(result.Errors);
        var row = Assert.Single(result.Rows);
        Assert.Equal("EMP-001", row.Number);
        Assert.Equal("Adrian Uribe Garcia", row.FullName);
        Assert.Equal("CAR-WASH", row.Area);
        Assert.Equal("Gerencia", row.Position);
        Assert.Equal(new DateOnly(2023, 12, 7), row.HireDate);
        Assert.Equal(EmploymentStatus.Active, row.Status);
        Assert.Equal(3800m, row.WeeklySalary);
        Assert.Equal(135.71m, row.OvertimeHourlyRate);
        Assert.Equal("Nota", row.Notes);
        Assert.Equal("7", row.Pin);
        Assert.Equal("Plaza Sabo", row.Department);
    }

    [Fact]
    public void Parse_ConDepartmentVacio_DevuelveNull()
    {
        var lines = new[]
        {
            Header,
            "EMP-001,Adrian Uribe Garcia,CAR-WASH,Gerencia,2023-12-07,Activo,3800,135.71,Nota,7,",
        };

        var result = EmployeeCatalogReplaceParser.Parse(lines);

        var row = Assert.Single(result.Rows);
        Assert.Null(row.Department);
    }

    [Fact]
    public void Parse_ConArchivoSeparadoPorPuntoYComa_LoDetectaYParseaIgual()
    {
        // Caso real: Excel en configuración regional en español (México y la mayoría de
        // Latinoamérica) exporta "Guardar como CSV" con ';' en vez de ',' — ver
        // CsvLineParser.DetectDelimiter.
        var lines = new[]
        {
            "Number;FullName;Area;Position;HireDate;Status;WeeklySalary;OvertimeHourlyRate;Notes;Pin;Department",
            "EMP-001;Adrian Uribe Garcia;CAR-WASH;Gerencia;2023-12-07;Activo;3800;135.71;Nota;7;",
        };

        var result = EmployeeCatalogReplaceParser.Parse(lines);

        Assert.Empty(result.Errors);
        var row = Assert.Single(result.Rows);
        Assert.Equal("EMP-001", row.Number);
        Assert.Equal("Adrian Uribe Garcia", row.FullName);
        Assert.Equal(3800m, row.WeeklySalary);
        Assert.Equal("7", row.Pin);
    }

    [Fact]
    public void Parse_ConHireDateVacio_DevuelveNull()
    {
        var lines = new[]
        {
            Header,
            "EMP-201,Javier Galaviz,CAR-WASH,,,Activo,,,,,",
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
            "EMP-001,Adrian Uribe,CAR-WASH,,45267,Activo,,,,,",
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
            "EMP-001,Adrian Uribe,CAR-WASH,,07/12/2023,Activo,,,,,",
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
            "EMP-001,Adrian Uribe,CAR-WASH,,32/13/2023,Activo,,,,,",
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
            $"EMP-001,Adrian Uribe,CAR-WASH,,,{statusText},,,,,",
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
            "EMP-001,Adrian Uribe,CAR-WASH,,,DeVacaciones,,,,,",
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
            "EMP-001,Adrian Uribe,CAR-WASH,,,,,,,,",
            "EMP-001,Otra Persona,CAR-WASH,,,,,,,,",
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
            "EMP-001,Adrian Uribe,CAR-WASH,,,,,,,,",
        };

        var result = EmployeeCatalogReplaceParser.Parse(lines);

        var row = Assert.Single(result.Rows);
        Assert.Null(row.WeeklySalary);
        Assert.Contains(row.Alerts, a => a.Contains("pendiente de captura"));
    }

    [Theory]
    [InlineData(",Adrian Uribe,CAR-WASH,,,,,,,,")] // Number vacío
    [InlineData("EMP-001,,CAR-WASH,,,,,,,,")] // FullName vacío
    [InlineData("EMP-001,Adrian Uribe,,,,,,,,,")] // Area vacía
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
            "EMP-001,Adrian Uribe,CAR-WASH,,,,,,,42,",
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
            "EMP-001,Adrian Uribe,CAR-WASH,,,,,,,,",
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
            "EMP-001,Adrian Uribe,CAR-WASH,,,,,,,EMP-001,",
        };

        var result = EmployeeCatalogReplaceParser.Parse(lines);

        Assert.Empty(result.Rows);
        Assert.Single(result.Errors);
        Assert.Contains("Pin", result.Errors[0]);
    }

    [Fact]
    public void Parse_ConEncabezadoQueSoloTraeLasColumnasObligatorias_LoAceptaConElRestoEnNull()
    {
        // Las columnas se resuelven por NOMBRE, no por posición fija — un encabezado
        // reducido a solo lo obligatorio (Number, FullName, Area) es válido, el resto de
        // los campos simplemente caen en su default ("no capturado").
        var lines = new[]
        {
            "Number,FullName,Area,Position,WeeklySalary,OvertimeHourlyRate,Notes",
            "EMP-001,Adrian Uribe,CAR-WASH,,3800,,",
        };

        var result = EmployeeCatalogReplaceParser.Parse(lines);

        Assert.Empty(result.Errors);
        var row = Assert.Single(result.Rows);
        Assert.Equal(3800m, row.WeeklySalary);
        Assert.Null(row.Pin);
        Assert.Null(row.Department);
        Assert.Null(row.ScheduledStartTime);
        Assert.Null(row.ScheduledEndTime);
    }

    [Fact]
    public void Parse_ConColumnaObligatoriaFaltante_ReportaError()
    {
        var lines = new[]
        {
            "Number,FullName,Position,WeeklySalary,OvertimeHourlyRate,Notes", // sin Area
            "EMP-001,Adrian Uribe,,3800,,",
        };

        var result = EmployeeCatalogReplaceParser.Parse(lines);

        Assert.Empty(result.Rows);
        Assert.Single(result.Errors);
        Assert.Contains("obligatorias", result.Errors[0]);
    }

    [Fact]
    public void Parse_ConColumnaDesconocida_ReportaError()
    {
        // Un formato genuinamente distinto (p. ej. "Nombre" en vez de "FullName") se
        // rechaza con un error claro, en vez de importarse en silencio con Number/FullName/
        // Area vacíos.
        var lines = new[]
        {
            "Number,Nombre,Area",
            "EMP-001,Adrian Uribe,CAR-WASH",
        };

        var result = EmployeeCatalogReplaceParser.Parse(lines);

        Assert.Empty(result.Rows);
        Assert.Single(result.Errors);
        Assert.Contains("no se reconocen", result.Errors[0]);
    }

    [Fact]
    public void Parse_ConColumnaVaciaSobranteAlFinal_LaIgnora()
    {
        // Caso real: al guardar un CSV desde Excel a veces queda una coma sobrante al
        // final de cada línea (columna sin nombre, siempre vacía) — no debe contarse como
        // columna "desconocida" ni romper el conteo de columnas por fila.
        var lines = new[]
        {
            Header + ",",
            "EMP-001,Adrian Uribe Garcia,CAR-WASH,Gerencia,2023-12-07,Activo,3800,135.71,Nota,7,Plaza Sabo,",
        };

        var result = EmployeeCatalogReplaceParser.Parse(lines);

        Assert.Empty(result.Errors);
        var row = Assert.Single(result.Rows);
        Assert.Equal("EMP-001", row.Number);
        Assert.Equal("Plaza Sabo", row.Department);
    }

    [Theory]
    [InlineData("8:00 AM", "4:00 PM")]
    [InlineData("08:00", "16:00")]
    public void Parse_ConHorarioValido_LoAsignaALaFila(string horaEntrada, string horaSalida)
    {
        var lines = new[]
        {
            "Number,FullName,Area,Hora Entrada,Hora Salida",
            $"EMP-001,Adrian Uribe,CAR-WASH,{horaEntrada},{horaSalida}",
        };

        var result = EmployeeCatalogReplaceParser.Parse(lines);

        Assert.Empty(result.Errors);
        var row = Assert.Single(result.Rows);
        Assert.Equal(new TimeOnly(8, 0), row.ScheduledStartTime);
        Assert.Equal(new TimeOnly(16, 0), row.ScheduledEndTime);
    }

    [Fact]
    public void Parse_ConSoloHoraEntradaSinHoraSalida_ReportaError()
    {
        var lines = new[]
        {
            "Number,FullName,Area,Hora Entrada,Hora Salida",
            "EMP-001,Adrian Uribe,CAR-WASH,8:00 AM,",
        };

        var result = EmployeeCatalogReplaceParser.Parse(lines);

        Assert.Empty(result.Rows);
        Assert.Single(result.Errors);
        Assert.Contains("captura ambas horas", result.Errors[0]);
    }

    [Fact]
    public void Parse_ConHorarioYDepartmentEnElMismoArchivo_LosAceptaLosDos()
    {
        // Pedido explícito del usuario: el catálogo "clásico" (Department) y el nuevo
        // (Hora Entrada/Hora Salida) no son mutuamente excluyentes.
        var lines = new[]
        {
            "Number,FullName,Area,Department,Hora Entrada,Hora Salida",
            "EMP-001,Adrian Uribe,CAR-WASH,Plaza Sabo,8:00 AM,4:00 PM",
        };

        var result = EmployeeCatalogReplaceParser.Parse(lines);

        Assert.Empty(result.Errors);
        var row = Assert.Single(result.Rows);
        Assert.Equal("Plaza Sabo", row.Department);
        Assert.Equal(new TimeOnly(8, 0), row.ScheduledStartTime);
        Assert.Equal(new TimeOnly(16, 0), row.ScheduledEndTime);
    }

    [Fact]
    public void Parse_ArchivoVacio_DevuelveError()
    {
        var result = EmployeeCatalogReplaceParser.Parse([]);

        Assert.Empty(result.Rows);
        Assert.Single(result.Errors);
    }
}
