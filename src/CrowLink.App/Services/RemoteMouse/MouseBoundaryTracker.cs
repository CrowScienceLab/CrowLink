namespace CrowLink.Services.RemoteMouse;

public sealed class MouseBoundaryTracker
{
    private const double RemoteTravelPixels = 1200d;
    private readonly MouseTransitionEdge _edge;

    public MouseBoundaryTracker(MouseTransitionEdge edge) => _edge = edge;

    public bool IsRemote { get; private set; }
    public double X { get; private set; }
    public double Y { get; private set; }

    public bool TryEnter(int x, int y, int left, int top, int width, int height, bool wasAtEdge)
    {
        if (IsRemote || width <= 1 || height <= 1 || wasAtEdge)
        {
            return false;
        }

        var atEdge = _edge == MouseTransitionEdge.Right
            ? x >= left + width - 1
            : x <= left;
        if (!atEdge)
        {
            return false;
        }

        IsRemote = true;
        X = _edge == MouseTransitionEdge.Right ? 0d : 1d;
        Y = Math.Clamp((double)(y - top) / Math.Max(1, height - 1), 0d, 1d);
        return true;
    }

    public bool ApplyDelta(int deltaX, int deltaY, int remoteHeight)
    {
        if (!IsRemote)
        {
            return false;
        }

        X += deltaX / RemoteTravelPixels;
        Y += (double)deltaY / Math.Max(1, remoteHeight);
        Y = Math.Clamp(Y, 0d, 1d);
        if (X < 0d || X > 1d)
        {
            IsRemote = false;
            return true;
        }

        X = Math.Clamp(X, 0d, 1d);
        return false;
    }

    public void Exit() => IsRemote = false;
}
