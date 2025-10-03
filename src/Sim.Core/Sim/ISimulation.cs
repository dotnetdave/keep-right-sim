using Sim.Core.DTO;

namespace Sim.Core.Sim;

public interface ISimulation
{
    double Time { get; }
    long Version { get; }

    void Step(double dt);

    SimSnapshot GetSnapshot();

    SimDelta? GetDeltaSince(long version);

    void Apply(Command command);
}
