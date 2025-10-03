using System;

namespace Sim.Core.Sim;

internal static class LaneChangeSafety
{
    // Compute time-to-collision: if relV <= 0 (opening), return +INF
    public static double Ttc(double gapMeters, double relSpeedMps)
        => relSpeedMps <= 0 ? double.PositiveInfinity : gapMeters / relSpeedMps;

    // Gap acceptance in target lane given my speed and neighbors
    public static bool AcceptsGap(
        double myS, double myV, double leadS, double leadV, double follS, double follV,
        double minFrontGapM, double minRearGapM, double minFrontTtc, double minRearTtc)
    {
        // Normalize gaps along S (assume same lane centerline projection)
        double frontGap = Math.Max(0, leadS - myS);
        double rearGap = Math.Max(0, myS - follS);

        // TTC toward lead (I approach them)
        double frontTtc = Ttc(frontGap, Math.Max(0, myV - leadV));
        // TTC for follower toward me (they approach me)
        double rearTtc = Ttc(rearGap, Math.Max(0, follV - myV));

        bool gapOK =
            frontGap >= minFrontGapM &&
            rearGap >= minRearGapM &&
            frontTtc >= minFrontTtc &&
            rearTtc >= minRearTtc;

        return gapOK;
    }
}
