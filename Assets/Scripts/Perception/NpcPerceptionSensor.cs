using System;
using System.Collections.Generic;
using System.Text;
using CityStateSim.Movement;
using UnityEngine;

namespace CityStateSim.Perception
{
    public sealed class NpcPerceptionSensor : MonoBehaviour
    {
        [SerializeField, Min(1)] private int maxObservations = 8;
        [SerializeField, Min(0.1f)] private float refreshIntervalSeconds = 0.75f;
        [SerializeField] private LayerMask lineOfSightBlockers;
        [SerializeField] private bool useLineOfSight;
        [SerializeField] private bool detectSignificantChanges = true;
        [SerializeField] private bool suppressInitialChangeEvent = true;
        [SerializeField] private bool raiseOnNewlyPerceivedEntities;
        [SerializeField] private bool raiseOnLostPerception;
        [SerializeField, Min(1f)] private float minSecondsBetweenChangeEvents = 20f;

        [Header("Player")]
        [SerializeField] private bool includePlayerObservation = true;
        [SerializeField] private bool playerSeenIsSignificantChange;
        [SerializeField] private string playerEntityId = "player";
        [SerializeField] private string playerDisplayName = "Player";
        [SerializeField, Min(0f)] private float playerVisualRange = 6f;
        [SerializeField, Min(0f)] private float playerAudibleRange = 4f;
        [SerializeField] private bool playerRequiresLineOfSight = true;
        [SerializeField, TextArea] private string playerVisibleDescription = "the player is visible nearby";
        [SerializeField, TextArea] private string playerAudibleDescription = "the player can be heard nearby";

        [SerializeField, TextArea(3, 10)] private string lastObservationSummary;
        [SerializeField, TextArea(2, 8)] private string lastChangeSummary;

        private readonly List<PerceptionObservation> observations = new List<PerceptionObservation>();
        private readonly Dictionary<string, string> signaturesByEntityId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> visibleEntityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> previousVisibleEntityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private float nextRefreshTime;
        private float lastChangeEventTime = -999f;
        private bool hasBaseline;

        public string LastObservationSummary => lastObservationSummary;
        public string LastChangeSummary => lastChangeSummary;
        public IReadOnlyList<PerceptionObservation> Observations => observations;

        public event Action<NpcPerceptionSensor> ObservationsRefreshed;
        public event Action<NpcPerceptionSensor, string> SignificantChangeDetected;

        private void Update()
        {
            if (Time.time < nextRefreshTime)
            {
                return;
            }

            nextRefreshTime = Time.time + refreshIntervalSeconds;
            Refresh();
        }

        public string BuildObservationSummary()
        {
            Refresh(false);
            return lastObservationSummary;
        }

        public bool CanCurrentlyPerceive(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId))
            {
                return false;
            }

            Refresh(false);
            return visibleEntityIds.Contains(entityId.Trim());
        }

        public void Refresh(bool raiseEvents = true)
        {
            observations.Clear();
            previousVisibleEntityIds.Clear();
            foreach (string id in visibleEntityIds)
            {
                previousVisibleEntityIds.Add(id);
            }

            visibleEntityIds.Clear();

            TryAddPlayerObservation();

            PerceivableEntity[] entities = FindObjectsByType<PerceivableEntity>(FindObjectsSortMode.None);
            for (int i = 0; i < entities.Length; i++)
            {
                PerceivableEntity entity = entities[i];
                if (entity == null || IsPlayerEntity(entity) || !entity.TryBuildObservation(transform, out PerceptionObservation observation))
                {
                    continue;
                }

                if (useLineOfSight && entity.RequireLineOfSight && !HasLineOfSight(entity.transform.position))
                {
                    continue;
                }

                AddObservation(observation);
            }

            observations.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            lastObservationSummary = BuildSummary();

            if (raiseEvents)
            {
                ObservationsRefreshed?.Invoke(this);
            }

            if (raiseEvents && detectSignificantChanges)
            {
                DetectChanges();
            }
        }

        private void DetectChanges()
        {
            if (!hasBaseline)
            {
                UpdateKnownSignatures();
                hasBaseline = true;
                if (suppressInitialChangeEvent)
                {
                    lastChangeSummary = string.Empty;
                    return;
                }
            }

            if (Time.time - lastChangeEventTime < minSecondsBetweenChangeEvents)
            {
                UpdateKnownSignatures();
                return;
            }

            string summary = BuildChangeSummary();
            UpdateKnownSignatures();
            if (string.IsNullOrWhiteSpace(summary))
            {
                return;
            }

            lastChangeSummary = summary;
            lastChangeEventTime = Time.time;
            SignificantChangeDetected?.Invoke(this, summary);
        }

        private string BuildChangeSummary()
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < observations.Count; i++)
            {
                PerceptionObservation observation = observations[i];
                if (IsPlayerObservation(observation) && !playerSeenIsSignificantChange)
                {
                    continue;
                }

                if (!previousVisibleEntityIds.Contains(observation.EntityId))
                {
                    if (raiseOnNewlyPerceivedEntities || ShouldRaiseNewObservation(observation))
                    {
                        AppendChange(builder, $"newly perceived {observation.DisplayName} ({observation.EntityType}): {observation.Description}");
                    }

                    continue;
                }

                if (signaturesByEntityId.TryGetValue(observation.EntityId, out string previousSignature)
                    && previousSignature != observation.Signature)
                {
                    AppendChange(builder, $"perceived change in {observation.DisplayName}: {observation.Description}");
                }
            }

            foreach (string previousId in previousVisibleEntityIds)
            {
                if (!playerSeenIsSignificantChange && IsPlayerEntityId(previousId))
                {
                    continue;
                }

                if (raiseOnLostPerception && !visibleEntityIds.Contains(previousId))
                {
                    AppendChange(builder, $"lost perception of {previousId}");
                }
            }

            return builder.ToString();
        }

        private void TryAddPlayerObservation()
        {
            if (!includePlayerObservation)
            {
                return;
            }

            PlayerMovementController player = FindFirstObjectByType<PlayerMovementController>();
            if (player == null || player.transform == transform)
            {
                return;
            }

            Vector2 delta = player.transform.position - transform.position;
            float distance = delta.magnitude;
            PerceptionChannel sensedChannels = PerceptionChannel.None;
            if (playerVisualRange > 0f && distance <= playerVisualRange)
            {
                bool canSee = !useLineOfSight
                    || !playerRequiresLineOfSight
                    || HasLineOfSight(player.transform.position);
                if (canSee)
                {
                    sensedChannels |= PerceptionChannel.Visual;
                }
            }

            if (playerAudibleRange > 0f && distance <= playerAudibleRange)
            {
                sensedChannels |= PerceptionChannel.Audible;
            }

            if (sensedChannels == PerceptionChannel.None)
            {
                return;
            }

            AddObservation(new PerceptionObservation(
                NormalizePlayerEntityId(),
                string.IsNullOrWhiteSpace(playerDisplayName) ? "Player" : playerDisplayName.Trim(),
                "player",
                distance,
                sensedChannels,
                BuildPlayerDescription(sensedChannels)));
        }

        private void AddObservation(PerceptionObservation observation)
        {
            if (observation == null || string.IsNullOrWhiteSpace(observation.EntityId))
            {
                return;
            }

            if (visibleEntityIds.Contains(observation.EntityId))
            {
                return;
            }

            observations.Add(observation);
            visibleEntityIds.Add(observation.EntityId);
        }

        private bool ShouldRaiseNewObservation(PerceptionObservation observation)
        {
            return playerSeenIsSignificantChange
                && observation != null
                && IsPlayerObservation(observation);
        }

        private string BuildPlayerDescription(PerceptionChannel channels)
        {
            StringBuilder builder = new StringBuilder();
            if ((channels & PerceptionChannel.Visual) != 0)
            {
                AppendPart(builder, string.IsNullOrWhiteSpace(playerVisibleDescription)
                    ? "the player is visible nearby"
                    : playerVisibleDescription);
            }

            if ((channels & PerceptionChannel.Audible) != 0)
            {
                AppendPart(builder, string.IsNullOrWhiteSpace(playerAudibleDescription)
                    ? "the player can be heard nearby"
                    : playerAudibleDescription);
            }

            return builder.Length > 0 ? builder.ToString() : "the player is nearby";
        }

        private string NormalizePlayerEntityId()
        {
            return string.IsNullOrWhiteSpace(playerEntityId) ? "player" : playerEntityId.Trim();
        }

        private static bool IsPlayerEntity(PerceivableEntity entity)
        {
            return entity != null && entity.GetComponentInParent<PlayerMovementController>() != null;
        }

        private static bool IsPlayerObservation(PerceptionObservation observation)
        {
            return observation != null && IsPlayerEntityId(observation.EntityId);
        }

        private static bool IsPlayerEntityId(string entityId)
        {
            return string.Equals(entityId, "player", StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateKnownSignatures()
        {
            signaturesByEntityId.Clear();
            for (int i = 0; i < observations.Count; i++)
            {
                PerceptionObservation observation = observations[i];
                signaturesByEntityId[observation.EntityId] = observation.Signature;
            }
        }

        private bool HasLineOfSight(Vector3 targetPosition)
        {
            Vector2 origin = transform.position;
            Vector2 target = targetPosition;
            Vector2 direction = target - origin;
            float distance = direction.magnitude;
            if (distance <= 0.001f)
            {
                return true;
            }

            RaycastHit2D hit = Physics2D.Raycast(origin, direction.normalized, distance, lineOfSightBlockers);
            return hit.collider == null;
        }

        private string BuildSummary()
        {
            if (observations.Count == 0)
            {
                return "(nothing notable perceived)";
            }

            StringBuilder builder = new StringBuilder();
            int count = Mathf.Min(maxObservations, observations.Count);
            for (int i = 0; i < count; i++)
            {
                PerceptionObservation observation = observations[i];
                builder.Append("- id=");
                builder.Append(observation.EntityId);
                builder.Append(", name=");
                builder.Append(observation.DisplayName);
                builder.Append(", type=");
                builder.Append(observation.EntityType);
                builder.Append(", distance=");
                builder.Append(observation.Distance.ToString("0.0"));
                builder.Append(", channels=");
                builder.Append(observation.Channels);
                builder.Append(", observed=");
                builder.Append(observation.Description);
                builder.AppendLine();
            }

            return builder.ToString();
        }

        private static void AppendChange(StringBuilder builder, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(text);
        }

        private static void AppendPart(StringBuilder builder, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.Append("; ");
            }

            builder.Append(text.Replace('\n', ' ').Replace('\r', ' ').Trim());
        }
    }
}
