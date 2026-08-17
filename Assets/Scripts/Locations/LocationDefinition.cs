using System;
using System.Text;
using UnityEngine;

namespace CityStateSim.Locations
{
    [CreateAssetMenu(menuName = "City State Sim/Locations/Location Definition")]
    public sealed class LocationDefinition : ScriptableObject
    {
        [SerializeField] private string locationId;
        [SerializeField] private string displayName;
        [SerializeField] private LocationType type = LocationType.Unknown;
        [SerializeField] private string areaId;
        [SerializeField, TextArea] private string description;
        [SerializeField] private string[] capabilityTags;
        [SerializeField] private LocationTaskTemplate[] taskTemplates;

        [Header("Access")]
        [SerializeField] private bool alwaysOpen = true;
        [SerializeField, Range(0, 23)] private int openHour = 6;
        [SerializeField, Range(0, 23)] private int closeHour = 22;

        public string LocationId => locationId;
        public string DisplayName => displayName;
        public LocationType Type => type;
        public string AreaId => areaId;
        public string Description => description;
        public string[] CapabilityTags => capabilityTags ?? Array.Empty<string>();
        public LocationTaskTemplate[] TaskTemplates => taskTemplates ?? Array.Empty<LocationTaskTemplate>();
        public bool AlwaysOpen => alwaysOpen;
        public int OpenHour => openHour;
        public int CloseHour => closeHour;

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(locationId))
            {
                locationId = Guid.NewGuid().ToString("N");
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = name;
            }
        }

        public bool IsOpenAtHour(int hour)
        {
            if (alwaysOpen)
            {
                return true;
            }

            hour = Mathf.Clamp(hour, 0, 23);
            if (openHour == closeHour)
            {
                return false;
            }

            if (openHour < closeHour)
            {
                return hour >= openHour && hour < closeHour;
            }

            return hour >= openHour || hour < closeHour;
        }

        public bool HasCapability(string capability)
        {
            if (string.IsNullOrWhiteSpace(capability))
            {
                return false;
            }

            string normalizedCapability = NormalizeLoose(capability);
            if (normalizedCapability.Length == 0)
            {
                return false;
            }

            string[] tags = CapabilityTags;
            for (int i = 0; i < tags.Length; i++)
            {
                if (NormalizeLoose(tags[i]) == normalizedCapability)
                {
                    return true;
                }
            }

            return false;
        }

        public bool HasAnyCapability(params string[] capabilities)
        {
            if (capabilities == null || capabilities.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < capabilities.Length; i++)
            {
                if (HasCapability(capabilities[i]))
                {
                    return true;
                }
            }

            return false;
        }

        public LocationTaskTemplate[] GetAvailableTaskTemplates()
        {
            LocationTaskTemplate[] templates = TaskTemplates;
            if (templates.Length == 0)
            {
                return Array.Empty<LocationTaskTemplate>();
            }

            System.Collections.Generic.List<LocationTaskTemplate> available = new System.Collections.Generic.List<LocationTaskTemplate>();
            for (int i = 0; i < templates.Length; i++)
            {
                LocationTaskTemplate template = templates[i];
                if (template != null && template.IsAvailableAt(this))
                {
                    available.Add(template);
                }
            }

            return available.ToArray();
        }

        public string BuildTaskTemplateSummary()
        {
            LocationTaskTemplate[] templates = GetAvailableTaskTemplates();
            if (templates.Length == 0)
            {
                return "(none)";
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < templates.Length; i++)
            {
                LocationTaskTemplate template = templates[i];
                if (template == null)
                {
                    continue;
                }

                builder.AppendLine(template.ToSummaryLine(this));
            }

            return builder.Length > 0 ? builder.ToString() : "(none)";
        }

        public bool TryGetAvailableTaskTemplate(string templateIdOrActivityKind, string primaryAction, out LocationTaskTemplate result)
        {
            result = null;
            string normalizedTarget = NormalizeLoose(templateIdOrActivityKind);
            string normalizedAction = NormalizeLoose(primaryAction);
            LocationTaskTemplate[] templates = GetAvailableTaskTemplates();
            for (int i = 0; i < templates.Length; i++)
            {
                LocationTaskTemplate template = templates[i];
                if (template == null)
                {
                    continue;
                }

                if (normalizedAction.Length > 0 && NormalizeLoose(template.PrimaryAction) != normalizedAction)
                {
                    continue;
                }

                if (normalizedTarget.Length == 0)
                {
                    result = template;
                    return true;
                }

                if (NormalizeLoose(template.TemplateId) == normalizedTarget
                    || NormalizeLoose(template.ActivityKind) == normalizedTarget)
                {
                    result = template;
                    return true;
                }
            }

            return false;
        }

        public string BuildAvailableTaskTemplateList(string primaryAction)
        {
            string normalizedAction = NormalizeLoose(primaryAction);
            LocationTaskTemplate[] templates = GetAvailableTaskTemplates();
            if (templates.Length == 0)
            {
                return "(none)";
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < templates.Length; i++)
            {
                LocationTaskTemplate template = templates[i];
                if (template == null)
                {
                    continue;
                }

                if (normalizedAction.Length > 0 && NormalizeLoose(template.PrimaryAction) != normalizedAction)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append("; ");
                }

                builder.Append("templateId=");
                builder.Append(string.IsNullOrWhiteSpace(template.TemplateId) ? "(unnamed)" : template.TemplateId);
                builder.Append(", activityKind=");
                builder.Append(string.IsNullOrWhiteSpace(template.ActivityKind) ? "(empty)" : template.ActivityKind);
                builder.Append(", action=");
                builder.Append(string.IsNullOrWhiteSpace(template.PrimaryAction) ? "ContinueCurrentAction" : template.PrimaryAction);
            }

            return builder.Length > 0 ? builder.ToString() : "(none)";
        }

        private static string NormalizeLoose(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.ToLowerInvariant()
                    .Replace("'", string.Empty)
                    .Replace(" ", string.Empty)
                    .Replace("_", string.Empty)
                    .Replace("-", string.Empty);
        }
    }

    [Serializable]
    public sealed class LocationTaskTemplate
    {
        [SerializeField] private string templateId;
        [SerializeField] private string displayName;
        [SerializeField, TextArea] private string description;
        [SerializeField] private string primaryAction = "ContinueCurrentAction";
        [SerializeField] private string activityKind;
        [SerializeField] private bool requiresActorTarget;
        [SerializeField] private bool requiresGroup;
        [SerializeField] private string[] requiredCapabilityTags;
        [SerializeField, Min(0)] private int activityDurationMinutes = 20;
        [SerializeField] private bool startConversationAfterActivity = true;
        [SerializeField, Range(0, 100)] private int defaultPriority = 50;
        [SerializeField] private bool interruptible = true;

        public string TemplateId => templateId;
        public string DisplayName => displayName;
        public string Description => description;
        public string PrimaryAction => primaryAction;
        public string ActivityKind => activityKind;
        public bool RequiresActorTarget => requiresActorTarget;
        public bool RequiresGroup => requiresGroup;
        public string[] RequiredCapabilityTags => requiredCapabilityTags ?? Array.Empty<string>();
        public int ActivityDurationMinutes => activityDurationMinutes;
        public bool StartConversationAfterActivity => startConversationAfterActivity;
        public int DefaultPriority => defaultPriority;
        public bool Interruptible => interruptible;

        public bool IsAvailableAt(LocationDefinition location)
        {
            if (location == null)
            {
                return false;
            }

            string[] requiredTags = RequiredCapabilityTags;
            if (requiredTags.Length == 0)
            {
                return true;
            }

            return location.HasAnyCapability(requiredTags);
        }

        public string ToSummaryLine(LocationDefinition location)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("- id=");
            builder.Append(string.IsNullOrWhiteSpace(templateId) ? "(unnamed)" : templateId);
            builder.Append(", name=");
            builder.Append(string.IsNullOrWhiteSpace(displayName) ? "(unnamed)" : displayName);
            builder.Append(", action=");
            builder.Append(string.IsNullOrWhiteSpace(primaryAction) ? "ContinueCurrentAction" : primaryAction);
            builder.Append(", priority=");
            builder.Append(defaultPriority);

            if (!string.IsNullOrWhiteSpace(activityKind))
            {
                builder.Append(", activityKind=");
                builder.Append(activityKind);
            }

            if (requiresActorTarget)
            {
                builder.Append(", requiresActorTarget=true");
            }

            if (requiresGroup)
            {
                builder.Append(", requiresGroup=true");
            }

            builder.Append(", durationMinutes=");
            builder.Append(activityDurationMinutes);
            builder.Append(", startConversationAfterActivity=");
            builder.Append(startConversationAfterActivity ? "true" : "false");

            string[] tags = RequiredCapabilityTags;
            if (tags.Length > 0)
            {
                builder.Append(", requiredCapabilities=");
                builder.Append(string.Join("|", tags));
            }

            if (!string.IsNullOrWhiteSpace(description))
            {
                builder.Append(", description=");
                builder.Append(description.Replace('\n', ' ').Replace('\r', ' '));
            }

            if (location != null)
            {
                builder.Append(", location=");
                builder.Append(location.DisplayName);
            }

            return builder.ToString();
        }
    }
}
