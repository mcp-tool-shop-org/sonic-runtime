namespace SonicRuntime.Protocol;

/// <summary>
/// Typed exception that maps directly to the wire protocol error shape.
/// Thrown by engine components, caught by CommandLoop.
/// </summary>
public sealed class RuntimeException : Exception
{
    public string Code { get; }
    public bool Retryable { get; }

    public RuntimeException(string code, string message, bool retryable = false)
        : base(message)
    {
        Code = code;
        Retryable = retryable;
    }
}
