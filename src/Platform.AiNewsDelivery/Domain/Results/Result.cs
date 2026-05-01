namespace Platform.AiNewsDelivery.Domain.Results;

/// <summary>
/// Representa o resultado de uma operação que pode ter sucesso com um valor
/// ou falhar com uma mensagem de erro.
/// Usado no lugar de exceções para controle de fluxo de lógica de negócio,
/// seguindo o padrão estabelecido no MSEMC.
///
/// Nota: CA1000 (não declarar membros estáticos em tipos genéricos) é suprimido
/// intencionalmente — os factory methods Ok/Fail são o ponto de entrada do tipo
/// e a convenção Result&lt;T&gt;.Ok(v) é explicitamente mais legível que uma classe helper.
/// </summary>
#pragma warning disable CA1000 // Do not declare static members on generic types
public readonly record struct Result<T>
{
    public T? Value { get; }
    public string? Error { get; }
    public bool IsSuccess { get; }

    private Result(T value)
    {
        Value = value;
        IsSuccess = true;
        Error = null;
    }

    private Result(string error)
    {
        Error = error;
        IsSuccess = false;
        Value = default;
    }

    /// <summary>Cria um resultado de sucesso contendo o valor especificado.</summary>
    public static Result<T> Ok(T value) => new(value);

    /// <summary>Cria um resultado de falha com a mensagem de erro especificada.</summary>
    public static Result<T> Fail(string error) => new(error);

    /// <summary>
    /// Executa uma das duas funções dependendo do sucesso ou falha do resultado.
    /// Garante exhaustiveness — ambos os branches precisam ser tratados.
    /// </summary>
    public TResult Match<TResult>(
        Func<T, TResult> onSuccess,
        Func<string, TResult> onFailure) =>
        IsSuccess ? onSuccess(Value!) : onFailure(Error!);

    /// <summary>
    /// Projeta o valor de sucesso para outro tipo, propagando erros automaticamente.
    /// </summary>
    public Result<TNew> Map<TNew>(Func<T, TNew> mapper) =>
        IsSuccess ? Result<TNew>.Ok(mapper(Value!)) : Result<TNew>.Fail(Error!);
}
#pragma warning restore CA1000
