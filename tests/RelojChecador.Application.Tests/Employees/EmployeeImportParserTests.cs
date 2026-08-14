using RelojChecador.Application.Employees;

namespace RelojChecador.Application.Tests.Employees;

public class EmployeeImportParserTests
{
    private const string Header = "Number,FullName,Area,Position,WeeklySalary,OvertimeHourlyRate,Notes";

    [Fact]
    public void Parse_FilaCompletaYValida_DevuelveLaFilaConLosValoresCorrectos()
    {
        var lines = new[]
        {
            Header,
            "EMP-001,Adrian Uribe,CAR WASH,GERENTE,3800,135.71,Horario y sueldo",
        };

        var result = EmployeeImportParser.Parse(lines);

        Assert.Empty(result.Errors);
        var row = Assert.Single(result.Rows);
        Assert.Equal("EMP-001", row.Number);
        Assert.Equal("Adrian Uribe", row.FullName);
        Assert.Equal("CAR WASH", row.Area);
        Assert.Equal("GERENTE", row.Position);
        Assert.Equal(3800m, row.WeeklySalary);
        Assert.Equal(135.71m, row.OvertimeHourlyRate);
        Assert.Equal("Horario y sueldo", row.Notes);
        Assert.Empty(row.Alerts);
    }

    [Fact]
    public void Parse_ConSueldoYTarifaVacios_LosDejaEnNullNuncaEnCero()
    {
        var lines = new[]
        {
            Header,
            "EMP-051,Ana Laura,CAFETERIA,BARISTA,,,Aparece solo en horario",
        };

        var result = EmployeeImportParser.Parse(lines);

        var row = Assert.Single(result.Rows);
        Assert.Null(row.WeeklySalary);
        Assert.Null(row.OvertimeHourlyRate);
        Assert.Contains(row.Alerts, a => a.Contains("pendiente de captura"));
    }

    [Fact]
    public void Parse_ConPuestoSinPuesto_GeneraAlerta()
    {
        var lines = new[]
        {
            Header,
            "EMP-013,Sebastian Jimenez,CAR WASH,SIN PUESTO,2400,,",
        };

        var result = EmployeeImportParser.Parse(lines);

        var row = Assert.Single(result.Rows);
        Assert.Contains(row.Alerts, a => a.Contains("Sin puesto"));
    }

    [Fact]
    public void Parse_ConPosicionYNotasVacias_LasDejaEnNull()
    {
        var lines = new[]
        {
            Header,
            "EMP-002,Angel David,CAR WASH,,3400,,",
        };

        var result = EmployeeImportParser.Parse(lines);

        var row = Assert.Single(result.Rows);
        Assert.Null(row.Position);
        Assert.Null(row.Notes);
    }

    [Theory]
    [InlineData(",Angel David,CAR WASH,SUPERVISOR,3400,,")] // Number vacío
    [InlineData("EMP-002,,CAR WASH,SUPERVISOR,3400,,")] // FullName vacío
    [InlineData("EMP-002,Angel David,,SUPERVISOR,3400,,")] // Area vacía
    public void Parse_ConCampoObligatorioVacio_ReportaErrorYNoAgregaLaFila(string line)
    {
        var lines = new[] { Header, line };

        var result = EmployeeImportParser.Parse(lines);

        Assert.Empty(result.Rows);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Parse_ConSueldoNoNumerico_ReportaErrorYNoAgregaLaFila()
    {
        var lines = new[]
        {
            Header,
            "EMP-002,Angel David,CAR WASH,SUPERVISOR,no-es-un-numero,,",
        };

        var result = EmployeeImportParser.Parse(lines);

        Assert.Empty(result.Rows);
        Assert.Single(result.Errors);
        Assert.Contains("WeeklySalary", result.Errors[0]);
    }

    [Fact]
    public void Parse_ConSueldoNegativo_ReportaError()
    {
        var lines = new[]
        {
            Header,
            "EMP-002,Angel David,CAR WASH,SUPERVISOR,-100,,",
        };

        var result = EmployeeImportParser.Parse(lines);

        Assert.Empty(result.Rows);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Parse_ConEncabezadoDistinto_DevuelveErrorYNingunaFila()
    {
        var lines = new[]
        {
            "Numero,Nombre,Sucursal",
            "EMP-001,Adrian Uribe,CAR WASH",
        };

        var result = EmployeeImportParser.Parse(lines);

        Assert.Empty(result.Rows);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Parse_ArchivoVacio_DevuelveError()
    {
        var result = EmployeeImportParser.Parse([]);

        Assert.Empty(result.Rows);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Parse_ConLineaEnBlancoAlFinal_LaIgnoraSinError()
    {
        var lines = new[]
        {
            Header,
            "EMP-001,Adrian Uribe,CAR WASH,GERENTE,3800,,",
            "",
        };

        var result = EmployeeImportParser.Parse(lines);

        Assert.Single(result.Rows);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Parse_UnaLineaConErrorNoDetieneElRestoDelArchivo()
    {
        var lines = new[]
        {
            Header,
            "EMP-001,Adrian Uribe,CAR WASH,GERENTE,no-valido,,",
            "EMP-002,Angel David,CAR WASH,SUPERVISOR,3400,,",
        };

        var result = EmployeeImportParser.Parse(lines);

        Assert.Single(result.Rows);
        Assert.Equal("EMP-002", result.Rows[0].Number);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Parse_ConCamposEntreComillasYComasInternas_LosInterpretaCorrectamente()
    {
        var lines = new[]
        {
            Header,
            "EMP-001,\"Uribe, Adrian\",CAR WASH,GERENTE,3800,,\"Nota con \"\"comillas\"\" internas\"",
        };

        var result = EmployeeImportParser.Parse(lines);

        var row = Assert.Single(result.Rows);
        Assert.Equal("Uribe, Adrian", row.FullName);
        Assert.Equal("Nota con \"comillas\" internas", row.Notes);
    }
}
