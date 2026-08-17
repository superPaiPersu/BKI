using System;

namespace CityStateSim.AI
{
    [Serializable]
    public sealed class NpcDailyPlanAiResponse
    {
        public string summary;
        public NpcDailyIntentAiEntry[] intents;
        public NpcDailyPlanAiEntry[] entries;
    }

    [Serializable]
    public sealed class NpcDailyIntentAiEntry
    {
        public string label;
        public int earliestStartHour;
        public int earliestStartMinute;
        public int latestEndHour;
        public int latestEndMinute;
        public string targetLocationId;
        public string targetActorId;
        public string desiredOutcome;
        public string allowedBehaviors;
        public string completionCondition;
        public string activityKind;
        public string[] participantActorIds;
        public string[] requiredActorIds;
        public string[] optionalActorIds;
        public int patienceMinutes;
        public int priority;
        public bool canInterruptRoutine = true;
        public string reason;
    }

    [Serializable]
    public sealed class NpcDailyPlanAiEntry
    {
        public string label;
        public int startHour;
        public int startMinute;
        public int endHour;
        public int endMinute;
        public string locationId;
        public string actionName;
        public int priority;
        public bool interruptible = true;
        public string reason;
    }
}
