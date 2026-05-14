using Sim.Core.Demo;
using Sim.Core.Model;
using Xunit;

namespace Sim.Core.Tests;

public class EducationalReportTests
{
    [Fact]
    public void ComparisonProducesReadableEducationalSummary()
    {
        var network = new HighwayNetwork(LaneCount: 3, LaneWidth: 3.7, Length: 1200, SpeedLimit: 33.33);

        var comparison = EducationalReport.Compare(
            durationSeconds: 90,
            dt: 0.05,
            demandPerHour: 1600,
            seed: 42,
            network: network,
            warmupSeconds: 15);

        Assert.Equal("Keep right except to pass", comparison.KeepRight.Name);
        Assert.Equal("Hogging / undertaking", comparison.Hogging.Name);
        Assert.True(comparison.KeepRight.MeanSpeedKph >= 0);
        Assert.True(comparison.Hogging.MeanSpeedKph >= 0);
        Assert.NotEmpty(comparison.Lessons);

        var markdown = EducationalReport.ToMarkdown(comparison);
        Assert.Contains("# Keep Right Highway Simulation Report", markdown);
        Assert.Contains("Time saved per 1,000 journeys", markdown);
        Assert.Contains("right lane", markdown);
    }
}
