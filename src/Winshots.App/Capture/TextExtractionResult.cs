namespace Winshots.App.Capture;

public sealed record TextExtractionResult(
    string Text,
    int NodeCount,
    bool NodeLimitReached,
    bool TextLimitReached,
    bool TimedOut);
