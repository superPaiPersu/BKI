namespace CityStateSim.AI
{
    using System.Text;
    using CityStateSim.Tasks;

    public static class NpcAiPromptBuilder
    {
        public const string SystemPrompt =
            "You are an NPC decision module for a 2D city-state life simulation game. " +
            "Output valid JSON for the requested schema. " +
            "Make one immediate decision; the runtime asks again after that step finishes or new evidence arrives. " +
            "Structured fields drive execution: intent, targetActorId, targetLocationId, eventKind, timingMode, timing fields, socialPlanChanges, and postConversationAction. " +
            "Do not output action lists; preserve originalGoal for the current matter, update currentGoal for this stage, and choose only the next executable action. " +
            "Actor and location targets are exact raw ids from the allowed lists. " +
            "Never invent actor ids, location ids, action names, activity kinds, task template ids, or location capabilities. " +
            "A location's listed task templates define which activity kinds can actually happen there. " +
            "Location capabilityTags only explain why templates are available; do not infer activities from location names, descriptions, or common sense. " +
            "If a requested activity is unsupported by the selected location's task templates, refuse, clarify, or choose a feasible prep step instead of fabricating support. " +
            "The emotion field is a portrait image name. " +
            "Think like a real person with duties, habits, relationships, and emotional boundaries. " +
            "Choose actions by urgency, relationship, current duty, memory, perception, task failures, and available task templates. " +
            "Fact records are evidence entries, including direct observations and quoted claims; do not treat a claim as verified unless its evidence says it was directly observed. " +
            "Dialogue is spoken text only; put private reasoning in nextActionPreference. " +
            "During any dialogue mode, put only immediate after-dialogue executable behavior in postConversationAction. Delayed, future, or shared commitments go in socialPlanChanges with postConversationAction.hasAction=false. " +
            "secondaryEventQuery is for optional minor records that would materially change the decision. " +
            "pendingEncounterChanges stores persistent future person-specific opportunities. " +
            "socialPlanChanges stores shared appointments, promises, meals, visits, and gatherings with time, venue, participants, and acceptance state. " +
            "Keep dialogue concise and in character.";

        public const string LocationTaskRules =
            "- Use exact locationId values from the allowed location list; never use display names as ids.\n" +
            "- targetLocationId means a real registered LocationMarker-backed place. Leave it empty when the immediate goal is only a person.\n" +
            "- The allowed location list includes each location's available task templates. Those templates are the legal menu for location-based actions.\n" +
            "- AttendActivity requires a final venue and an activityKind copied from that venue's listed AttendActivity template, or that template's templateId when appropriate.\n" +
            "- WorkAtLocation and RestAtLocation must be backed by a listed template at the selected location. If activityKind is set, copy it from that template.\n" +
            "- Do not infer meals, work, rest, medicine, visits, free talk, or public gatherings from a location name, description, or real-world common sense. Use only the task templates shown for that exact location.\n" +
            "- Person-first goals use targetActorId. Use FindActor to locate/observe, TalkToNpc to converse, FollowActor to follow, and keep targetLocationId optional background unless a final venue is known.\n" +
            "- If a failure says a target was absent or a location could not support the task, treat that as fresh evidence and choose a corrected legal action.";

        public static string BuildUserPrompt(NpcAiRequest request)
        {
            string portraitList = BuildPortraitNameList(request);
            return
                "NPC context:\n" +
                $"- id: {request.npcId}\n" +
                $"- name: {request.npcName}\n" +
                $"- role: {request.role}\n" +
                $"- personality: {request.personalitySummary}\n" +
                $"- date: {request.date}\n" +
                $"- calendar details: {request.date.ToLongString()}\n" +
                $"- time: {request.time}\n" +
                $"- current actual location: {BuildLocationLine(request.currentLocationId, request.currentLocation)}\n" +
                $"- current schedule target: {BuildLocationLine(request.plannedLocationId, request.plannedLocation)}\n" +
                $"- current schedule action: {request.currentAction}\n" +
                $"- current portrait image name: {request.currentEmotion}\n" +
                $"- current location task templates: {request.currentLocationTaskSummary}\n" +
                $"- current NPC interaction templates: {request.currentNpcInteractionTemplateSummary}\n" +
                $"- current world event response templates: {request.currentWorldEventTemplateSummary}\n" +
                $"- relationship to player: {request.playerRelationshipSummary}\n" +
                $"- recent memories: {request.recentMemorySummary}\n" +
                $"- same-day raw player dialogue transcript: {BuildSameDayPlayerDialogueTranscript(request)}\n" +
                $"- evidence fact records: {BuildFactSummary(request)}\n" +
                $"- current perception from sight/hearing: {request.perceptionSummary}\n" +
                $"- observed event: {request.observedEventSummary}\n" +
                $"- rolling goal context: {BuildRollingGoalSummary(request)}\n" +
                $"- persistent pending encounters: {request.pendingEncounterSummary}\n" +
                $"- persistent social plans: {request.socialPlanSummary}\n" +
                $"- player quests related to this NPC: {request.playerQuestSummary}\n" +
                $"- festival rule: {request.festivalRuleSummary}\n" +
                $"- secondary event lookup access: {BuildSecondaryEventAccessSummary(request)}\n" +
                $"- secondary event lookup result: {BuildSecondaryEventLookupResultSummary(request)}\n" +
                $"- allowed portrait image names for the emotion field: {portraitList}\n" +
                $"- portrait resource folder for this NPC: Resources/{NpcPortraitCatalog.DescribePortraitPath(request.npcId, request.npcName)}\n\n" +
                "Allowed locations for targetLocationId:\n" +
                (string.IsNullOrWhiteSpace(request.allowedLocationSummary) ? "(none)" : request.allowedLocationSummary) +
                "\n" +
                "Allowed actors for targetActorId:\n" +
                (string.IsNullOrWhiteSpace(request.allowedActorSummary) ? "(none)" : request.allowedActorSummary) +
                "\n" +
                "Location/task rules:\n" +
                LocationTaskRules +
                "\n" +
                "Decision format:\n" +
                "- One immediate decision only; do not output action lists or future step sequences.\n" +
                "- originalGoal is the durable reason this multi-step matter began. Keep it stable while pursuing the same matter.\n" +
                "- currentGoal is the current stage, based on completedResults and new perception.\n" +
                "- goalStatus is none, active, completed, or abandoned. Use completed/abandoned only when the originalGoal is truly resolved or intentionally dropped.\n" +
                "- Action timing is controlled by timingMode: Immediate, DelayMinutes, TodayAtTime, or NextDayAtTime.\n" +
                "- Shared appointments, promises, meals, visits, and gatherings go in socialPlanChanges, not pendingEncounterChanges.\n" +
                "- In dialogue modes, dialogue is the spoken line. postConversationAction is only for an immediate executable action after the current dialogue closes; future or delayed shared commitments must use socialPlanChanges instead.\n" +
                "- nextActionPreference is memory or reasoning text, not an executable action.\n" +
                "Runtime task constraints:\n" +
                NpcTaskConstraintValidator.BuildConstraintSummary() +
                "Task semantics:\n" +
                "- ContinueCurrentAction: keep the current task or schedule.\n" +
                "- TalkToPlayer/TalkToNpc: start or continue a real conversation with the target actor.\n" +
                "- MoveToLocation/WorkAtLocation/RestAtLocation/JoinFestival: location based behavior constrained by the selected location.\n" +
                "- MoveToLocation is for pure place travel; when a person is the goal, use FindActor or TalkToNpc instead of attaching targetActorId to MoveToLocation.\n" +
                "- FindActor: locate, approach, and observe targetActorId once, then ask again. For live actor search, targetActorId is the real target and targetLocationId is optional background, not a hard search limit.\n" +
                "- FollowActor: continuously follow targetActorId; use targetActorId=player when the player leads the way. targetLocationId or plannedTargetLocationId can mark the destination.\n" +
                "- ReactToEvent/AvoidActor: one immediate person/event response. For helping someone, choose a concrete step such as FindActor, TalkToNpc, FollowActor, MoveToLocation, or ReactToEvent.\n" +
                "- AttendActivity: join the final shared venue for a shared activity whose activityKind comes from that venue's listed templates.\n" +
                "- SelfTalk: a short mutter; it is not a movement task.\n" +
                "Decision principles:\n" +
                "- Pick the next useful step by urgency, relationship, personality, duty, memory, perception, and task failures.\n" +
                "- Prefer the provided templates when they fit the situation.\n" +
                "- Concrete feasible player requests should become executable intent and target fields.\n" +
                "- Vague, unsupported, or impossible requests call for a brief refusal or clarification.\n" +
                "- secondaryEventQuery can request optional lookup; when results are present, decide normally from them.\n" +
                "- pendingEncounterChanges records future person-specific opportunities that should persist, such as \"when I next see Tom, ask about X\".\n" +
                "- socialPlanChanges records real shared commitments with a time, final venue, participants, and who has accepted or still needs confirmation.\n" +
                "- In dialogue modes, top-level intent is the spoken turn; postConversationAction is the cached immediate action after the current dialogue closes.\n\n" +
                "Choose the most appropriate next intent and expression. " +
                "Set emotion to one allowed portrait image name. " +
                "If no special event matters, prefer continuing the schedule.";
        }

        public static string BuildDecisionSchema()
        {
            return
                "{" +
                "\"type\":\"object\"," +
                "\"additionalProperties\":false," +
                "\"properties\":{" +
                "\"intent\":{\"type\":\"string\",\"enum\":[\"ContinueCurrentAction\",\"TalkToPlayer\",\"TalkToNpc\",\"MoveToLocation\",\"WorkAtLocation\",\"RestAtLocation\",\"ReactToEvent\",\"AvoidActor\",\"JoinFestival\",\"SelfTalk\",\"AttendActivity\",\"FindActor\",\"FollowActor\"]}," +
                "\"behaviorMode\":{\"type\":\"string\",\"enum\":[\"FollowSchedule\",\"Socialize\",\"Work\",\"Rest\",\"Investigate\",\"Avoid\",\"Celebrate\",\"OppositeDay\"]}," +
                "\"tone\":{\"type\":\"string\"}," +
                "\"dialogue\":{\"type\":\"string\"}," +
                "\"emotion\":{\"type\":\"string\",\"description\":\"One allowed portrait image name from this NPC's Resources/UI/Npc/{npc}/portraits folder, without file extension or path.\"}," +
                "\"nextActionPreference\":{\"type\":\"string\"}," +
                "\"originalGoal\":{\"type\":\"string\",\"description\":\"Stable root goal for the current multi-step matter. Empty only when there is no active matter beyond routine/schedule.\"}," +
                "\"currentGoal\":{\"type\":\"string\",\"description\":\"Current stage goal for this single next action. This may change after every task result.\"}," +
                "\"goalStatus\":{\"type\":\"string\",\"enum\":[\"none\",\"active\",\"completed\",\"abandoned\"],\"description\":\"Status of originalGoal after this decision.\"}," +
                "\"goalStatusReason\":{\"type\":\"string\",\"description\":\"Short private reason when goalStatus is completed or abandoned, otherwise empty or brief.\"}," +
                "\"nextSpeakerId\":{\"type\":\"string\",\"description\":\"For group dialogue: empty string, or one actor id from Allowed actors who should naturally speak next.\"}," +
                "\"secondaryEventQuery\":{\"type\":\"string\",\"description\":\"Empty string unless this decision needs a one-time lookup of minor secondary events. Use exact ids such as actorId=<actor_id> locationId=<location_id> eventType=location_entered when possible.\"}," +
                "\"eventKind\":{\"type\":\"string\",\"enum\":[\"None\",\"OneShot\",\"ScheduleOverride\"]}," +
                "\"targetLocationId\":{\"type\":\"string\",\"description\":\"Empty string, or one exact locationId from the Allowed locations list. Location-based intents must be backed by that location's listed task templates.\"}," +
                "\"targetActorId\":{\"type\":\"string\",\"description\":\"Empty string, player, or one npcId from the Allowed actors list in the prompt.\"}," +
                "\"plannedTargetLocationId\":{\"type\":\"string\",\"description\":\"Empty string, or one exact locationId from Allowed locations for the final venue of a shared social plan. This is separate from the immediate action targetLocationId and must support the activity.\"}," +
                "\"activityKind\":{\"type\":\"string\",\"description\":\"Empty unless this decision creates or joins a location-defined activity. Copy the activityKind from the selected location's listed task template, or use the matching templateId only when activityKind is unavailable.\"}," +
                "\"participantActorIds\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"NPC ids involved in a shared activity. May include this NPC's own id. Player appears only when runtime UI explicitly needs it.\"}," +
                "\"requiredActorIds\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"NPC ids whose absence should make the shared activity wait or fail.\"}," +
                "\"optionalActorIds\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"NPC ids who may join late but are not required.\"}," +
                "\"patienceMinutes\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":240,\"description\":\"How long participants should wait after the shared activity start time before treating missing required people as absent.\"}," +
                "\"timingMode\":{\"type\":\"string\",\"enum\":[\"Immediate\",\"DelayMinutes\",\"TodayAtTime\",\"NextDayAtTime\"],\"description\":\"Immediate runs now. DelayMinutes uses delayMinutes. TodayAtTime uses scheduledStartHour/scheduledStartMinute on the current date. NextDayAtTime uses scheduledStartHour/scheduledStartMinute on the next date.\"}," +
                "\"delayMinutes\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":1440,\"description\":\"Used only when timingMode is DelayMinutes.\"}," +
                "\"scheduledStartHour\":{\"type\":\"integer\",\"minimum\":-1,\"maximum\":23,\"description\":\"Used only when timingMode is TodayAtTime or NextDayAtTime.\"}," +
                "\"scheduledStartMinute\":{\"type\":\"integer\",\"minimum\":-1,\"maximum\":59,\"description\":\"Used only when timingMode is TodayAtTime or NextDayAtTime.\"}," +
                "\"socialPlanChanges\":{\"type\":\"array\",\"items\":{" +
                "\"type\":\"object\"," +
                "\"additionalProperties\":false," +
                "\"properties\":{" +
                "\"operation\":{\"type\":\"string\",\"enum\":[\"add_or_update\",\"remove\",\"cancel\",\"complete\"],\"description\":\"Add/update, remove/cancel, or complete a persistent shared social plan. Empty socialPlanChanges means no shared plan change.\"}," +
                "\"planId\":{\"type\":\"string\",\"description\":\"Existing plan id when updating/removing/completing; empty when creating a new plan.\"}," +
                "\"label\":{\"type\":\"string\",\"description\":\"Short private label for the shared plan.\"}," +
                "\"activityKind\":{\"type\":\"string\",\"description\":\"Location-defined activity kind. Copy from the final venue's listed task templates; do not invent or infer it from text.\"}," +
                "\"targetLocationId\":{\"type\":\"string\",\"description\":\"Final venue exact locationId from Allowed locations. The venue must expose a task template supporting this activityKind.\"}," +
                "\"organizerActorId\":{\"type\":\"string\",\"description\":\"NPC id coordinating the plan; empty means this NPC.\"}," +
                "\"participantActorIds\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"All intended participants. May include player.\"}," +
                "\"requiredActorIds\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"Actors whose absence changes or blocks the plan. May include player only if the plan truly requires the player.\"}," +
                "\"optionalActorIds\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"Actors who may join but are not required.\"}," +
                "\"acceptedActorIds\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"Actors who explicitly agreed or are already committed. The speaking NPC and player can be accepted when the dialogue clearly agreed.\"}," +
                "\"pendingActorIds\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"Actors who still need to be found, invited, or confirmed.\"}," +
                "\"declinedActorIds\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"Actors who refused or are unavailable.\"}," +
                "\"patienceMinutes\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":240}," +
                "\"priority\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":100}," +
                "\"timingMode\":{\"type\":\"string\",\"enum\":[\"Immediate\",\"DelayMinutes\",\"TodayAtTime\",\"NextDayAtTime\"]}," +
                "\"delayMinutes\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":1440}," +
                "\"scheduledStartHour\":{\"type\":\"integer\",\"minimum\":-1,\"maximum\":23}," +
                "\"scheduledStartMinute\":{\"type\":\"integer\",\"minimum\":-1,\"maximum\":59}," +
                "\"reason\":{\"type\":\"string\",\"description\":\"Private reason/evidence for this social plan change.\"}" +
                "}," +
                "\"required\":[\"operation\",\"planId\",\"label\",\"activityKind\",\"targetLocationId\",\"organizerActorId\",\"participantActorIds\",\"requiredActorIds\",\"optionalActorIds\",\"acceptedActorIds\",\"pendingActorIds\",\"declinedActorIds\",\"patienceMinutes\",\"priority\",\"timingMode\",\"delayMinutes\",\"scheduledStartHour\",\"scheduledStartMinute\",\"reason\"]" +
                "}}," +
                "\"pendingEncounterChanges\":{\"type\":\"array\",\"items\":{" +
                "\"type\":\"object\"," +
                "\"additionalProperties\":false," +
                "\"properties\":{" +
                "\"operation\":{\"type\":\"string\",\"enum\":[\"add_or_update\",\"remove\"],\"description\":\"Add/update a future opportunity trigger, or remove an existing one. Empty pendingEncounterChanges means no change.\"}," +
                "\"targetActorId\":{\"type\":\"string\",\"description\":\"Exact actor id this future encounter is about. Required for both add_or_update and remove.\"}," +
                "\"actionKind\":{\"type\":\"string\",\"description\":\"Short action category such as ask, invite, warn, apologize, check, deliver_message, thank, avoid, follow_up. Empty is allowed for broad removal.\"}," +
                "\"topic\":{\"type\":\"string\",\"description\":\"Concise topic or condition for the encounter. Empty is allowed for broad removal.\"}," +
                "\"priority\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":100}," +
                "\"reason\":{\"type\":\"string\"}," +
                "\"consumeOnTrigger\":{\"type\":\"boolean\",\"description\":\"True when acting on this opportunity should normally remove it; false when it should keep recurring until explicitly removed.\"}," +
                "\"expiresAfterDays\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":365,\"description\":\"0 means no automatic day-based expiration.\"}," +
                "\"interruptPolicy\":{\"type\":\"string\",\"enum\":[\"only_if_free\",\"can_interrupt_leisure\",\"can_interrupt_anything\"]}," +
                "\"cooldownMinutes\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":1440}" +
                "}," +
                "\"required\":[\"operation\",\"targetActorId\",\"actionKind\",\"topic\",\"priority\",\"reason\",\"consumeOnTrigger\",\"expiresAfterDays\",\"interruptPolicy\",\"cooldownMinutes\"]" +
                "}}," +
                "\"postConversationAction\":{" +
                "\"type\":\"object\"," +
                "\"additionalProperties\":false," +
                "\"properties\":{" +
                "\"hasAction\":{\"type\":\"boolean\",\"description\":\"For dialogue modes: true only if this reply creates a concrete immediate executable action to run as soon as the current dialogue closes. Use false for delayed or future agreements handled by socialPlanChanges.\"}," +
                "\"intent\":{\"type\":\"string\",\"enum\":[\"ContinueCurrentAction\",\"TalkToPlayer\",\"TalkToNpc\",\"MoveToLocation\",\"WorkAtLocation\",\"RestAtLocation\",\"ReactToEvent\",\"AvoidActor\",\"JoinFestival\",\"FindActor\",\"FollowActor\"],\"description\":\"The executable after-dialogue intent when hasAction is true. ContinueCurrentAction pairs with hasAction=false. Shared activities use socialPlanChanges, not postConversationAction.\"}," +
                "\"eventKind\":{\"type\":\"string\",\"enum\":[\"None\",\"OneShot\",\"ScheduleOverride\"]}," +
                "\"targetLocationId\":{\"type\":\"string\",\"description\":\"Empty string, or one exact locationId from the Allowed locations list. Location-based after-dialogue actions must be backed by that location's listed task templates.\"}," +
                "\"targetActorId\":{\"type\":\"string\",\"description\":\"Empty string, player, or one npcId from the Allowed actors list.\"}," +
                "\"plannedTargetLocationId\":{\"type\":\"string\",\"description\":\"Empty string, or one exact locationId from Allowed locations for the final venue of a shared social plan. It must support the planned activity.\"}," +
                "\"activityKind\":{\"type\":\"string\",\"description\":\"Usually empty for postConversationAction. Shared location-defined activities belong in socialPlanChanges.\"}," +
                "\"participantActorIds\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}," +
                "\"requiredActorIds\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}," +
                "\"optionalActorIds\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}," +
                "\"patienceMinutes\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":240}," +
                "\"timingMode\":{\"type\":\"string\",\"enum\":[\"Immediate\",\"DelayMinutes\",\"TodayAtTime\",\"NextDayAtTime\"]}," +
                "\"delayMinutes\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":1440}," +
                "\"scheduledStartHour\":{\"type\":\"integer\",\"minimum\":-1,\"maximum\":23}," +
                "\"scheduledStartMinute\":{\"type\":\"integer\",\"minimum\":-1,\"maximum\":59}," +
                "\"reason\":{\"type\":\"string\",\"description\":\"Short private reason for this after-dialogue action. This is not spoken dialogue.\"}" +
                "}," +
                "\"required\":[\"hasAction\",\"intent\",\"eventKind\",\"targetLocationId\",\"targetActorId\",\"plannedTargetLocationId\",\"activityKind\",\"participantActorIds\",\"requiredActorIds\",\"optionalActorIds\",\"patienceMinutes\",\"timingMode\",\"delayMinutes\",\"scheduledStartHour\",\"scheduledStartMinute\",\"reason\"]" +
                "}," +
                "\"relationshipDeltaHint\":{\"type\":\"integer\",\"minimum\":-2,\"maximum\":2}," +
                "\"confidence\":{\"type\":\"number\",\"minimum\":0,\"maximum\":1}" +
                "}," +
                "\"required\":[\"intent\",\"behaviorMode\",\"tone\",\"dialogue\",\"emotion\",\"nextActionPreference\",\"originalGoal\",\"currentGoal\",\"goalStatus\",\"goalStatusReason\",\"nextSpeakerId\",\"secondaryEventQuery\",\"eventKind\",\"targetLocationId\",\"targetActorId\",\"plannedTargetLocationId\",\"activityKind\",\"participantActorIds\",\"requiredActorIds\",\"optionalActorIds\",\"patienceMinutes\",\"timingMode\",\"delayMinutes\",\"scheduledStartHour\",\"scheduledStartMinute\",\"socialPlanChanges\",\"pendingEncounterChanges\",\"postConversationAction\",\"relationshipDeltaHint\",\"confidence\"]" +
                "}";
        }

        public static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder(value.Length + 16);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        builder.Append(c);
                        break;
                }
            }

            return builder.ToString();
        }

        private static string BuildPortraitNameList(NpcAiRequest request)
        {
            string[] names = NpcPortraitCatalog.GetPortraitNames(request != null ? request.npcId : string.Empty, request != null ? request.npcName : string.Empty);
            if (names == null || names.Length == 0)
            {
                return NpcPortraitCatalog.GetFallbackPortraitName(request != null ? request.npcId : string.Empty, request != null ? request.npcName : string.Empty);
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < names.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(names[i]);
            }

            return builder.ToString();
        }

        private static string BuildSecondaryEventAccessSummary(NpcAiRequest request)
        {
            if (request == null || !request.secondaryEventLookupAvailable)
            {
                return "(unavailable; leave secondaryEventQuery empty)";
            }

            return string.IsNullOrWhiteSpace(request.secondaryEventAccessSummary)
                ? "(available, but no access detail was provided)"
                : request.secondaryEventAccessSummary;
        }

        private static string BuildLocationLine(string locationId, string displayName)
        {
            if (string.IsNullOrWhiteSpace(locationId) && string.IsNullOrWhiteSpace(displayName))
            {
                return "(unknown)";
            }

            if (string.IsNullOrWhiteSpace(locationId))
            {
                return displayName;
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                return $"id={locationId}";
            }

            return $"id={locationId}, name={displayName}";
        }

        private static string BuildFactSummary(NpcAiRequest request)
        {
            return request == null || string.IsNullOrWhiteSpace(request.factSummary)
                ? "(none)"
                : request.factSummary;
        }

        private static string BuildRollingGoalSummary(NpcAiRequest request)
        {
            return request == null || string.IsNullOrWhiteSpace(request.rollingGoalSummary)
                ? "(none)"
                : request.rollingGoalSummary;
        }

        private static string BuildSameDayPlayerDialogueTranscript(NpcAiRequest request)
        {
            return request == null || string.IsNullOrWhiteSpace(request.sameDayPlayerDialogueTranscript)
                ? "(none)"
                : request.sameDayPlayerDialogueTranscript;
        }

        private static string BuildSecondaryEventLookupResultSummary(NpcAiRequest request)
        {
            if (request == null || !request.secondaryEventLookupAlreadyResolved)
            {
                return "(not requested yet)";
            }

            string query = string.IsNullOrWhiteSpace(request.secondaryEventLookupQuery)
                ? "(empty query)"
                : request.secondaryEventLookupQuery;
            string result = string.IsNullOrWhiteSpace(request.secondaryEventLookupResultSummary)
                ? "(no result)"
                : request.secondaryEventLookupResultSummary;
            return $"query={query}\n{result}";
        }
    }
}
