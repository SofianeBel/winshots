using System.Globalization;
using System.Diagnostics;
using System.Text.Json;

namespace Winshots.App.Capture;

public sealed class InstantReplayBufferStore
{
    private const int MaxRecentEvents = 200;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions LineJsonOptions = new();

    private readonly object _gate = new();
    private readonly object _saveGate = new();
    private readonly InstantReplayOptions _options;
    private InstantReplayManifest _manifest;

    public InstantReplayBufferStore(InstantReplayOptions options)
    {
        _options = options.Normalized();
        Directory.CreateDirectory(_options.BufferRootPath);
        Directory.CreateDirectory(FramesRootPath);
        _manifest = LoadManifest() ?? NewManifest();
        Recover();
    }

    public string RootPath => _options.BufferRootPath;
    public string FramesRootPath => Path.Combine(RootPath, "frames");

    public string CreateCandidateDirectory(DateTimeOffset timestamp)
    {
        string id = $"{timestamp.UtcDateTime:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}";
        string directory = Path.Combine(FramesRootPath, id);
        Directory.CreateDirectory(directory);
        return directory;
    }

    public InstantReplayRetentionDecision DecideRetention(WindowSnapshot window, ulong hash, DateTimeOffset timestamp)
    {
        lock (_gate)
        {
            InstantReplayFrame? previous = _manifest.Frames.LastOrDefault();
            if (previous is null)
            {
                return new InstantReplayRetentionDecision(true, "initial", hash);
            }

            bool contextChanged = !string.Equals(previous.WindowTitle, window.Title, StringComparison.Ordinal) ||
                !string.Equals(previous.ProcessName, window.ProcessName, StringComparison.OrdinalIgnoreCase) ||
                previous.ProcessId != window.ProcessId;
            if (contextChanged)
            {
                return new InstantReplayRetentionDecision(true, "window-change", hash);
            }

            ulong previousHash = ulong.Parse(previous.PerceptualHash, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            int distance = PerceptualHash.Distance(previousHash, hash);
            if (distance >= _options.HashDistanceThreshold)
            {
                return new InstantReplayRetentionDecision(true, "visual-change", hash);
            }

            DateTimeOffset previousTimestamp = DateTimeOffset.Parse(previous.TimestampUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if (timestamp - previousTimestamp >= TimeSpan.FromSeconds(_options.StableKeyframeSeconds))
            {
                return new InstantReplayRetentionDecision(true, "stable-keyframe", hash);
            }

            AddEventLocked(timestamp, "duplicate", $"dHash distance {distance} below threshold {_options.HashDistanceThreshold}.");
            _manifest = _manifest with
            {
                DuplicateFrameCount = _manifest.DuplicateFrameCount + 1,
                IgnoredFrameCount = _manifest.IgnoredFrameCount + 1
            };
            WriteManifestLocked();
            return new InstantReplayRetentionDecision(false, "duplicate", hash);
        }
    }

    public void AddRetainedFrame(CaptureResult capture, ulong hash, string retentionReason)
    {
        lock (_gate)
        {
            var frame = new InstantReplayFrame
            {
                Id = capture.Metadata.Id,
                TimestampUtc = capture.Metadata.TimestampUtc,
                TimestampLocal = capture.Metadata.TimestampLocal,
                WindowTitle = capture.Metadata.WindowTitle,
                ProcessName = capture.Metadata.ProcessName,
                ProcessId = capture.Metadata.ProcessId,
                DirectoryPath = capture.DirectoryPath,
                ScreenshotPath = capture.ScreenshotPath,
                TextPath = capture.TextPath,
                MetadataPath = capture.MetadataPath,
                PerceptualHash = hash.ToString("X16", CultureInfo.InvariantCulture),
                RetentionReason = retentionReason,
                Bytes = 0
            };

            string framePath = Path.Combine(capture.DirectoryPath, "frame.json");
            long bytes = 0;
            bool sizeStable = false;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                WriteJsonAtomic(framePath, frame);
                long measuredBytes = ExistingLength(capture.ScreenshotPath) +
                    ExistingLength(capture.TextPath) +
                    ExistingLength(capture.MetadataPath) +
                    ExistingLength(framePath);
                if (frame.Bytes == measuredBytes)
                {
                    bytes = measuredBytes;
                    sizeStable = true;
                    break;
                }

                frame = frame with { Bytes = measuredBytes };
            }

            if (!sizeStable || bytes <= 0 || bytes > _options.MaxBytes)
            {
                DeleteDirectory(capture.DirectoryPath);
                AddEventLocked(DateTimeOffset.Now, "ignored", "A retained frame exceeded the replay disk limit or had no valid files.");
                _manifest = _manifest with { IgnoredFrameCount = _manifest.IgnoredFrameCount + 1 };
                WriteManifestLocked();
                return;
            }

            var frames = _manifest.Frames.Append(frame).OrderBy(static item => item.TimestampUtc, StringComparer.Ordinal).ToList();
            _manifest = _manifest with { Frames = frames };

            if (retentionReason is "window-change" or "visual-change")
            {
                AddEventLocked(DateTimeOffset.Now, retentionReason, $"Retained {capture.Metadata.ProcessName}: {capture.Metadata.WindowTitle}");
                _manifest = _manifest with { ChangeEventCount = _manifest.ChangeEventCount + 1 };
            }

            PruneLocked(DateTimeOffset.Now);
            WriteManifestLocked();
        }
    }

    public void RecordFailure(string detail)
    {
        lock (_gate)
        {
            AddEventLocked(DateTimeOffset.Now, "capture-failed", detail);
            _manifest = _manifest with { FailedFrameCount = _manifest.FailedFrameCount + 1 };
            WriteManifestLocked();
        }
    }

    public void RecordBusySkip()
    {
        lock (_gate)
        {
            _manifest = _manifest with { BusySkipCount = _manifest.BusySkipCount + 1 };
            WriteManifestLocked();
        }
    }

    public void SetRunning(bool running)
    {
        lock (_gate)
        {
            _manifest = _manifest with { Status = running ? "running" : "stopped" };
            WriteManifestLocked();
        }
    }

    public InstantReplayStatus GetStatus(bool running)
    {
        lock (_gate)
        {
            if (PruneLocked(DateTimeOffset.Now))
            {
                WriteManifestLocked();
            }
            double bufferedSeconds = 0;
            if (_manifest.Frames.Count > 1 &&
                DateTimeOffset.TryParse(_manifest.Frames[0].TimestampUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset first) &&
                DateTimeOffset.TryParse(_manifest.Frames[^1].TimestampUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset last))
            {
                bufferedSeconds = Math.Max(0, (last - first).TotalSeconds);
            }

            return new InstantReplayStatus
            {
                Running = running,
                State = running ? "running" : "stopped",
                LookbackSeconds = _options.LookbackSeconds,
                IntervalMs = _options.IntervalMs,
                FrameCount = _manifest.Frames.Count,
                BufferedSeconds = Math.Round(bufferedSeconds, 1),
                BufferBytes = _manifest.BufferBytes,
                MaxBytes = _options.MaxBytes,
                MaxFrames = _options.MaxFrames,
                DuplicateFrameCount = _manifest.DuplicateFrameCount,
                FailedFrameCount = _manifest.FailedFrameCount,
                BusySkipCount = _manifest.BusySkipCount,
                IgnoredFrameCount = _manifest.IgnoredFrameCount,
                ChangeEventCount = _manifest.ChangeEventCount
            };
        }
    }

    public VisualSessionManifest SaveReplay(int? lookbackSeconds = null)
    {
        lock (_saveGate)
        {
            return SaveReplaySerialized(lookbackSeconds);
        }
    }

    private VisualSessionManifest SaveReplaySerialized(int? lookbackSeconds)
    {
        InstantReplayFrame[] snapshot;
        InstantReplayManifest manifestSnapshot;
        string partialDirectory = string.Empty;
        string finalDirectory;
        DateTimeOffset savedAt = DateTimeOffset.Now;
        int requestedLookback = Math.Clamp(lookbackSeconds ?? _options.LookbackSeconds, 5, _options.LookbackSeconds);
        string sessionId;

        try
        {
            lock (_gate)
            {
                CleanupOrphanedPartialSessionsLocked();
                PruneLocked(savedAt);
                DateTimeOffset cutoff = savedAt - TimeSpan.FromSeconds(requestedLookback);
                snapshot = _manifest.Frames
                    .Where(frame => DateTimeOffset.Parse(frame.TimestampUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) >= cutoff)
                    .ToArray();
                if (snapshot.Length == 0)
                {
                    throw new InvalidOperationException("Instant Replay has no retained frames to save.");
                }

                manifestSnapshot = _manifest;

                finalDirectory = UniqueSessionDirectory(savedAt);
                sessionId = Path.GetFileName(finalDirectory);
                partialDirectory = $"{finalDirectory}.{Environment.ProcessId}.{Guid.NewGuid():N}.partial";
                Directory.CreateDirectory(Path.Combine(partialDirectory, "frames"));
                Directory.CreateDirectory(Path.Combine(partialDirectory, "contexts"));
                WriteJsonAtomic(Path.Combine(partialDirectory, ".instant-replay-owner.json"), CurrentPartialOwner());

                for (int index = 0; index < snapshot.Length; index++)
                {
                    string frameId = (index + 1).ToString("000000", CultureInfo.InvariantCulture);
                    File.Copy(snapshot[index].ScreenshotPath, Path.Combine(partialDirectory, "frames", $"{frameId}.png"), overwrite: false);
                    File.Copy(snapshot[index].TextPath, Path.Combine(partialDirectory, "contexts", $"{frameId}.txt"), overwrite: false);
                    File.Copy(snapshot[index].MetadataPath, Path.Combine(partialDirectory, "contexts", $"{frameId}.metadata.json"), overwrite: false);
                }
            }

            var sessionFrames = new List<VisualSessionFrame>(snapshot.Length);
            var copiedContexts = new Dictionary<int, string>();
            for (int index = 0; index < snapshot.Length; index++)
            {
                int number = index + 1;
                string frameId = number.ToString("000000", CultureInfo.InvariantCulture);
                string finalScreenshot = Path.Combine(finalDirectory, "frames", $"{frameId}.png");
                string finalText = Path.Combine(finalDirectory, "contexts", $"{frameId}.txt");
                string finalMetadata = Path.Combine(finalDirectory, "contexts", $"{frameId}.metadata.json");
                string partialText = Path.Combine(partialDirectory, "contexts", $"{frameId}.txt");
                string partialMetadata = Path.Combine(partialDirectory, "contexts", $"{frameId}.metadata.json");

                CaptureMetadata metadata = JsonSerializer.Deserialize<CaptureMetadata>(File.ReadAllText(partialMetadata))
                    ?? throw new InvalidDataException("Replay frame metadata could not be read.");
                metadata = metadata with
                {
                    Id = $"{sessionId}-{frameId}",
                    ScreenshotPath = finalScreenshot,
                    TextPath = finalText
                };
                File.WriteAllText(partialMetadata, JsonSerializer.Serialize(metadata, JsonOptions));

                string context = File.ReadAllText(partialText)
                    .Replace(snapshot[index].ScreenshotPath, finalScreenshot, StringComparison.OrdinalIgnoreCase)
                    .Replace(snapshot[index].TextPath, finalText, StringComparison.OrdinalIgnoreCase)
                    .Replace(snapshot[index].MetadataPath, finalMetadata, StringComparison.OrdinalIgnoreCase);
                File.WriteAllText(partialText, context);
                copiedContexts[number] = context;

                sessionFrames.Add(new VisualSessionFrame
                {
                    Number = number,
                    TimestampUtc = snapshot[index].TimestampUtc,
                    TimestampLocal = snapshot[index].TimestampLocal,
                    Captured = true,
                    WindowTitle = snapshot[index].WindowTitle,
                    ProcessName = snapshot[index].ProcessName,
                    ScreenshotPath = finalScreenshot,
                    TextPath = finalText,
                    MetadataPath = finalMetadata,
                    Bounds = metadata.Bounds,
                    Metrics = metadata.Metrics,
                    Diagnostics = metadata.Diagnostics,
                    PerceptualHash = snapshot[index].PerceptualHash,
                    RetentionReason = snapshot[index].RetentionReason
                });
            }

            DateTimeOffset firstTimestamp = DateTimeOffset.Parse(snapshot[0].TimestampUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            DateTimeOffset lastTimestamp = DateTimeOffset.Parse(snapshot[^1].TimestampUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            var manifest = new VisualSessionManifest
            {
                Id = sessionId,
                Status = "completed",
                StartedUtc = firstTimestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                StartedLocal = firstTimestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
                CompletedUtc = savedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                CompletedLocal = savedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
                DirectoryPath = finalDirectory,
                FramesDirectoryPath = Path.Combine(finalDirectory, "frames"),
                ContextsDirectoryPath = Path.Combine(finalDirectory, "contexts"),
                FramesIndexPath = Path.Combine(finalDirectory, "frames.jsonl"),
                ContextPath = Path.Combine(finalDirectory, "context.md"),
                ManifestPath = Path.Combine(finalDirectory, "session.json"),
                IntervalMs = _options.IntervalMs,
                MaxDurationSeconds = requestedLookback,
                VideoRequested = false,
                FrameCount = sessionFrames.Count,
                CapturedFrameCount = sessionFrames.Count,
                FailedFrameCount = 0,
                TotalMs = Math.Max(0, (long)(lastTimestamp - firstTimestamp).TotalMilliseconds),
                SessionType = "instant-replay",
                Source = "Instant Replay",
                LookbackSeconds = requestedLookback,
                WindowStartUtc = snapshot[0].TimestampUtc,
                WindowEndUtc = snapshot[^1].TimestampUtc,
                DuplicateFrameCount = manifestSnapshot.DuplicateFrameCount,
                IgnoredFrameCount = manifestSnapshot.IgnoredFrameCount,
                ChangeEventCount = manifestSnapshot.ChangeEventCount
            };

            File.WriteAllLines(
                Path.Combine(partialDirectory, "frames.jsonl"),
                sessionFrames.Select(frame => JsonSerializer.Serialize(frame, LineJsonOptions)));
            File.WriteAllText(
                Path.Combine(partialDirectory, "context.md"),
                VisualSessionStorage.BuildContextMarkdown(manifest, sessionFrames, frame => copiedContexts[frame.Number]));
            File.Delete(Path.Combine(partialDirectory, ".instant-replay-owner.json"));
            WriteJsonAtomic(Path.Combine(partialDirectory, "session.json"), manifest);
            Directory.Move(partialDirectory, finalDirectory);
            return manifest;
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(partialDirectory))
            {
                DeleteDirectory(partialDirectory);
            }

            lock (_gate)
            {
                RemoveMissingFramesLocked();
            }

            throw;
        }
    }

    private void RemoveMissingFramesLocked()
    {
        InstantReplayFrame[] missing = _manifest.Frames
            .Where(static frame =>
                !File.Exists(frame.ScreenshotPath) ||
                !File.Exists(frame.TextPath) ||
                !File.Exists(frame.MetadataPath))
            .ToArray();
        if (missing.Length == 0)
        {
            return;
        }

        var missingIds = missing.Select(static frame => frame.Id).ToHashSet(StringComparer.Ordinal);
        foreach (InstantReplayFrame frame in missing)
        {
            DeleteDirectory(frame.DirectoryPath);
            AddEventLocked(DateTimeOffset.Now, "source-missing", $"Removed replay frame {frame.Id} after a source artifact disappeared.");
        }

        InstantReplayFrame[] retained = _manifest.Frames.Where(frame => !missingIds.Contains(frame.Id)).ToArray();
        _manifest = _manifest with
        {
            Frames = retained,
            BufferBytes = retained.Sum(static frame => frame.Bytes),
            FailedFrameCount = _manifest.FailedFrameCount + missing.Length,
            IgnoredFrameCount = _manifest.IgnoredFrameCount + missing.Length
        };
        WriteManifestLocked();
    }

    private void Recover()
    {
        lock (_gate)
        {
            CleanupOrphanedPartialSessionsLocked();
            foreach (string temporary in Directory.EnumerateFiles(RootPath, "*.tmp", SearchOption.AllDirectories))
            {
                TryDeleteFile(temporary);
            }

            var recovered = new List<InstantReplayFrame>();
            foreach (string directory in Directory.EnumerateDirectories(FramesRootPath))
            {
                string framePath = Path.Combine(directory, "frame.json");
                InstantReplayFrame? frame = TryRead<InstantReplayFrame>(framePath);
                if (frame is null || !IsValidRecoveredFrame(frame, directory))
                {
                    DeleteDirectory(directory);
                    continue;
                }

                recovered.Add(frame with
                {
                    Bytes = ExistingLength(frame.ScreenshotPath) +
                        ExistingLength(frame.TextPath) +
                        ExistingLength(frame.MetadataPath) +
                        ExistingLength(framePath)
                });
            }

            _manifest = _manifest with
            {
                Status = "stopped",
                LookbackSeconds = _options.LookbackSeconds,
                IntervalMs = _options.IntervalMs,
                MaxFrames = _options.MaxFrames,
                MaxBytes = _options.MaxBytes,
                Frames = recovered.OrderBy(static frame => frame.TimestampUtc, StringComparer.Ordinal).ToArray()
            };
            PruneLocked(DateTimeOffset.Now);
            WriteManifestLocked();
        }
    }

    private bool PruneLocked(DateTimeOffset now)
    {
        DateTimeOffset cutoff = now - TimeSpan.FromSeconds(_options.LookbackSeconds);
        var frames = _manifest.Frames.OrderBy(static frame => frame.TimestampUtc, StringComparer.Ordinal).ToList();
        long bytes = frames.Sum(static frame => frame.Bytes);
        bool changed = bytes != _manifest.BufferBytes ||
            !_manifest.Frames.Select(static frame => frame.Id).SequenceEqual(frames.Select(static frame => frame.Id), StringComparer.Ordinal);

        while (frames.Count > 0)
        {
            bool validTimestamp = DateTimeOffset.TryParse(
                frames[0].TimestampUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset firstTimestamp);
            if (validTimestamp && firstTimestamp >= cutoff && frames.Count <= _options.MaxFrames && bytes <= _options.MaxBytes)
            {
                break;
            }

            InstantReplayFrame removed = frames[0];
            frames.RemoveAt(0);
            bytes -= removed.Bytes;
            DeleteDirectory(removed.DirectoryPath);
            changed = true;
        }

        _manifest = _manifest with { Frames = frames, BufferBytes = Math.Max(0, bytes) };
        return changed;
    }

    private void AddEventLocked(DateTimeOffset timestamp, string type, string detail)
    {
        var events = _manifest.Events.Append(new InstantReplayEvent
        {
            TimestampUtc = timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            Type = type,
            Detail = detail
        }).TakeLast(MaxRecentEvents).ToArray();
        _manifest = _manifest with { Events = events };
    }

    private InstantReplayManifest NewManifest()
    {
        return new InstantReplayManifest
        {
            LookbackSeconds = _options.LookbackSeconds,
            IntervalMs = _options.IntervalMs,
            MaxFrames = _options.MaxFrames,
            MaxBytes = _options.MaxBytes
        };
    }

    private InstantReplayManifest? LoadManifest()
    {
        return TryRead<InstantReplayManifest>(Path.Combine(RootPath, "buffer.json"));
    }

    private void WriteManifestLocked()
    {
        WriteJsonAtomic(Path.Combine(RootPath, "buffer.json"), _manifest);
    }

    private string UniqueSessionDirectory(DateTimeOffset timestamp)
    {
        Directory.CreateDirectory(_options.SessionRootPath);
        string stamp = timestamp.LocalDateTime.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        return Path.Combine(_options.SessionRootPath, $"{stamp}-instant-replay-{Guid.NewGuid():N}"[..44]);
    }

    private void CleanupOrphanedPartialSessionsLocked()
    {
        if (!Directory.Exists(_options.SessionRootPath))
        {
            return;
        }

        foreach (string directory in Directory.EnumerateDirectories(_options.SessionRootPath, "*-instant-replay*.partial", SearchOption.TopDirectoryOnly))
        {
            string ownerPath = Path.Combine(directory, ".instant-replay-owner.json");
            InstantReplayPartialOwner? owner = TryRead<InstantReplayPartialOwner>(ownerPath);
            if (owner is not null)
            {
                if (!IsOwnerAlive(owner))
                {
                    DeleteDirectory(directory);
                }

                continue;
            }

            DateTime lastWriteUtc;
            try
            {
                lastWriteUtc = Directory.GetLastWriteTimeUtc(directory);
            }
            catch
            {
                continue;
            }

            if (DateTime.UtcNow - lastWriteUtc >= TimeSpan.FromHours(1))
            {
                DeleteDirectory(directory);
            }
        }
    }

    private bool IsValidRecoveredFrame(InstantReplayFrame frame, string directory)
    {
        if (!DateTimeOffset.TryParse(frame.TimestampUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _) ||
            !ulong.TryParse(frame.PerceptualHash, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
        {
            return false;
        }

        string normalizedDirectory = Path.GetFullPath(directory);
        return IsUnderRoot(FramesRootPath, normalizedDirectory) &&
            string.Equals(Path.GetFullPath(frame.DirectoryPath), normalizedDirectory, StringComparison.OrdinalIgnoreCase) &&
            IsUnderRoot(normalizedDirectory, frame.ScreenshotPath) &&
            IsUnderRoot(normalizedDirectory, frame.TextPath) &&
            IsUnderRoot(normalizedDirectory, frame.MetadataPath) &&
            File.Exists(frame.ScreenshotPath) &&
            File.Exists(frame.TextPath) &&
            File.Exists(frame.MetadataPath);
    }

    private static bool IsUnderRoot(string root, string candidate)
    {
        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string normalizedCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static InstantReplayPartialOwner CurrentPartialOwner()
    {
        using Process process = Process.GetCurrentProcess();
        return new InstantReplayPartialOwner
        {
            ProcessId = Environment.ProcessId,
            ProcessStartedUtc = process.StartTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            SaveStartedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };
    }

    private static bool IsOwnerAlive(InstantReplayPartialOwner owner)
    {
        try
        {
            using Process process = Process.GetProcessById(owner.ProcessId);
            if (process.HasExited ||
                !DateTime.TryParse(owner.ProcessStartedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime expectedStart))
            {
                return false;
            }

            return Math.Abs((process.StartTime.ToUniversalTime() - expectedStart.ToUniversalTime()).TotalSeconds) < 2;
        }
        catch
        {
            return false;
        }
    }

    private static T? TryRead<T>(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : default;
        }
        catch
        {
            return default;
        }
    }

    private static void WriteJsonAtomic<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temporary, path, overwrite: true);
    }

    private static long ExistingLength(string path)
    {
        return File.Exists(path) ? new FileInfo(path).Length : 0;
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // A later recovery pass can remove an artifact still held by another reader.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // A later recovery pass can remove a stale temporary file.
        }
    }

    private sealed record InstantReplayPartialOwner
    {
        public required int ProcessId { get; init; }
        public required string ProcessStartedUtc { get; init; }
        public required string SaveStartedUtc { get; init; }
    }
}
