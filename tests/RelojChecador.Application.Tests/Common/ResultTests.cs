using RelojChecador.Application.Common;

namespace RelojChecador.Application.Tests.Common;

public class ResultTests
{
    [Fact]
    public void Success_CreaResultadoExitosoSinError()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(Error.None, result.Error);
    }

    [Fact]
    public void Failure_CreaResultadoFallidoConError()
    {
        var error = new Error("Device.Timeout", "La operación superó el tiempo de espera.");

        var result = Result.Failure(error);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void GenericSuccess_ExponeElValor()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void GenericFailure_AccederAValue_LanzaInvalidOperationException()
    {
        var result = Result.Failure<int>(new Error("X", "algo falló"));

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Failure_ConErrorNone_LanzaInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Result.Failure(Error.None));
    }
}
