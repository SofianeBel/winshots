using System.Globalization;
using Winshots.App.Capture;

namespace Winshots.Tests;

public sealed class AgentWatchServiceTests
{
    private static readonly AgentWatchTarget Target = new() { TitleContains = "Agent Watch" };

    [Fact]
    public async Task WaitForWindowAsync_SucceedsAfterMatchingWindowAppears()
    {
        var scheduler = new FakeScheduler();
        var service = CreateService(scheduler, Missing(), Window());

        AgentWatchResult result = await service.WaitForWindowAsync(
            Target,
            new AgentWatchOptions { TimeoutMs = 500, PollIntervalMs = 100 });

        Assert.Equal("succeeded", result.Outcome);
        Assert.Equal(2, result.FramesObserved);
        Assert.Equal(100, result.DurationMs);
        Assert.Equal(500, result.AppliedBounds.TimeoutMs);
        Assert.Equal(100, result.AppliedBounds.PollIntervalMs);
    }

    [Fact]
    public async Task WaitForWindowAsync_TimesOutAtAppliedBound()
    {
        var scheduler = new FakeScheduler();
        var service = CreateService(scheduler, Missing());

        AgentWatchResult result = await service.WaitForWindowAsync(
            Target,
            new AgentWatchOptions { TimeoutMs = 250, PollIntervalMs = 100 });

        Assert.Equal("timed_out", result.Outcome);
        Assert.Equal(3, result.FramesObserved);
        Assert.Equal(250, result.DurationMs);
    }

    [Fact]
    public async Task WaitForWindowAsync_ReturnsCancelledDiagnostics()
    {
        using var cancellation = new CancellationTokenSource();
        var scheduler = new FakeScheduler { OnDelay = cancellation.Cancel };
        var service = CreateService(scheduler, Missing());

        AgentWatchResult result = await service.WaitForWindowAsync(
            Target,
            new AgentWatchOptions { TimeoutMs = 1_000, PollIntervalMs = 100 },
            cancellation.Token);

        Assert.Equal("cancelled", result.Outcome);
        Assert.Equal(1, result.FramesObserved);
        Assert.Equal(100, result.DurationMs);
        Assert.Contains("cancellation token", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WaitForTextAsync_MatchesFullUiaTextBeyondBoundedPreview()
    {
        string fullText = new string('x', 1_200) + "needle-after-preview";
        var scheduler = new FakeScheduler();
        var service = CreateService(scheduler, Window(
            matchText: fullText,
            textPreview: fullText[..1_000]));

        AgentWatchResult result = await service.WaitForTextAsync(
            Target,
            "needle-after-preview",
            new AgentWatchOptions { TimeoutMs = 500, PollIntervalMs = 100 });

        Assert.Equal("succeeded", result.Outcome);
        Assert.Equal("windows_ui_automation", result.TextSource);
        Assert.DoesNotContain("needle-after-preview", result.LastObservation!.TextPreview, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WaitForDisappearAsync_RequiresPriorPositiveObservation()
    {
        var scheduler = new FakeScheduler();
        var service = CreateService(scheduler, Missing(), Window(), Missing());

        AgentWatchResult result = await service.WaitForDisappearAsync(
            Target,
            options: new AgentWatchOptions { TimeoutMs = 500, PollIntervalMs = 100 });

        Assert.Equal("succeeded", result.Outcome);
        Assert.True(result.TargetObserved);
        Assert.Equal(3, result.FramesObserved);
    }

    [Fact]
    public async Task WaitForDisappearAsync_InitialAbsenceTimesOut()
    {
        var scheduler = new FakeScheduler();
        var service = CreateService(scheduler, Missing());

        AgentWatchResult result = await service.WaitForDisappearAsync(
            Target,
            options: new AgentWatchOptions { TimeoutMs = 300, PollIntervalMs = 100 });

        Assert.Equal("timed_out", result.Outcome);
        Assert.False(result.TargetObserved);
    }

    [Fact]
    public async Task WaitForChangeAsync_SucceedsAtAppliedHashDistance()
    {
        var scheduler = new FakeScheduler();
        var service = CreateService(scheduler, Frame(0), Frame(1), Frame(7));

        AgentWatchResult result = await service.WaitForChangeAsync(
            Target,
            new AgentWatchOptions
            {
                TimeoutMs = 500,
                PollIntervalMs = 100,
                MinChangeHashDistance = 3
            });

        Assert.Equal("succeeded", result.Outcome);
        Assert.Equal(3, result.LastHashDistance);
        Assert.Equal(2, result.Comparisons);
        Assert.Equal(3, result.AppliedBounds.MinChangeHashDistance);
    }

    [Fact]
    public async Task WaitForStableAsync_SucceedsOnlyAfterAppliedStableDuration()
    {
        var scheduler = new FakeScheduler();
        var service = CreateService(scheduler, Frame(4));

        AgentWatchResult result = await service.WaitForStableAsync(
            Target,
            new AgentWatchOptions
            {
                TimeoutMs = 500,
                PollIntervalMs = 100,
                StableDurationMs = 200,
                MaxStableHashDistance = 0
            });

        Assert.Equal("succeeded", result.Outcome);
        Assert.Equal(200, result.StableForMs);
        Assert.Equal(200, result.DurationMs);
        Assert.Equal(200, result.AppliedBounds.StableDurationMs);
    }

    [Fact]
    public async Task WaitForStableAsync_DoesNotAcceptCumulativeDriftFromBaseline()
    {
        var scheduler = new FakeScheduler();
        var service = CreateService(scheduler, Frame(0), Frame(1), Frame(3), Frame(7), Frame(15));

        AgentWatchResult result = await service.WaitForStableAsync(
            Target,
            new AgentWatchOptions
            {
                TimeoutMs = 500,
                PollIntervalMs = 100,
                StableDurationMs = 300,
                MaxStableHashDistance = 1
            });

        Assert.Equal("timed_out", result.Outcome);
        Assert.Equal(0, result.StableForMs);
        Assert.Equal(500, result.DurationMs);
    }

    private static AgentWatchService CreateService(
        FakeScheduler scheduler,
        params AgentWatchObservation[] observations)
    {
        return new AgentWatchService(new FakeObservationSource(observations), scheduler);
    }

    private static AgentWatchObservation Missing()
    {
        return new AgentWatchObservation
        {
            TimestampUtc = "2026-07-10T00:00:00.0000000Z",
            WindowFound = false
        };
    }

    private static AgentWatchObservation Window(string? matchText = null, string? textPreview = null)
    {
        return new AgentWatchObservation
        {
            TimestampUtc = "2026-07-10T00:00:00.0000000Z",
            WindowFound = true,
            WindowHandle = "0x1234",
            WindowTitle = "Agent Watch target",
            ProcessName = "test",
            ProcessId = 42,
            TextSource = matchText is null ? null : "windows_ui_automation",
            TextStatus = matchText is null ? null : "succeeded",
            TextPreview = textPreview ?? matchText,
            MatchText = matchText
        };
    }

    private static AgentWatchObservation Frame(ulong hash)
    {
        return Window() with
        {
            PerceptualHash = hash.ToString("X16", CultureInfo.InvariantCulture),
            DirectoryPath = $"C:\\captures\\{hash}",
            ScreenshotPath = $"C:\\captures\\{hash}\\screenshot.png",
            TextPath = $"C:\\captures\\{hash}\\context.txt",
            MetadataPath = $"C:\\captures\\{hash}\\metadata.json"
        };
    }

    private sealed class FakeObservationSource(IReadOnlyList<AgentWatchObservation> observations)
        : AgentWatchService.IAgentWatchObservationSource
    {
        private int _index;

        public Task<AgentWatchObservation> ObserveAsync(
            AgentWatchTarget target,
            bool includeText,
            bool includeImage,
            TimeSpan remaining,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AgentWatchObservation observation = observations.Count == 0
                ? Missing()
                : observations[Math.Min(_index, observations.Count - 1)];
            _index++;
            return Task.FromResult(observation);
        }
    }

    private sealed class FakeScheduler : AgentWatchService.IAgentWatchScheduler
    {
        public DateTimeOffset UtcNow { get; private set; } = new(2026, 7, 10, 0, 0, 0, TimeSpan.Zero);
        public Action? OnDelay { get; init; }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            UtcNow += delay;
            OnDelay?.Invoke();
            return cancellationToken.IsCancellationRequested
                ? Task.FromCanceled(cancellationToken)
                : Task.CompletedTask;
        }
    }
}
