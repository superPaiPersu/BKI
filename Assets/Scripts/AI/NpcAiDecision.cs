using System;
using UnityEngine;

namespace CityStateSim.AI
{
    public enum NpcEventKind
    {
        None = 0,
        OneShot = 1,
        ScheduleOverride = 2
    }

    public enum NpcTimingMode
    {
        Immediate = 0,
        DelayMinutes = 1,
        TodayAtTime = 2,
        NextDayAtTime = 3
    }

    public static class NpcTimingModeUtility
    {
        public static NpcTimingMode Parse(string value)
        {
            return Enum.TryParse(value, true, out NpcTimingMode parsed)
                ? parsed
                : NpcTimingMode.Immediate;
        }

        public static void Normalize(
            ref string timingMode,
            ref int delayMinutes,
            ref int scheduledStartHour,
            ref int scheduledStartMinute)
        {
            delayMinutes = Mathf.Clamp(delayMinutes, 0, 1440);
            scheduledStartHour = Mathf.Clamp(scheduledStartHour, -1, 23);
            scheduledStartMinute = Mathf.Clamp(scheduledStartMinute, -1, 59);

            NpcTimingMode mode = Parse(timingMode);
            switch (mode)
            {
                case NpcTimingMode.DelayMinutes:
                    if (delayMinutes <= 0)
                    {
                        timingMode = NpcTimingMode.Immediate.ToString();
                        scheduledStartHour = -1;
                        scheduledStartMinute = -1;
                        return;
                    }

                    timingMode = mode.ToString();
                    scheduledStartHour = -1;
                    scheduledStartMinute = -1;
                    return;
                case NpcTimingMode.TodayAtTime:
                case NpcTimingMode.NextDayAtTime:
                    if (scheduledStartHour < 0 || scheduledStartMinute < 0)
                    {
                        timingMode = NpcTimingMode.Immediate.ToString();
                        delayMinutes = 0;
                        scheduledStartHour = -1;
                        scheduledStartMinute = -1;
                        return;
                    }

                    timingMode = mode.ToString();
                    delayMinutes = 0;
                    return;
                case NpcTimingMode.Immediate:
                default:
                    timingMode = NpcTimingMode.Immediate.ToString();
                    delayMinutes = 0;
                    scheduledStartHour = -1;
                    scheduledStartMinute = -1;
                    return;
            }
        }
    }

    [Serializable]
    public sealed class NpcPendingEncounterChange
    {
        public string operation = string.Empty;
        public string targetActorId = string.Empty;
        public string actionKind = string.Empty;
        public string topic = string.Empty;
        public int priority = 50;
        public string reason = string.Empty;
        public bool consumeOnTrigger = true;
        public int expiresAfterDays = 0;
        public string interruptPolicy = "only_if_free";
        public int cooldownMinutes = 30;

        public void Clamp()
        {
            operation = Clean(operation);
            targetActorId = Clean(targetActorId);
            actionKind = Clean(actionKind);
            topic = Clean(topic);
            reason = Clean(reason);
            interruptPolicy = Clean(interruptPolicy);
            priority = Mathf.Clamp(priority, 0, 100);
            expiresAfterDays = Mathf.Clamp(expiresAfterDays, 0, 365);
            cooldownMinutes = Mathf.Clamp(cooldownMinutes, 1, 1440);
        }

        private static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        }
    }

    [Serializable]
    public sealed class NpcSocialPlanChange
    {
        public string operation = string.Empty;
        public string planId = string.Empty;
        public string label = string.Empty;
        public string activityKind = string.Empty;
        public string targetLocationId = string.Empty;
        public string organizerActorId = string.Empty;
        public string[] participantActorIds = Array.Empty<string>();
        public string[] requiredActorIds = Array.Empty<string>();
        public string[] optionalActorIds = Array.Empty<string>();
        public string[] acceptedActorIds = Array.Empty<string>();
        public string[] pendingActorIds = Array.Empty<string>();
        public string[] declinedActorIds = Array.Empty<string>();
        public int patienceMinutes;
        public int priority = 70;
        public string timingMode = NpcTimingMode.Immediate.ToString();
        public int delayMinutes;
        public int scheduledStartHour = -1;
        public int scheduledStartMinute = -1;
        public string reason = string.Empty;

        public NpcTimingMode ParsedTimingMode => NpcTimingModeUtility.Parse(timingMode);

        public void Clamp()
        {
            operation = Clean(operation);
            planId = Clean(planId);
            label = Clean(label);
            activityKind = Clean(activityKind);
            targetLocationId = Clean(targetLocationId);
            organizerActorId = Clean(organizerActorId);
            reason = Clean(reason);
            participantActorIds ??= Array.Empty<string>();
            requiredActorIds ??= Array.Empty<string>();
            optionalActorIds ??= Array.Empty<string>();
            acceptedActorIds ??= Array.Empty<string>();
            pendingActorIds ??= Array.Empty<string>();
            declinedActorIds ??= Array.Empty<string>();
            patienceMinutes = Mathf.Clamp(patienceMinutes, 0, 240);
            priority = Mathf.Clamp(priority, 0, 100);
            timingMode = Clean(timingMode);
            NpcTimingModeUtility.Normalize(ref timingMode, ref delayMinutes, ref scheduledStartHour, ref scheduledStartMinute);
        }

        private static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        }
    }

    [Serializable]
    public sealed class NpcPostConversationAction
    {
        public bool hasAction;
        public string intent = NpcIntentType.ContinueCurrentAction.ToString();
        public string eventKind = NpcEventKind.None.ToString();
        public string targetLocationId = string.Empty;
        public string targetActorId = string.Empty;
        public string plannedTargetLocationId = string.Empty;
        public string activityKind = string.Empty;
        public string[] participantActorIds = Array.Empty<string>();
        public string[] requiredActorIds = Array.Empty<string>();
        public string[] optionalActorIds = Array.Empty<string>();
        public int patienceMinutes;
        public string timingMode = NpcTimingMode.Immediate.ToString();
        public int delayMinutes;
        public int scheduledStartHour = -1;
        public int scheduledStartMinute = -1;
        public string reason = string.Empty;

        public NpcTimingMode ParsedTimingMode => NpcTimingModeUtility.Parse(timingMode);

        public NpcIntentType ParsedIntent
        {
            get
            {
                return Enum.TryParse(intent, true, out NpcIntentType parsed)
                    ? parsed
                    : NpcIntentType.ContinueCurrentAction;
            }
        }

        public void Clamp()
        {
            intent = Clean(intent);
            eventKind = Clean(eventKind);
            targetLocationId = Clean(targetLocationId);
            targetActorId = Clean(targetActorId);
            plannedTargetLocationId = Clean(plannedTargetLocationId);
            activityKind = Clean(activityKind);
            reason = Clean(reason);

            if (string.Equals(targetActorId, "player", StringComparison.OrdinalIgnoreCase)
                && ParsedIntent == NpcIntentType.TalkToNpc)
            {
                intent = NpcIntentType.TalkToPlayer.ToString();
            }

            if (!hasAction)
            {
                intent = NpcIntentType.ContinueCurrentAction.ToString();
                eventKind = NpcEventKind.None.ToString();
                targetLocationId = string.Empty;
                targetActorId = string.Empty;
                plannedTargetLocationId = string.Empty;
                activityKind = string.Empty;
                timingMode = NpcTimingMode.Immediate.ToString();
                delayMinutes = 0;
                scheduledStartHour = -1;
                scheduledStartMinute = -1;
                reason = string.Empty;
            }

            participantActorIds ??= Array.Empty<string>();
            requiredActorIds ??= Array.Empty<string>();
            optionalActorIds ??= Array.Empty<string>();
            patienceMinutes = Mathf.Clamp(patienceMinutes, 0, 240);
            timingMode = Clean(timingMode);
            NpcTimingModeUtility.Normalize(ref timingMode, ref delayMinutes, ref scheduledStartHour, ref scheduledStartMinute);
        }

        private static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        }
    }

    [Serializable]
    public sealed class NpcAiDecision
    {
        public string intent = NpcIntentType.ContinueCurrentAction.ToString();
        public string behaviorMode = NpcBehaviorMode.FollowSchedule.ToString();
        public string tone = "neutral";
        public string dialogue = string.Empty;
        public string emotion = "neutral";
        public string nextActionPreference = "continue";
        public string originalGoal = string.Empty;
        public string currentGoal = string.Empty;
        public string goalStatus = "none";
        public string goalStatusReason = string.Empty;
        public string nextSpeakerId = string.Empty;
        public string secondaryEventQuery = string.Empty;
        public string eventKind = NpcEventKind.None.ToString();
        public string targetLocationId = string.Empty;
        public string targetActorId = string.Empty;
        public string plannedTargetLocationId = string.Empty;
        public string activityKind = string.Empty;
        public string[] participantActorIds = Array.Empty<string>();
        public string[] requiredActorIds = Array.Empty<string>();
        public string[] optionalActorIds = Array.Empty<string>();
        public int patienceMinutes;
        public string timingMode = NpcTimingMode.Immediate.ToString();
        public int delayMinutes;
        public int scheduledStartHour = -1;
        public int scheduledStartMinute = -1;
        public string dialogueContextKind = string.Empty;
        public string dialogueSourceActorId = string.Empty;
        public string dialogueSubjectActorId = string.Empty;
        public string dialogueSubjectLocationId = string.Empty;
        public string dialogueSourceText = string.Empty;
        public NpcPendingEncounterChange[] pendingEncounterChanges = Array.Empty<NpcPendingEncounterChange>();
        public NpcSocialPlanChange[] socialPlanChanges = Array.Empty<NpcSocialPlanChange>();
        public NpcPostConversationAction postConversationAction = new NpcPostConversationAction();
        public int relationshipDeltaHint;
        public float confidence = 0.5f;

        public NpcIntentType ParsedIntent
        {
            get
            {
                return Enum.TryParse(intent, true, out NpcIntentType parsed)
                    ? parsed
                    : NpcIntentType.ContinueCurrentAction;
            }
        }

        public NpcBehaviorMode ParsedBehaviorMode
        {
            get
            {
                return Enum.TryParse(behaviorMode, true, out NpcBehaviorMode parsed)
                    ? parsed
                    : NpcBehaviorMode.FollowSchedule;
            }
        }

        public NpcTimingMode ParsedTimingMode => NpcTimingModeUtility.Parse(timingMode);

        public void ClampHints()
        {
            intent = Clean(intent);
            behaviorMode = Clean(behaviorMode);
            tone = Clean(tone);
            dialogue = Clean(dialogue);
            emotion = Clean(emotion);
            nextActionPreference = Clean(nextActionPreference);
            originalGoal = Clean(originalGoal);
            currentGoal = Clean(currentGoal);
            goalStatus = Clean(goalStatus);
            goalStatusReason = Clean(goalStatusReason);
            if (!string.Equals(goalStatus, "none", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(goalStatus, "active", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(goalStatus, "completed", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(goalStatus, "abandoned", StringComparison.OrdinalIgnoreCase))
            {
                goalStatus = "none";
            }

            if (!string.IsNullOrWhiteSpace(goalStatus))
            {
                goalStatus = goalStatus.ToLowerInvariant();
            }

            nextSpeakerId = Clean(nextSpeakerId);
            secondaryEventQuery = Clean(secondaryEventQuery);
            eventKind = Clean(eventKind);
            targetLocationId = Clean(targetLocationId);
            targetActorId = Clean(targetActorId);
            plannedTargetLocationId = Clean(plannedTargetLocationId);
            activityKind = Clean(activityKind);
            timingMode = Clean(timingMode);
            dialogueContextKind = Clean(dialogueContextKind);
            dialogueSourceActorId = Clean(dialogueSourceActorId);
            dialogueSubjectActorId = Clean(dialogueSubjectActorId);
            dialogueSubjectLocationId = Clean(dialogueSubjectLocationId);
            dialogueSourceText = Clean(dialogueSourceText);

            if (string.Equals(targetActorId, "player", StringComparison.OrdinalIgnoreCase)
                && ParsedIntent == NpcIntentType.TalkToNpc)
            {
                intent = NpcIntentType.TalkToPlayer.ToString();
            }

            NpcTimingModeUtility.Normalize(ref timingMode, ref delayMinutes, ref scheduledStartHour, ref scheduledStartMinute);
            relationshipDeltaHint = Mathf.Clamp(relationshipDeltaHint, -2, 2);
            confidence = Mathf.Clamp01(confidence);
            patienceMinutes = Mathf.Clamp(patienceMinutes, 0, 240);

            participantActorIds ??= Array.Empty<string>();
            requiredActorIds ??= Array.Empty<string>();
            optionalActorIds ??= Array.Empty<string>();

            pendingEncounterChanges ??= Array.Empty<NpcPendingEncounterChange>();
            for (int i = 0; i < pendingEncounterChanges.Length; i++)
            {
                pendingEncounterChanges[i]?.Clamp();
            }

            socialPlanChanges ??= Array.Empty<NpcSocialPlanChange>();
            for (int i = 0; i < socialPlanChanges.Length; i++)
            {
                socialPlanChanges[i]?.Clamp();
            }

            postConversationAction ??= new NpcPostConversationAction();
            postConversationAction.Clamp();
        }

        public string GetPrimaryDialogue()
        {
            return dialogue ?? string.Empty;
        }

        public string GetPrimaryNextActionPreference()
        {
            return nextActionPreference ?? string.Empty;
        }

        private static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        }
    }
}
