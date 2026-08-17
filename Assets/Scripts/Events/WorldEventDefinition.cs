using System;
using System.Text;
using CityStateSim.AI;
using CityStateSim.Core;
using CityStateSim.Locations;
using UnityEngine;

namespace CityStateSim.Events
{
    [CreateAssetMenu(menuName = "City State Sim/Events/World Event")]
    public sealed class WorldEventDefinition : ScriptableObject
    {
        [SerializeField] private string eventId;
        [SerializeField] private string displayName;
        [SerializeField] private WorldEventType type = WorldEventType.City;
        [SerializeField, TextArea] private string description;

        [Header("Default Target")]
        [SerializeField] private LocationDefinition targetLocation;
        [SerializeField] private GameTime overrideStart = new GameTime(8, 0);
        [SerializeField] private GameTime overrideEnd = new GameTime(10, 0);
        [SerializeField] private string overrideAction = "Respond to event";
        [SerializeField] private int priority = 100;
        [SerializeField] private bool createsTemporaryScheduleOverride = true;
        [SerializeField] private WorldEventResponseTemplate[] responseTemplates;

        public string EventId => eventId;
        public string DisplayName => displayName;
        public WorldEventType Type => type;
        public string Description => description;
        public LocationDefinition TargetLocation => targetLocation;
        public GameTime OverrideStart => overrideStart;
        public GameTime OverrideEnd => overrideEnd;
        public string OverrideAction => overrideAction;
        public int Priority => priority;
        public bool CreatesTemporaryScheduleOverride => createsTemporaryScheduleOverride;
        public WorldEventResponseTemplate[] ResponseTemplates => responseTemplates ?? Array.Empty<WorldEventResponseTemplate>();

        public string BuildResponseTemplateSummary()
        {
            WorldEventResponseTemplate[] templates = ResponseTemplates;
            if (templates.Length == 0)
            {
                return "(none)";
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < templates.Length; i++)
            {
                WorldEventResponseTemplate template = templates[i];
                if (template == null)
                {
                    continue;
                }

                builder.AppendLine(template.ToSummaryLine());
            }

            return builder.Length > 0 ? builder.ToString() : "(none)";
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(eventId))
            {
                eventId = name;
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = name;
            }
        }
    }

    [Serializable]
    public sealed class WorldEventResponseTemplate
    {
        [SerializeField] private string templateId;
        [SerializeField] private string displayName;
        [SerializeField, TextArea] private string description;
        [SerializeField] private string suggestedIntent = NpcIntentType.ReactToEvent.ToString();
        [SerializeField] private string targetActorHint;
        [SerializeField, Range(0, 100)] private int priority = 50;
        [SerializeField] private bool oneShot = true;
        [SerializeField] private bool interruptible = true;
        [SerializeField] private string[] requiredEventTags;

        public string TemplateId => templateId;
        public string DisplayName => displayName;
        public string Description => description;
        public string SuggestedIntent => suggestedIntent;
        public string TargetActorHint => targetActorHint;
        public int Priority => priority;
        public bool OneShot => oneShot;
        public bool Interruptible => interruptible;
        public string[] RequiredEventTags => requiredEventTags ?? Array.Empty<string>();

        public string ToSummaryLine()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("- id=");
            builder.Append(string.IsNullOrWhiteSpace(templateId) ? "(unnamed)" : templateId);
            builder.Append(", name=");
            builder.Append(string.IsNullOrWhiteSpace(displayName) ? "(unnamed)" : displayName);
            builder.Append(", intent=");
            builder.Append(string.IsNullOrWhiteSpace(suggestedIntent) ? NpcIntentType.ReactToEvent.ToString() : suggestedIntent);
            builder.Append(", priority=");
            builder.Append(priority);

            if (oneShot)
            {
                builder.Append(", oneShot=true");
            }

            if (!interruptible)
            {
                builder.Append(", interruptible=false");
            }

            if (!string.IsNullOrWhiteSpace(targetActorHint))
            {
                builder.Append(", targetActorHint=");
                builder.Append(targetActorHint);
            }

            string[] tags = RequiredEventTags;
            if (tags.Length > 0)
            {
                builder.Append(", eventTags=");
                builder.Append(string.Join("|", tags));
            }

            if (!string.IsNullOrWhiteSpace(description))
            {
                builder.Append(", description=");
                builder.Append(description.Replace('\n', ' ').Replace('\r', ' '));
            }

            return builder.ToString();
        }
    }
}
