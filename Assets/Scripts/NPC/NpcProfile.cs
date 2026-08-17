using System;
using System.Text;
using CityStateSim.AI;
using UnityEngine;

namespace CityStateSim.NPC
{
    [CreateAssetMenu(menuName = "City State Sim/NPC/NPC Profile")]
    public sealed class NpcProfile : ScriptableObject
    {
        [SerializeField] private string npcId;
        [SerializeField] private string displayName;
        [SerializeField] private string role;
        [SerializeField, TextArea] private string personalitySummary;
        [SerializeField] private NpcValueProfile valueProfile;
        [SerializeField] private NpcInteractionTemplate[] interactionTemplates;

        public string NpcId => npcId;
        public string DisplayName => displayName;
        public string Role => role;
        public string PersonalitySummary => personalitySummary;
        public NpcValueProfile ValueProfile => valueProfile;
        public NpcInteractionTemplate[] InteractionTemplates => interactionTemplates ?? Array.Empty<NpcInteractionTemplate>();

        public string BuildInteractionTemplateSummary()
        {
            NpcInteractionTemplate[] templates = InteractionTemplates;
            if (templates.Length == 0)
            {
                return "(none)";
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < templates.Length; i++)
            {
                NpcInteractionTemplate template = templates[i];
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
            if (string.IsNullOrWhiteSpace(npcId))
            {
                npcId = name;
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = name;
            }
        }
    }

    [Serializable]
    public sealed class NpcInteractionTemplate
    {
        [SerializeField] private string templateId;
        [SerializeField] private string displayName;
        [SerializeField, TextArea] private string description;
        [SerializeField] private string suggestedIntent = NpcIntentType.TalkToNpc.ToString();
        [SerializeField] private string targetActorHint;
        [SerializeField, Range(0, 100)] private int priority = 50;
        [SerializeField] private bool oneShot = true;
        [SerializeField] private bool interruptible = true;
        [SerializeField] private string[] requiredRelationshipTags;

        public string TemplateId => templateId;
        public string DisplayName => displayName;
        public string Description => description;
        public string SuggestedIntent => suggestedIntent;
        public string TargetActorHint => targetActorHint;
        public int Priority => priority;
        public bool OneShot => oneShot;
        public bool Interruptible => interruptible;
        public string[] RequiredRelationshipTags => requiredRelationshipTags ?? Array.Empty<string>();

        public string ToSummaryLine()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("- id=");
            builder.Append(string.IsNullOrWhiteSpace(templateId) ? "(unnamed)" : templateId);
            builder.Append(", name=");
            builder.Append(string.IsNullOrWhiteSpace(displayName) ? "(unnamed)" : displayName);
            builder.Append(", intent=");
            builder.Append(string.IsNullOrWhiteSpace(suggestedIntent) ? NpcIntentType.TalkToNpc.ToString() : suggestedIntent);
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

            string[] tags = RequiredRelationshipTags;
            if (tags.Length > 0)
            {
                builder.Append(", relationTags=");
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
