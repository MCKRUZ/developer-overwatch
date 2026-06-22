namespace DeveloperOverwatch.Domain.Common;

/// <summary>
/// Non-generic view over a <see cref="Result{T}"/>, allowing cross-cutting code
/// (e.g. MediatR pipeline behaviors) to inspect success without knowing the value type.
/// </summary>
public interface IResult
{
    bool IsSuccess { get; }
}

public sealed class Result<T> : IResult
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }
    public IReadOnlyList<string> ValidationErrors { get; }

    private Result(bool isSuccess, T? value, string? error, IReadOnlyList<string>? validationErrors)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
        ValidationErrors = validationErrors ?? [];
    }

    public static Result<T> Success(T value) => new(true, value, null, null);

    public static Result<T> Fail(string error) => new(false, default, error, null);

    public static Result<T> ValidationFailure(IReadOnlyList<string> errors) =>
        new(false, default, "Validation failed", errors);
}
