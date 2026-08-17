using System.Text;
using CityStateSim.NPC;
using UnityEngine;

namespace CityStateSim.Perception
{
    public sealed class PerceivableEntity : MonoBehaviour
    {
        [SerializeField] private string entityId;
        [SerializeField] private string displayName;
        [SerializeField] private string entityType = "object";
        [SerializeField] private PerceptionChannel availableChannels = PerceptionChannel.Visual;
        [SerializeField, Min(0f)] private float visualRange = 6f;
        [SerializeField, Min(0f)] private float audibleRange = 4f;
        [SerializeField] private bool requireLineOfSight = true;
        [SerializeField, TextArea] private string staticVisibleDescription;
        [SerializeField, TextArea] private string staticAudibleDescription;

        public string EntityId => string.IsNullOrWhiteSpace(entityId) ? name : entityId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public string EntityType => string.IsNullOrWhiteSpace(entityType) ? "object" : entityType;
        public bool RequireLineOfSight => requireLineOfSight;

        private void Awake()
        {
            NpcRuntimeState npc = GetComponent<NpcRuntimeState>();
            if (npc != null && npc.Profile != null)
            {
                if (string.IsNullOrWhiteSpace(entityId))
                {
                    entityId = npc.Profile.NpcId;
                }

                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = npc.Profile.DisplayName;
                }

                if (string.IsNullOrWhiteSpace(entityType) || entityType == "object")
                {
                    entityType = "person";
                }
            }
        }

        public bool TryBuildObservation(Transform observer, out PerceptionObservation observation)
        {
            observation = null;
            if (observer == null || observer == transform)
            {
                return false;
            }

            Vector2 delta = transform.position - observer.position;
            float distance = delta.magnitude;
            PerceptionChannel sensedChannels = PerceptionChannel.None;
            if (CanSense(PerceptionChannel.Visual, distance))
            {
                sensedChannels |= PerceptionChannel.Visual;
            }

            if (CanSense(PerceptionChannel.Audible, distance))
            {
                sensedChannels |= PerceptionChannel.Audible;
            }

            if (sensedChannels == PerceptionChannel.None)
            {
                return false;
            }

            observation = new PerceptionObservation(
                EntityId,
                DisplayName,
                EntityType,
                distance,
                sensedChannels,
                BuildDescription(sensedChannels));
            return true;
        }

        private bool CanSense(PerceptionChannel channel, float distance)
        {
            if ((availableChannels & channel) == 0)
            {
                return false;
            }

            float range = channel == PerceptionChannel.Visual ? visualRange : audibleRange;
            return range > 0f && distance <= range;
        }

        private string BuildDescription(PerceptionChannel channels)
        {
            StringBuilder builder = new StringBuilder();
            AppendStaticDescriptions(builder, channels);

            IPerceivableDetailProvider[] providers = GetComponents<IPerceivableDetailProvider>();
            for (int i = 0; i < providers.Length; i++)
            {
                string detail = providers[i].BuildPerceptionDetail(channels);
                if (string.IsNullOrWhiteSpace(detail))
                {
                    continue;
                }

                AppendPart(builder, detail);
            }

            return builder.Length > 0 ? builder.ToString() : "no notable details";
        }

        private void AppendStaticDescriptions(StringBuilder builder, PerceptionChannel channels)
        {
            if ((channels & PerceptionChannel.Visual) != 0)
            {
                AppendPart(builder, staticVisibleDescription);
            }

            if ((channels & PerceptionChannel.Audible) != 0)
            {
                AppendPart(builder, staticAudibleDescription);
            }
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
