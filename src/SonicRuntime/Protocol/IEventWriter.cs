namespace SonicRuntime.Protocol;

/// <summary>
/// Abstraction for emitting unsolicited runtime events.
/// Engines use this to report state changes without coupling to transport.
/// </summary>
public interface IEventWriter
{
    void Write(string eventType, object? data);
}

/// <summary>
/// Silent event writer for tests or paths where events are not needed.
/// </summary>
public sealed class NullEventWriter : IEventWriter
{
    public static readonly NullEventWriter Instance = new();
    public void Write(string eventType, object? data) { }
}

/// <summary>
/// Bridges engine events to the CommandLoop's stdout writer.
/// Thread-safe — delegates to CommandLoop.WriteEvent which locks stdout.
///
/// Supports late binding: create with no loop, then call Connect() after
/// the CommandLoop is constructed. Events before Connect() are silently dropped.
/// This breaks the circular dependency: engines → event writer → loop → dispatcher → engines.
/// </summary>
public sealed class CommandLoopEventWriter : IEventWriter
{
    private volatile CommandLoop? _loop;

    public CommandLoopEventWriter() { }

    public CommandLoopEventWriter(CommandLoop loop)
    {
        _loop = loop;
    }

    public void Connect(CommandLoop loop)
    {
        _loop = loop;
    }

    public void Write(string eventType, object? data)
    {
        _loop?.WriteEvent(new RuntimeEvent
        {
            Event = eventType,
            Data = data
        });
    }
}
