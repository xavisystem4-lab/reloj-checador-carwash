namespace RelojChecador.Application.Common;

/// <summary>
/// Resultado de una operación que puede fallar de forma esperada. Se usa en toda la capa
/// de Application (y muy especialmente en <c>IAttendanceDeviceAdapter</c>) en vez de
/// excepciones, para poder distinguir con precisión niveles de fallo — p. ej. "IP inválida"
/// vs "puerto cerrado" vs "autenticación rechazada" — sin recurrir al costo ni a la
/// ambigüedad de lanzar y atrapar excepciones para control de flujo esperado.
/// </summary>
public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
        {
            throw new InvalidOperationException("Un resultado exitoso no puede tener un error asociado.");
        }

        if (!isSuccess && error == Error.None)
        {
            throw new InvalidOperationException("Un resultado fallido requiere un error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) => Result<TValue>.Success(value);
    public static Result<TValue> Failure<TValue>(Error error) => Result<TValue>.Failure(error);
}

public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("No se puede acceder a Value de un resultado fallido.");

    private Result(bool isSuccess, TValue? value, Error error) : base(isSuccess, error)
    {
        _value = value;
    }

    public static Result<TValue> Success(TValue value) => new(true, value, Error.None);

    public static new Result<TValue> Failure(Error error) => new(false, default, error);
}
