namespace Selficient.Domain.Primitives;

/// <summary>
/// The outcome of an operation that can fail for an expected reason. Callers must handle both
/// paths; expected failures never travel as exceptions.
/// </summary>
public sealed class Result<T>
{
    private readonly T? _value;

    private Result(T? value, Fault error, bool isSuccess)
    {
        _value = value;
        Error = error;
        IsSuccess = isSuccess;
    }

    /// <summary>True when the operation produced a value.</summary>
    public bool IsSuccess { get; }

    /// <summary>True when the operation failed for an expected reason.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>The failure, or <see cref="Fault.None"/> on success.</summary>
    public Fault Error { get; }

    /// <summary>The value. Reading it on a failure is a programming error and throws.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"The result failed with '{Error.Code}', so it has no value to read.");

    internal static Result<T> Ok(T value) => new(value, Fault.None, isSuccess: true);

    internal static Result<T> Fail(Fault error) => new(default, error, isSuccess: false);

    /// <summary>Collapses both paths into one value.</summary>
    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Fault, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return IsSuccess ? onSuccess(_value!) : onFailure(Error);
    }

    /// <summary>Transforms the value on success; carries the error through on failure.</summary>
    public Result<TOut> Map<TOut>(Func<T, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return IsSuccess ? Result<TOut>.Ok(map(_value!)) : Result<TOut>.Fail(Error);
    }

    /// <summary>Chains an operation that can itself fail.</summary>
    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        return IsSuccess ? bind(_value!) : Result<TOut>.Fail(Error);
    }
}

/// <summary>Factories for <see cref="Result{T}"/>.</summary>
public static class Result
{
    /// <summary>A successful result carrying <paramref name="value"/>.</summary>
    public static Result<T> Success<T>(T value) => Result<T>.Ok(value);

    /// <summary>A failed result carrying <paramref name="error"/>.</summary>
    public static Result<T> Failure<T>(Fault error) => Result<T>.Fail(error);
}
