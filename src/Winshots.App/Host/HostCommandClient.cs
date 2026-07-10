using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;

namespace Winshots.App.Host;

public static class HostCommandClient
{
    public static async Task<JsonElement> SendAsync(
        string command,
        IReadOnlyDictionary<string, object?>? payload = null,
        TimeSpan? timeout = null,
        string? descriptorPath = null)
    {
        TimeSpan budget = timeout ?? TimeSpan.FromSeconds(10);
        using var cancellation = new CancellationTokenSource(budget);
        HostEndpointDescriptor descriptor = ReadLiveDescriptor(descriptorPath ?? HostEndpointRegistry.DefaultDescriptorPath);
        await using var pipe = new NamedPipeClientStream(
            ".",
            descriptor.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous,
            TokenImpersonationLevel.Identification);
        try
        {
            await pipe.ConnectAsync(cancellation.Token).ConfigureAwait(false);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            var request = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["command"] = command
            };
            if (payload is not null)
            {
                foreach ((string key, object? value) in payload)
                {
                    request[key] = value;
                }
            }

            string json = JsonSerializer.Serialize(request);
            await writer.WriteLineAsync(json.AsMemory(), cancellation.Token).ConfigureAwait(false);
            string? responseLine = await reader.ReadLineAsync(cancellation.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(responseLine))
            {
                throw new InvalidOperationException("The Winshots host returned an empty response.");
            }

            using JsonDocument response = JsonDocument.Parse(responseLine);
            if (!response.RootElement.TryGetProperty("ok", out JsonElement ok) || !ok.GetBoolean())
            {
                string error = response.RootElement.TryGetProperty("error", out JsonElement errorElement)
                    ? errorElement.GetString() ?? "Winshots host command failed."
                    : "Winshots host command failed.";
                throw new InvalidOperationException(error);
            }

            return response.RootElement.GetProperty("result").Clone();
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"Winshots host command '{command}' timed out after {budget.TotalSeconds:0.#} seconds.");
        }
    }

    internal static HostEndpointDescriptor ReadLiveDescriptor(string descriptorPath)
    {
        HostEndpointDescriptor descriptor = HostEndpointRegistry.TryRead(descriptorPath)
            ?? throw new InvalidOperationException("Winshots host is not running or its local descriptor is invalid.");
        if (string.IsNullOrWhiteSpace(descriptor.PipeName) || descriptor.ProcessId <= 0)
        {
            throw new InvalidOperationException("Winshots host descriptor is invalid.");
        }

        try
        {
            using Process process = Process.GetProcessById(descriptor.ProcessId);
            if (process.HasExited ||
                !DateTime.TryParse(descriptor.ProcessStartedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime expectedStart) ||
                Math.Abs((process.StartTime.ToUniversalTime() - expectedStart.ToUniversalTime()).TotalSeconds) >= 2 ||
                !string.Equals(process.ProcessName, descriptor.ProcessName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Winshots host descriptor is stale.");
            }

            string? actualPath = process.MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(descriptor.ExecutablePath) &&
                !string.IsNullOrWhiteSpace(actualPath) &&
                !string.Equals(Path.GetFullPath(actualPath), Path.GetFullPath(descriptor.ExecutablePath), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Winshots host descriptor does not match the live process.");
            }
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("Winshots host descriptor is stale.");
        }

        return descriptor;
    }
}
