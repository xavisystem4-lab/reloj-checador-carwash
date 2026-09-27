using RelojChecador.Application.Common;
using RelojChecador.Application.Employees;

namespace RelojChecador.Application.Tests.Employees;

public class EmployeeCatalogGenericConverterTests
{
    private static IReadOnlyList<IReadOnlyList<string?>> Rows(params string?[][] rows) => rows;

    private static Dictionary<string, string> Parse(IReadOnlyList<string> csvLines, int row)
    {
        var header = CsvLineParser.SplitLine(csvLines[0], ',');
        var fields = CsvLineParser.SplitLine(csvLines[row], ',');
        return header.Zip(fields).ToDictionary(p => p.First, p => p.Second);
    }

    [Fact]
    public void NombreYApellidosSeparados_SeJuntanEnNombreCompleto()
    {
        string[] header = ["No. Empleado", "Nombre(s)", "Apellido Paterno", "Apellido Materno", "Puesto", "Sueldo Semanal"];
        var ok = EmployeeCatalogGenericConverter.TryConvert(header,
            Rows(["12", "Adrián", "Uribe", "García", "Lavador", "$1,850.00"]), out var lines, out var description, out var error);

        Assert.True(ok, error);
        var row = Parse(lines, 1);
        Assert.Equal("12", row["Number"]);
        Assert.Equal("12", row["Pin"]);
        Assert.Equal("Adrián Uribe García", row["FullName"]);
        Assert.Equal("CAR-WASH", row["Area"]);
        Assert.Equal("Lavador", row["Position"]);
        Assert.Equal("1850.00", row["WeeklySalary"]);
        Assert.Contains("Apellido paterno", description);
    }

    [Fact]
    public void ElResultadoLoAceptaElParserDelCatalogo()
    {
        string[] header = ["Empleado", "Fecha de ingreso", "Estatus", "Sucursal", "Teléfono", "Antigüedad"];
        EmployeeCatalogGenericConverter.TryConvert(header,
            Rows(["PEREZ LOPEZ, JUAN", "05/03/2024", "Baja", "Plaza Sabo", "686 123 4567", "2"]), out var lines, out var description, out _);

        var parsed = EmployeeCatalogReplaceParser.Parse([.. lines.Select((l, i) => i == 1 ? "EMP-900" + l : l)]);

        Assert.False(parsed.HasErrors, string.Join(" | ", parsed.Errors));
        var row = Assert.Single(parsed.Rows);
        Assert.Equal("Juan Perez Lopez", row.FullName);
        Assert.Equal(new DateOnly(2024, 3, 5), row.HireDate);
        Assert.Equal("Plaza Sabo", row.Department);
        Assert.Contains("Tel: 686 123 4567", row.Notes);
        Assert.Contains("Antigüedad", description); // columna ignorada, se avisa
    }

    [Fact]
    public void SinNumeroNiPin_DejaNumeroVacioParaQueLoCompleteLaApp()
    {
        string[] header = ["Nombre", "Puesto"];
        EmployeeCatalogGenericConverter.TryConvert(header, Rows(["Luis Luna", "Cajero"]), out var lines, out _, out _);

        var row = Parse(lines, 1);
        Assert.Equal("", row["Number"]);
        Assert.Equal("Luis Luna", row["FullName"]);
    }

    [Fact]
    public void SinColumnaDeNombre_NoSeReconoce()
    {
        Assert.False(EmployeeCatalogGenericConverter.IsRecognizableHeader(["Puesto", "Sueldo"]));
        Assert.False(EmployeeCatalogGenericConverter.TryConvert(["Puesto", "Sueldo"], Rows(["A", "1"]), out _, out _, out var error));
        Assert.Contains("nombre", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnTituloNoSeConfundeConEncabezado()
    {
        Assert.False(EmployeeCatalogGenericConverter.IsRecognizableHeader(["Lista de empleados septiembre"]));
        Assert.False(EmployeeCatalogGenericConverter.IsRecognizableHeader(["Nombre"])); // solo una columna
        Assert.True(EmployeeCatalogGenericConverter.IsRecognizableHeader(["ID", "Nombre", ""]));
    }

    [Fact]
    public void ElConvertidorGeneralEntraCuandoNingunFormatoFijoCoincide()
    {
        string[] header = ["Clave", "Nombre Completo", "Cargo"];
        var ok = EmployeeCatalogSourceConverter.TryConvert(header, Rows(["EMP-003", "Alexander Santiago", "Supervisor"]),
            out var lines, out var description, out _);

        Assert.True(ok);
        Assert.True(EmployeeCatalogSourceConverter.IsRecognizedHeader(header));
        Assert.Equal("EMP-003", Parse(lines, 1)["Number"]);
        Assert.Equal("", Parse(lines, 1)["Pin"]); // "EMP-003" no es un PIN numérico
        Assert.StartsWith("Formato reconocido automáticamente", description);
    }

    [Theory]
    [InlineData("2024-01-15", "2024-01-15")]
    [InlineData("15/01/2024", "2024-01-15")]
    [InlineData("15-1-24", "2024-01-15")]
    [InlineData("45306", "2024-01-15")]
    [InlineData("", "")]
    public void NormalizeDate_DiaPrimero(string input, string expected) =>
        Assert.Equal(expected, EmployeeCatalogGenericConverter.NormalizeDate(input));
}
