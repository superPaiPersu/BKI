namespace CityStateSim.Perception
{
    public sealed class PerceptionObservation
    {
        public PerceptionObservation(
            string entityId,
            string displayName,
            string entityType,
            float distance,
            PerceptionChannel channels,
            string description)
        {
            EntityId = entityId;
            DisplayName = displayName;
            EntityType = entityType;
            Distance = distance;
            Channels = channels;
            Description = description;
        }

        public string EntityId { get; }
        public string DisplayName { get; }
        public string EntityType { get; }
        public float Distance { get; }
        public PerceptionChannel Channels { get; }
        public string Description { get; }
        public string Signature => $"{EntityId}|{Channels}|{Description}";
    }
}
