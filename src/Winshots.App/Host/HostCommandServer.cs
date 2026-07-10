using System.IO.Pipes;
using System.Text.Json;
using Winshots.App.UI;

namespace Winshots.App.Host;

public sealed class HostCommandServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    private readonly MainForm _mainForm;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly HostEndpointRegistry _endpointRegistry;
    private Task? _serverTask;
    private int _disposed;

    public HostCommandServer(MainForm mainForm)
    {
        _mainForm = mainForm;
        PipeName = $"winshots-{Environment.ProcessId}-{Guid.NewGuid():N}";
        _endpointRegistry = new HostEndpointRegistry(PipeName);
    }

    public string PipeName { get; }

    public void Start()
    {
        _serverTask ??= Task.Run(() => RunAsync(_cancellation.Token));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _cancellation.Cancel();
        try
        {
            _serverTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Shutdown should not wait on a stale client connection.
        }

        _cancellation.Dispose();
        _endpointRegistry.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                await HandleClientAsync(pipe, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // Ignore malformed clients; Electron will surface command errors from responses.
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(pipe, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, leaveOpen: true)
        {
            AutoFlush = true
        };

        string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        string response;
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            object? result = await ExecuteCommandAsync(document.RootElement, cancellationToken).ConfigureAwait(false);
            response = JsonSerializer.Serialize(new { ok = true, result }, JsonOptions);
        }
        catch (Exception ex)
        {
            response = JsonSerializer.Serialize(new { ok = false, error = ex.Message }, JsonOptions);
        }

        await writer.WriteLineAsync(response.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private Task<object?> ExecuteCommandAsync(JsonElement request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string command = ReadString(request, "command");

        return command switch
        {
            "capture" => CaptureAsync(request),
            "timeline.toggle" => TimelineToggleAsync(request),
            "session.start" => SessionStartAsync(request),
            "session.stop" => SessionStopAsync(),
            "replay.status" => ReplayStatusAsync(),
            "replay.start" => ReplayStartAsync(request),
            "replay.stop" => ReplayStopAsync(),
            "replay.save" => ReplaySaveAsync(request),
            "status" => StatusAsync(),
            _ => throw new InvalidOperationException($"Unsupported Winshots host command: {command}")
        };
    }

    private async Task<object?> CaptureAsync(JsonElement request)
    {
        string reason = ReadOptionalString(request, "reason") ?? "electron";
        bool pasteToCodex = ReadOptionalBool(request, "pasteToCodex") ?? false;
        return await _mainForm.CaptureForHostAsync(reason, pasteToCodex).ConfigureAwait(false);
    }

    private async Task<object?> TimelineToggleAsync(JsonElement request)
    {
        int intervalMs = ReadOptionalInt(request, "intervalMs") ?? 60_000;
        return await _mainForm.ToggleTimelineForHostAsync(intervalMs).ConfigureAwait(false);
    }

    private async Task<object?> SessionStartAsync(JsonElement request)
    {
        int intervalMs = ReadOptionalInt(request, "intervalMs") ?? 60_000;
        int durationSeconds = ReadOptionalInt(request, "durationSeconds") ?? 300;
        return await _mainForm.StartVisualSessionForHostAsync(intervalMs, durationSeconds).ConfigureAwait(false);
    }

    private async Task<object?> SessionStopAsync()
    {
        return await _mainForm.StopVisualSessionForHostAsync().ConfigureAwait(false);
    }

    private async Task<object?> ReplayStatusAsync()
    {
        return await _mainForm.GetInstantReplayStatusForHostAsync().ConfigureAwait(false);
    }

    private async Task<object?> ReplayStartAsync(JsonElement request)
    {
        int lookbackSeconds = ReadOptionalInt(request, "lookbackSeconds") ?? 30;
        int intervalMs = ReadOptionalInt(request, "intervalMs") ?? 1000;
        return await _mainForm.StartInstantReplayForHostAsync(lookbackSeconds, intervalMs).ConfigureAwait(false);
    }

    private async Task<object?> ReplayStopAsync()
    {
        return await _mainForm.StopInstantReplayForHostAsync().ConfigureAwait(false);
    }

    private async Task<object?> ReplaySaveAsync(JsonElement request)
    {
        int? lookbackSeconds = ReadOptionalInt(request, "lookbackSeconds");
        return await _mainForm.SaveInstantReplayForHostAsync(lookbackSeconds).ConfigureAwait(false);
    }

    private async Task<object?> StatusAsync()
    {
        return await _mainForm.GetHostStatusAsync().ConfigureAwait(false);
    }

    private static string ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Missing required command field: {name}");
        }

        return value.GetString() ?? string.Empty;
    }

    private static string? ReadOptionalString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? ReadOptionalInt(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int result)
            ? result
            : null;
    }

    private static bool? ReadOptionalBool(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
    }
}
