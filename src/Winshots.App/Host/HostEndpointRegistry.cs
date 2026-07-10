using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Winshots.App.Host;

public sealed record HostEndpointDescriptor
{
    public required string PipeName { get; init; }
    public required int ProcessId { get; init; }
    public required string ProcessStartedUtc { get; init; }
    public required string ProcessName { get; init; }
    public required string ExecutablePath { get; init; }
}

public sealed class HostEndpointRegistry : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _descriptorPath;
    private readonly string _mutexName;
    private readonly HostEndpointDescriptor _descriptor;
    private int _disposed;

    public HostEndpointRegistry(string pipeName, string? descriptorPath = null)
    {
        _descriptorPath = Path.GetFullPath(descriptorPath ?? DefaultDescriptorPath);
        _mutexName = MutexName(_descriptorPath);
        using Process process = Process.GetCurrentProcess();
        _descriptor = new HostEndpointDescriptor
        {
            PipeName = pipeName,
            ProcessId = Environment.ProcessId,
            ProcessStartedUtc = process.StartTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ProcessName = process.ProcessName,
            ExecutablePath = Environment.ProcessPath ?? process.MainModule?.FileName ?? string.Empty
        };

        WithMutex(() => WriteAtomic(_descriptorPath, _descriptor));
    }

    public static string DefaultDescriptorPath
    {
        get
        {
            string? overridePath = Environment.GetEnvironmentVariable("WINSHOTS_HOST_DESCRIPTOR");
            return string.IsNullOrWhiteSpace(overridePath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Winshots", "host.json")
                : Path.GetFullPath(overridePath);
        }
    }

    public HostEndpointDescriptor Descriptor => _descriptor;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            WithMutex(() =>
            {
                HostEndpointDescriptor? current = TryRead(_descriptorPath);
                if (current is not null && SameOwner(current, _descriptor))
                {
                    File.Delete(_descriptorPath);
                }
            });
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException or ApplicationException)
        {
            // Host shutdown must not fail because descriptor cleanup is contended.
        }
    }

    internal static HostEndpointDescriptor? TryRead(string descriptorPath)
    {
        try
        {
            return File.Exists(descriptorPath)
                ? JsonSerializer.Deserialize<HostEndpointDescriptor>(File.ReadAllText(descriptorPath))
                : null;
        }
        catch
        {
            return null;
        }
    }

    internal static void WriteAtomic(string descriptorPath, HostEndpointDescriptor descriptor)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(descriptorPath) ?? ".");
        string temporary = $"{descriptorPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(descriptor, JsonOptions));
            File.Move(temporary, descriptorPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch
            {
                // A later registry write can ignore a locked temporary descriptor.
            }
        }
    }

    private static bool SameOwner(HostEndpointDescriptor left, HostEndpointDescriptor right)
    {
        return left.ProcessId == right.ProcessId &&
            string.Equals(left.ProcessStartedUtc, right.ProcessStartedUtc, StringComparison.Ordinal) &&
            string.Equals(left.PipeName, right.PipeName, StringComparison.Ordinal);
    }

    private void WithMutex(Action action)
    {
        using var mutex = new Mutex(false, _mutexName);
        bool acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }
            if (!acquired)
            {
                throw new TimeoutException("Timed out while updating the Winshots host descriptor.");
            }

            action();
        }
        finally
        {
            if (acquired)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    internal static string MutexName(string descriptorPath)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(descriptorPath.ToUpperInvariant()));
        return $"Local\\Winshots.HostDescriptor.{Convert.ToHexString(hash.AsSpan(0, 12))}";
    }
}
