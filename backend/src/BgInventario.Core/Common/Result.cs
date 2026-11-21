namespace BgInventario.Core.Common;

public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Unauthorized,

    Unavailable
}

public sealed record ErrorDetail(string Code, string Message, ErrorType Type)
{
    public static ErrorDetail Validation(string code, string message) => new(code, message, ErrorType.Validation);
    public static ErrorDetail NotFound(string code, string message) => new(code, message, ErrorType.NotFound);
    public static ErrorDetail Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
    public static ErrorDetail Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);
    public static ErrorDetail Unavailable(string code, string message) => new(code, message, ErrorType.Unavailable);
}

public class Result
{
    protected Result(ErrorDetail? error) => Error = error;

    public ErrorDetail? Error { get; }
    public bool IsSuccess => Error is null;

    public static Result Success() => new(null);
    public static Result Failure(ErrorDetail error) => new(error);

    public static implicit operator Result(ErrorDetail error) => Failure(error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;
    private Result(T value) : base(null) => _value = value;
    private Result(ErrorDetail error) : base(error) { }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access the value of a failed result.");

    public static implicit operator Result<T>(T value) => new(value);
    public static implicit operator Result<T>(ErrorDetail error) => new(error);
}
