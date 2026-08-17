using System;
using CityStateSim.Core;
using CityStateSim.Locations;

namespace CityStateSim.Events
{
    public sealed class WorldEventInstance
    {
        public string InstanceId { get; }
        public WorldEventDefinition Definition { get; }
        public LocationDefinition Location { get; }
        public string Summary { get; }
        public string[] TargetNpcIds { get; }
        public GameDate Date { get; }
        public GameTime Time { get; }

        public WorldEventInstance(
            WorldEventDefinition definition,
            LocationDefinition location,
            string summary,
            string[] targetNpcIds,
            GameDate date,
            GameTime time)
        {
            InstanceId = Guid.NewGuid().ToString("N");
            Definition = definition;
            Location = location;
            Summary = summary;
            TargetNpcIds = targetNpcIds ?? Array.Empty<string>();
            Date = date;
            Time = time;
        }

        public string BuildMemorySummary()
        {
            string name = Definition != null ? Definition.DisplayName : "World event";
            string locationName = Location != null ? Location.DisplayName : "unknown location";
            string text = string.IsNullOrWhiteSpace(Summary) ? name : Summary;
            return $"{name} at {locationName}: {text}";
        }
    }
}
