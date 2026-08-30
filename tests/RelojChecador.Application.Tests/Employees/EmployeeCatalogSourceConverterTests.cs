using RelojChecador.Application.Employees;

namespace RelojChecador.Application.Tests.Employees;

public class EmployeeCatalogSourceConverterTests
{
    private static readonly string[] RegistroEmpleadosHeader =
    [
        "ID Empleado", "Nombre completo", "Fecha de ingreso", "Estado", "Fecha de salida",
        "Antigüedad (años)", "Sexo", "Lugar de trabajo", "Posición", "Sueldo", "Fecha de nacimiento",
    ];

    private static readonly string[] CanonicalHeader =
        ["Number", "FullName", "Area", "Position", "HireDate", "Status", "WeeklySalary", "OvertimeHourlyRate", "Notes", "Pin", "Department"];

    [Fact]
    public void IsCanonicalHeader_ConElEncabezadoDelCatalogoDeReemplazo_DevuelveTrue()
    {
        Assert.True(EmployeeCatalogSourceConverter.IsCanonicalHeader(CanonicalHeader));
    }

    [Fact]
    public void IsCanonicalHeader_ConOtroEncabezado_DevuelveFalse()
    {
        Assert.False(EmployeeCatalogSourceConverter.IsCanonicalHeader(RegistroEmpleadosHeader));
    }

    [Fact]
    public void TryConvert_ConEncabezadoRegistroEmpleados_ConvierteFilaCompleta()
    {
        var rows = new IReadOnlyList<string?>[]
        {
            [
                "1", "Adrian Uribe Garcia", "2023-12-07", "Activo", "",
                "2.7", "Hombre", "Plaza Sabo", "Gerencia", "3800", "20 Diciembre 2005",
            ],
        };

        var ok = EmployeeCatalogSourceConverter.TryConvert(RegistroEmpleadosHeader, rows, out var csvLines, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(string.Join(",", CanonicalHeader), csvLines[0]);
        var result = EmployeeCatalogReplaceParser.Parse(csvLines);
        Assert.Empty(result.Errors);
        var row = Assert.Single(result.Rows);
        Assert.Equal("1", row.Number);
        Assert.Equal("Adrian Uribe Garcia", row.FullName);
        // Area SIEMPRE es la sucursal unificada — pedido explícito del usuario tras fusionar
        // varias sucursales en una sola ("sí fusiónalos"). El "Lugar de trabajo" real del
        // Excel (Plaza Sabo, distinto de la unificada) se conserva en Department.
        Assert.Equal("CAR-WASH", row.Area);
        Assert.Equal("Plaza Sabo", row.Department);
        Assert.Equal("Gerencia", row.Position);
        Assert.Equal(new DateOnly(2023, 12, 7), row.HireDate);
        Assert.Equal(3800m, row.WeeklySalary);
        Assert.Equal("Sexo: Hombre | Fecha de nacimiento: 20 Diciembre 2005", row.Notes);
        Assert.Equal("1", row.Pin); // Pin = mismo ID Empleado, convención del negocio
    }

    [Fact]
    public void TryConvert_ConLugarDeTrabajoYaIgualALaSucursalUnificada_DepartmentQuedaVacio()
    {
        // Si el "Lugar de trabajo" del Excel YA es la sucursal unificada, guardarlo también
        // en Department sería puro ruido repetido — solo se guarda cuando aporta algo.
        var rows = new IReadOnlyList<string?>[]
        {
            ["1", "Adrian Uribe Garcia", "", "Activo", "", "", "", "CAR-WASH", "", "", ""],
        };

        EmployeeCatalogSourceConverter.TryConvert(RegistroEmpleadosHeader, rows, out var csvLines, out _);

        var row = Assert.Single(EmployeeCatalogReplaceParser.Parse(csvLines).Rows);
        Assert.Equal("CAR-WASH", row.Area);
        Assert.Null(row.Department);
    }

    [Fact]
    public void TryConvert_RespetaElPinTalCualVengaEnIdEmpleado_InclusoUnoAtipicoComo201()
    {
        // Caso real que motivó esta clase: Javier Galaviz ya estaba dado de alta en el
        // reloj checador físico con PIN 201, y el Excel lo trae con ID Empleado=201.
        var rows = new IReadOnlyList<string?>[]
        {
            ["201", "Javier Galaviz", "", "Activo", "", "", "", "CAR-WASH", "", "", ""],
        };

        var ok = EmployeeCatalogSourceConverter.TryConvert(RegistroEmpleadosHeader, rows, out var csvLines, out _);

        Assert.True(ok);
        var row = Assert.Single(EmployeeCatalogReplaceParser.Parse(csvLines).Rows);
        Assert.Equal("201", row.Number);
        Assert.Equal("201", row.Pin);
    }

    [Fact]
    public void TryConvert_IgnoraFilasEnBlanco()
    {
        var rows = new IReadOnlyList<string?>[]
        {
            ["1", "Adrian Uribe Garcia", "", "Activo", "", "", "", "CAR-WASH", "", "", ""],
            ["", "", "", "", "", "", "", "", "", "", ""],
            [null, null, null, null, null, null, null, null, null, null, null],
        };

        var ok = EmployeeCatalogSourceConverter.TryConvert(RegistroEmpleadosHeader, rows, out var csvLines, out _);

        Assert.True(ok);
        Assert.Equal(2, csvLines.Count); // encabezado + una sola fila con datos
    }

    [Fact]
    public void TryConvert_ConEstadoInactivo_LoRespeta()
    {
        var rows = new IReadOnlyList<string?>[]
        {
            ["9", "Antony Salvador Beltran Garcia", "", "Inactivo", "", "", "Hombre", "CAR-WASH", "", "", ""],
        };

        EmployeeCatalogSourceConverter.TryConvert(RegistroEmpleadosHeader, rows, out var csvLines, out _);

        var row = Assert.Single(EmployeeCatalogReplaceParser.Parse(csvLines).Rows);
        Assert.Equal(RelojChecador.Domain.Employees.EmploymentStatus.Inactive, row.Status);
    }

    [Fact]
    public void TryConvert_ConPrimeraColumnaLlamadaPin_TambienLaReconoce()
    {
        // El usuario le cambió el nombre a la primera columna de "ID Empleado" a "PIN" en
        // una revisión posterior del Excel maestro — mismo dato, mismo significado.
        string[] headerConPin =
        [
            "PIN", "Nombre completo", "Fecha de ingreso", "Estado", "Fecha de salida",
            "Antigüedad (años)", "Sexo", "Lugar de trabajo", "Posición", "Sueldo", "Fecha de nacimiento",
        ];
        var rows = new IReadOnlyList<string?>[]
        {
            ["1", "Adrian Uribe Garcia", "", "Activo", "", "", "", "CAR-WASH", "", "", ""],
        };

        Assert.True(EmployeeCatalogSourceConverter.IsRecognizedHeader(headerConPin));
        var ok = EmployeeCatalogSourceConverter.TryConvert(headerConPin, rows, out var csvLines, out var error);

        Assert.True(ok);
        Assert.Null(error);
        var row = Assert.Single(EmployeeCatalogReplaceParser.Parse(csvLines).Rows);
        Assert.Equal("1", row.Number);
        Assert.Equal("1", row.Pin);
        Assert.Equal("CAR-WASH", row.Area);
    }

    [Fact]
    public void TryConvert_ConEncabezadoDesconocido_DevuelveError()
    {
        var ok = EmployeeCatalogSourceConverter.TryConvert(
            ["Columna A", "Columna B"], [["x", "y"]], out var csvLines, out var error);

        Assert.False(ok);
        Assert.Empty(csvLines);
        Assert.NotNull(error);
    }
}
