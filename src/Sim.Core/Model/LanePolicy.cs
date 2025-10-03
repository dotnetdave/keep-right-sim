namespace Sim.Core.Model;

public enum LanePolicy
{
    KeepRight,
    Hogging,
    UndertakeFriendly
}

public sealed record LanePolicyConfig(
    LanePolicy Policy,
    double SafetyDecelThreshold,
    double KeepRightBonus,
    double LeftPenalty,
    double UndertakeBonus);
