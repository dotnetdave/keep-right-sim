using System;

namespace Sim.Core.Primitives;

/// <summary>
/// Optional helpers for representing fixed-point measurements in millimetres or milliseconds.
/// The simulation primarily operates on <see cref="double"/> but these helpers provide deterministic
/// conversions should fixed-point arithmetic be required in the future.
/// </summary>
public static class MathFixed
{
    private const double MillimetresPerMetre = 1000d;
    private const double MillisecondsPerSecond = 1000d;

    public static int MetresToMillimetres(double metres) => (int)Math.Round(metres * MillimetresPerMetre);

    public static double MillimetresToMetres(int millimetres) => millimetres / MillimetresPerMetre;

    public static int SecondsToMilliseconds(double seconds) => (int)Math.Round(seconds * MillisecondsPerSecond);

    public static double MillisecondsToSeconds(int milliseconds) => milliseconds / MillisecondsPerSecond;

    public static double Clamp(double value, double min, double max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
