using System.Text.Json;

namespace SonicRuntime.Protocol;

/// <summary>
/// Reads newline-delimited JSON from stdin, dispatches to the handler,
/// writes responses to stdout. stderr is for logging only.
///
/// This is the only component that touches stdin/stdout.
/// Nothing else in the runtime may write to Console.Out.
/// </summary>
public sealed class CommandLoop
{
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly TextWriter _log;
    private readonly CommandDispatcher _dispatcher;

    public CommandLoop(
        CommandDispatcher dispatcher,
        TextReader? input = null,
        TextWriter? output = null,
        TextWriter? log = null)
    {
        _dispatcher = dispatcher;
        _input = input ?? Console.In;
        _output = output ?? Console.Out;
        _log = log ?? Console.Error;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        _log.WriteLine("[sonic-runtime] command loop started");

        while (!ct.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await _input.ReadLineAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (line is null)
                break; // stdin closed

            if (string.IsNullOrWhiteSpace(line))
                continue;

            await ProcessLineAsync(line);
        }

        _log.WriteLine("[sonic-runtime] command loop stopped");
    }

    private async Task ProcessLineAsync(string line)
    {
        RuntimeRequest? request = null;
        try
        {
            request = JsonSerializer.Deserialize(line, RuntimeJsonContext.Default.RuntimeRequest);
            if (request is null)
            {
                _log.WriteLine($"[sonic-runtime] null request from line: {line}");
                return;
            }

            var result = await _dispatcher.DispatchAsync(request);
            var response = new RuntimeResponse { Id = request.Id, Result = result };
            WriteResponse(response);
        }
        catch (RuntimeException ex)
        {
            var errorResponse = new RuntimeErrorResponse
            {
                Id = request?.Id ?? 0,
                Error = new RuntimeError
                {
                    Code = ex.Code,
                    Message = ex.Message,
                    Retryable = ex.Retryable
                }
            };
            WriteErrorResponse(errorResponse);
        }
        catch (Exception ex)
        {
            _log.WriteLine($"[sonic-runtime] unhandled error: {ex}");
            var errorResponse = new RuntimeErrorResponse
            {
                Id = request?.Id ?? 0,
                Error = new RuntimeError
                {
                    Code = "internal_error",
                    Message = ex.Message,
                    Retryable = false
                }
            };
            WriteErrorResponse(errorResponse);
        }
    }

    private void WriteResponse(RuntimeResponse response)
    {
        var json = JsonSerializer.Serialize(response, RuntimeJsonContext.Default.RuntimeResponse);
        _output.WriteLine(json);
        _output.Flush();
    }

    private void WriteErrorResponse(RuntimeErrorResponse response)
    {
        var json = JsonSerializer.Serialize(response, RuntimeJsonContext.Default.RuntimeErrorResponse);
        _output.WriteLine(json);
        _output.Flush();
    }
}
