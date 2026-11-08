namespace Bifrost.Application.Results;

/// <summary>Categoria da falha. A API usa a categoria para escolher o status HTTP.</summary>
public enum ErrorType
{
    /// <summary>Entrada fora das regras de formato (400).</summary>
    Validation = 1,

    /// <summary>O recurso pedido não existe (404).</summary>
    NotFound = 2,

    /// <summary>Conflito com o que já existe, como um SKU repetido (409).</summary>
    Conflict = 3,

    /// <summary>Regra de negócio violada no estado atual, como cancelar um pedido pago (422).</summary>
    BusinessRule = 4,
}

/// <summary>Campo que não passou na validação.</summary>
public sealed record FieldError(string Field, string Message);

/// <summary>
/// Falha esperada de um caso de uso. O <see cref="Code"/> é estável e serve para quem consome a API tratar o erro;
/// a <see cref="Message"/> é para pessoas.
/// </summary>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    public IReadOnlyList<FieldError> Fields { get; init; } = [];

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error BusinessRule(string code, string message) => new(code, message, ErrorType.BusinessRule);

    public static Error Validation(IReadOnlyList<FieldError> fields) =>
        new("validation", "Um ou mais campos não passaram na validação.", ErrorType.Validation) { Fields = fields };
}

/// <summary>O que o pipeline precisa saber de qualquer resultado, sem conhecer o tipo do valor.</summary>
public interface IResult
{
    bool IsFailure { get; }

    Error? Error { get; }
}

/// <summary>
/// Permite que um decorator genérico crie uma falha do tipo exato que o handler devolveria.
/// </summary>
/// <remarks>
/// O decorator de validação conhece só <c>TResult</c>. Com um membro estático abstrato, ele chama
/// <c>TResult.Failure(error)</c> e devolve um <see cref="Result{T}"/> do tipo certo, sem reflexão e sem exceção.
/// </remarks>
public interface IFailureFactory<out TSelf> : IResult
    where TSelf : IFailureFactory<TSelf>
{
    static abstract TSelf Failure(Error error);
}

/// <summary>Resultado de um caso de uso: um valor ou um <see cref="Error"/>, nunca os dois.</summary>
public sealed class Result<T> : IFailureFactory<Result<T>>
{
    private readonly T? _value;

    private Result(T? value, Error? error)
    {
        _value = value;
        Error = error;
    }

    public Error? Error { get; }

    public bool IsSuccess => Error is null;

    public bool IsFailure => !IsSuccess;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"O resultado é uma falha ({Error!.Code}) e não tem valor.");

    public static Result<T> Success(T value) => new(value, null);

    public static Result<T> Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>(default, error);
    }

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure(error);

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return IsSuccess ? onSuccess(_value!) : onFailure(Error!);
    }
}
