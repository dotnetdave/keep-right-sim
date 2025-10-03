using Sim.Core.Metrics;
using Xunit;

namespace Sim.Core.Tests;

public class StatsTests
{
    [Fact]
    public void WelfordMeanAndVarianceAreAccurate()
    {
        var sensors = new Sensors(2);
        sensors.SampleSpeedsAndOccupancy(new[] { (lane: 0, speed: 10.0), (0, 20.0), (1, 30.0) }, 1.0);
        sensors.RegisterEntry(1, 0);
        sensors.RegisterExit(1, 10);
        var stats = sensors.Capture(10);

        Assert.Equal(15.0, stats.MeanLaneSpeeds[0], 1);
        Assert.Equal(30.0, stats.MeanLaneSpeeds[1], 1);
        Assert.True(stats.LaneSpeedStdDev[0] > 0);
        Assert.InRange(stats.LaneOccupancyShare[0], 0.0, 1.0);
        Assert.True(stats.TravelTimeP50 >= 10);
        Assert.True(stats.ThroughputPerHour > 0);
    }
}
