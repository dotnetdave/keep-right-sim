using System;
using Sim.Core.Model;

namespace Sim.Core.Sim;

internal static class Dynamics
{
    public static double ComputeIdmAcceleration(VehicleAgent agent, double speed, double speedLimit, double? netDistance, double relativeSpeed)
    {
        var desiredSpeed = Math.Min(speedLimit * agent.Driver.DesiredSpeedFactor, agent.Vehicle.MaxSpeed);
        var vmax = Math.Max(desiredSpeed, 1e-3);
        const double delta = 4.0;
        var accelMax = agent.Vehicle.MaxAccel;
        var decelComfort = agent.Vehicle.ComfortDecel;

        if (netDistance is null)
        {
            var term = Math.Pow(speed / vmax, delta);
            var accelFree = accelMax * (1 - term);
            return Math.Clamp(accelFree, -decelComfort, accelMax);
        }

        var gap = Math.Max(netDistance.Value, 1.0);
        var desiredGap = agent.Vehicle.Length + 2.0 + speed * agent.Driver.HeadwayTime;
        desiredGap += speed * relativeSpeed / (2 * Math.Sqrt(accelMax * decelComfort + 1e-3));
        desiredGap = Math.Max(desiredGap, 0.5);
        var brakingTerm = Math.Pow(desiredGap / gap, 2);
        var accel = accelMax * (1 - Math.Pow(speed / vmax, delta) - brakingTerm);
        return Math.Clamp(accel, -decelComfort, accelMax);
    }

    public static double ComputeMobilIncentive(double myAccelerationGain, double followerTargetLoss, double followerCurrentLoss, double politeness)
    {
        return myAccelerationGain - politeness * (followerTargetLoss + followerCurrentLoss);
    }
}
