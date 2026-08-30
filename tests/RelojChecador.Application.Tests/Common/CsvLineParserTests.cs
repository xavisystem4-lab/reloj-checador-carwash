using RelojChecador.Application.Common;

namespace RelojChecador.Application.Tests.Common;

public class CsvLineParserTests
{
    [Fact]
    public void SplitLine_ConComaPorDefecto_SeparaCorrectamente()
    {
        var fields = CsvLineParser.SplitLine("a,b,c");

        Assert.Equal(["a", "b", "c"], fields);
    }

    [Fact]
    public void SplitLine_ConDelimitadorPuntoYComa_SeparaCorrectamente()
    {
        var fields = CsvLineParser.SplitLine("a;b;c", ';');

        Assert.Equal(["a", "b", "c"], fields);
    }

    [Fact]
    public void SplitLine_ConComillasYPuntoYComaComoDelimitador_RespetaLasComillas()
    {
        var fields = CsvLineParser.SplitLine("\"a;1\";b;c", ';');

        Assert.Equal(["a;1", "b", "c"], fields);
    }

    [Fact]
    public void DetectDelimiter_ConMasComasQuePuntoYComa_DevuelveComa()
    {
        Assert.Equal(',', CsvLineParser.DetectDelimiter("Number,FullName,Area"));
    }

    [Fact]
    public void DetectDelimiter_ConMasPuntoYComaQueComas_DevuelvePuntoYComa()
    {
        // Caso real: Excel con configuración regional en español exporta CSV así, porque
        // usa la coma como separador decimal.
        Assert.Equal(';', CsvLineParser.DetectDelimiter("Number;FullName;Area"));
    }

    [Fact]
    public void DetectDelimiter_SinNingunoDeLosDos_AsumeComa()
    {
        Assert.Equal(',', CsvLineParser.DetectDelimiter("SoloUnaColumna"));
    }

    [Fact]
    public void DetectDelimiter_ConEmpate_AsumeComa()
    {
        Assert.Equal(',', CsvLineParser.DetectDelimiter("a,b;c"));
    }
}
