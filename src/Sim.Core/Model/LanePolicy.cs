namespace Sim.Core.Model;

public enum LanePolicy
{
    KeepRight,
    Hogging,
    UndertakeFriendly
}

public sealed record LanePolicyConfig(
    LanePolicy Policy = LanePolicy.KeepRight,
    double DeltaAT = 0.3,
    double ReturnRightThreshold = 0.5,
    double RecentChangePenalty = 0.3,
    double StickySeconds = 3.0);
