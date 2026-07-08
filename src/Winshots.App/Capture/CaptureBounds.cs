namespace Winshots.App.Capture;

public sealed record CaptureBounds(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
}
