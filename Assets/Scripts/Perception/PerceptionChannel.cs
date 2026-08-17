using System;

namespace CityStateSim.Perception
{
    [Flags]
    public enum PerceptionChannel
    {
        None = 0,
        Visual = 1,
        Audible = 2
    }
}
