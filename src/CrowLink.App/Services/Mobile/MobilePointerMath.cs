namespace CrowLink.Services.Mobile;

public static class MobilePointerMath
{
    public static (double X, double Y) ScaleMovement(
        double deltaX,
        double deltaY,
        double sensitivity,
        bool acceleration)
    {
        if (!double.IsFinite(deltaX) || !double.IsFinite(deltaY))
        {
            return (0d, 0d);
        }

        var clampedX = Math.Clamp(deltaX, -160d, 160d);
        var clampedY = Math.Clamp(deltaY, -160d, 160d);
        var safeSensitivity = Math.Clamp(sensitivity, 0.5d, 5d);
        if (!acceleration)
        {
            return (clampedX * safeSensitivity, clampedY * safeSensitivity);
        }

        var speed = Math.Sqrt((clampedX * clampedX) + (clampedY * clampedY));
        // Preserve precision for small motions without making them slower than the
        // user's selected sensitivity. Fast swipes can reach 2.5x acceleration.
        var accelerationFactor = 1d + Math.Min(speed / 20d, 1.5d);
        return (
            clampedX * safeSensitivity * accelerationFactor,
            clampedY * safeSensitivity * accelerationFactor);
    }

    public static int ScaleWheel(double delta, double speed)
    {
        if (!double.IsFinite(delta))
        {
            return 0;
        }

        return (int)Math.Clamp(Math.Round(delta * Math.Clamp(speed, 0.5d, 3d)), -1200d, 1200d);
    }
}
