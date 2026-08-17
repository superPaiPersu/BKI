using System;
using System.Collections.Generic;
using System.Text;
using CityStateSim.Behavior;
using CityStateSim.Core;
using CityStateSim.Dialogue;
using CityStateSim.Locations;
using CityStateSim.Memory;
using CityStateSim.NPC;
using CityStateSim.Tasks;
using UnityEngine;

namespace CityStateSim.Activities
{
    public sealed class NpcActivityParticipantInfo
    {
        public NpcActivityParticipantInfo(
            NpcRuntimeState npc,
            LocationDefinition location,
            string activityKind,
            string sessionKey,
            GameDate startDate,
            int startMinute,
            int durationMinutes,
            int remainingMinutes)
        {
            Npc = npc;
            Location = location;
            ActivityKind = activityKind;
            SessionKey = sessionKey;
            StartDate = startDate;
            StartMinute = startMinute;
            DurationMinutes = durationMinutes;
            RemainingMinutes = remainingMinutes;
        }

        public NpcRuntimeState Npc { get; }
        public LocationDefinition Location { get; }
        public string ActivityKind { get; }
        public string SessionKey { get; }
        public GameDate StartDate { get; }
        public int StartMinute { get; }
        public int DurationMinutes { get; }
        public int RemainingMinutes { get; }
    }

    public sealed class NpcActivitySystem : MonoBehaviour
    {
        private const string ActivityPauseReason = "npc_activity";

        [Header("References")]
        [SerializeField] private GameClock clock;
        [SerializeField] private ActivitySpotSystem activitySpotSystem;
        [SerializeField] private ConversationArbiter conversationArbiter;
        [SerializeField] private MemorySystem memorySystem;

        [Header("Policy")]
        [SerializeField, Min(2)] private int minimumNpcParticipantsToStart = 2;
        [SerializeField, Min(0)] private int defaultPatienceMinutes = 20;
        [SerializeField, Min(0.1f)] private float nearbyActivityDistance = 3f;
        [SerializeField, Min(0.05f)] private float gatherReachDistance = 0.25f;
        [SerializeField] private bool logDebug = true;

        private readonly Dictionary<string, SocialActivitySession> sessionsByKey = new Dictionary<string, SocialActivitySession>();
        private readonly Dictionary<NpcRuntimeState, SocialActivitySession> sessionsByNpc = new Dictionary<NpcRuntimeState, SocialActivitySession>();
        private readonly HashSet<string> completedActivityKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void Awake()
        {
            if (clock == null)
            {
                clock = FindFirstObjectByType<GameClock>();
            }

            if (activitySpotSystem == null)
            {
                activitySpotSystem = FindFirstObjectByType<ActivitySpotSystem>();
            }

            if (conversationArbiter == null)
            {
                conversationArbiter = ConversationArbiter.GetOrCreate();
            }

            if (memorySystem == null)
            {
                memorySystem = FindFirstObjectByType<MemorySystem>();
            }
        }

        private void OnEnable()
        {
            if (clock != null)
            {
                clock.DayChanged += HandleDayChanged;
            }
        }

        private void OnDisable()
        {
            if (clock != null)
            {
                clock.DayChanged -= HandleDayChanged;
            }
        }

        private void Update()
        {
            if (sessionsByKey.Count == 0)
            {
                return;
            }

            List<SocialActivitySession> sessions = new List<SocialActivitySession>(sessionsByKey.Values);
            for (int i = 0; i < sessions.Count; i++)
            {
                SocialActivitySession session = sessions[i];
                if (session == null || session.Completed)
                {
                    continue;
                }

                if (session.Phase == SocialActivityPhase.PerformingActivity)
                {
                    UpdatePerformingActivity(session);
                    continue;
                }

                CollectNearbyExpectedParticipants(session);
                UpdateParticipantGathering(session);
                if (TryStartReadyActivity(session, out _, out _))
                {
                    continue;
                }

                if (IsWaitExpired(session))
                {
                    CompleteWaitingSession(session, "activity wait expired");
                }
            }
        }

        public bool IsNpcInActivity(NpcRuntimeState npc)
        {
            return npc != null && sessionsByNpc.ContainsKey(npc);
        }

        public NpcActivityParticipantInfo[] GetActiveParticipants(LocationDefinition location, string activityKind = "")
        {
            List<NpcActivityParticipantInfo> result = new List<NpcActivityParticipantInfo>();
            foreach (SocialActivitySession session in sessionsByKey.Values)
            {
                if (!IsMatchingPerformingSession(session, location, activityKind))
                {
                    continue;
                }

                NpcRuntimeState[] participants = session.GetParticipants();
                int remainingMinutes = GetRemainingActivityMinutes(session);
                for (int i = 0; i < participants.Length; i++)
                {
                    NpcRuntimeState participant = participants[i];
                    if (participant == null)
                    {
                        continue;
                    }

                    result.Add(new NpcActivityParticipantInfo(
                        participant,
                        session.Location,
                        session.ActivityKind,
                        session.Key,
                        session.ActivityStartDate,
                        session.ActivityStartMinute,
                        session.ActivityDurationMinutes,
                        remainingMinutes));
                }
            }

            return result.ToArray();
        }

        public NpcRuntimeState[] GetActiveParticipantNpcs(LocationDefinition location, string activityKind = "")
        {
            NpcActivityParticipantInfo[] infos = GetActiveParticipants(location, activityKind);
            NpcRuntimeState[] npcs = new NpcRuntimeState[infos.Length];
            for (int i = 0; i < infos.Length; i++)
            {
                npcs[i] = infos[i].Npc;
            }

            return npcs;
        }

        public bool TryGetParticipantActivityInfo(NpcRuntimeState npc, out NpcActivityParticipantInfo info)
        {
            info = null;
            if (npc == null || !sessionsByNpc.TryGetValue(npc, out SocialActivitySession session))
            {
                return false;
            }

            if (session == null || session.Completed || session.Phase != SocialActivityPhase.PerformingActivity)
            {
                return false;
            }

            info = new NpcActivityParticipantInfo(
                npc,
                session.Location,
                session.ActivityKind,
                session.Key,
                session.ActivityStartDate,
                session.ActivityStartMinute,
                session.ActivityDurationMinutes,
                GetRemainingActivityMinutes(session));
            return true;
        }

        public NpcActivityJoinResult TryJoinActivity(NpcRuntimeState npc, NpcTask task, out string reason)
        {
            reason = string.Empty;
            if (npc == null || task == null || task.Kind != NpcTaskKind.AttendActivity)
            {
                reason = "invalid activity request";
                return NpcActivityJoinResult.Rejected;
            }

            if (task.TargetLocation == null)
            {
                reason = "activity has no target location";
                return NpcActivityJoinResult.Rejected;
            }

            if (!NpcTaskConstraintValidator.ValidateAtExecutionPoint(task, out string constraintFailure))
            {
                reason = constraintFailure;
                return NpcActivityJoinResult.Rejected;
            }

            string key = BuildSessionKey(task);
            string[] requestedParticipantIds = CleanNpcIds(task.ParticipantActorIds);
            if (conversationArbiter != null
                && conversationArbiter.TryJoinActivityConversation(npc, key, out ConversationArbiter.ConversationStartFailureReason lateJoinFailure))
            {
                ClearActivityTask(npc, key, "joined active activity conversation");
                reason = "joined active activity conversation";
                return NpcActivityJoinResult.StartedConversation;
            }

            if (conversationArbiter != null
                && conversationArbiter.TryJoinCompatibleActivityConversation(npc, requestedParticipantIds, out lateJoinFailure))
            {
                ClearActivityTask(npc, key, "joined compatible active activity conversation");
                reason = "joined compatible active activity conversation";
                return NpcActivityJoinResult.StartedConversation;
            }

            if (completedActivityKeys.Contains(key))
            {
                reason = "activity already resolved today";
                return NpcActivityJoinResult.Rejected;
            }

            if (sessionsByNpc.TryGetValue(npc, out SocialActivitySession existingForNpc))
            {
                if (string.Equals(existingForNpc.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    reason = BuildWaitingReason(existingForNpc);
                    return NpcActivityJoinResult.Waiting;
                }

                reason = "npc is already waiting in another activity";
                return NpcActivityJoinResult.Rejected;
            }

            if (conversationArbiter != null && conversationArbiter.IsNpcInConversation(npc))
            {
                reason = "npc is already in conversation";
                return NpcActivityJoinResult.Rejected;
            }

            if (!sessionsByKey.TryGetValue(key, out SocialActivitySession session))
            {
                session = FindCompatibleWaitingSession(npc, task.TargetLocation, requestedParticipantIds);
                if (session != null)
                {
                    key = session.Key;
                }
            }

            if (session == null)
            {
                session = CreateSession(key, task);
                sessionsByKey[key] = session;
            }

            AddParticipant(session, npc);
            CollectNearbyExpectedParticipants(session);
            UpdateParticipantGathering(session);

            if (TryStartReadyActivity(session, out reason, out NpcActivityJoinResult startResult))
            {
                return startResult;
            }

            if (IsWaitExpired(session))
            {
                CompleteWaitingSession(session, "activity wait expired immediately");
                reason = "activity wait expired before enough participants arrived";
                return NpcActivityJoinResult.Rejected;
            }

            reason = BuildWaitingReason(session);
            if (logDebug)
            {
                Debug.Log($"[NPC Activity] {GetName(npc)} waiting. key={key}, {reason}", this);
            }

            return NpcActivityJoinResult.Waiting;
        }

        private SocialActivitySession CreateSession(string key, NpcTask task)
        {
            int currentMinute = clock != null ? clock.CurrentTime.TotalMinutes : 0;
            GameDate date = clock != null ? clock.CurrentDate : new GameDate(1, 1, 1);
            int patience = task.PatienceMinutes > 0 ? task.PatienceMinutes : defaultPatienceMinutes;
            string[] participants = CleanNpcIds(task.ParticipantActorIds);
            string[] required = CleanNpcIds(task.RequiredActorIds);
            string[] optional = CleanNpcIds(task.OptionalActorIds);
            if (required.Length == 0)
            {
                required = participants;
            }

            LocationTaskTemplate template = null;
            task.TargetLocation?.TryGetAvailableTaskTemplate(
                task.ActivityKind,
                NpcTaskKind.AttendActivity.ToString(),
                out template);
            int durationMinutes = template != null ? template.ActivityDurationMinutes : 0;
            bool startConversationAfterActivity = template == null || template.StartConversationAfterActivity;
            bool requiresGroup = template != null && template.RequiresGroup;

            return new SocialActivitySession(
                key,
                task.Label,
                task.ActivityKind,
                task.TargetLocation,
                task.Priority,
                task.Reason,
                task.Dialogue,
                participants,
                required,
                optional,
                patience,
                durationMinutes,
                startConversationAfterActivity,
                requiresGroup,
                date,
                currentMinute);
        }

        private void AddParticipant(SocialActivitySession session, NpcRuntimeState npc)
        {
            if (session == null || npc == null || session.Contains(npc))
            {
                return;
            }

            session.AddParticipant(npc);
            sessionsByNpc[npc] = session;

            MoveParticipantTowardGatherPoint(session, npc);

            FaceActivityCenter(session);
        }

        private void CollectNearbyExpectedParticipants(SocialActivitySession session)
        {
            if (session == null)
            {
                return;
            }

            string[] expected = session.ExpectedActorIds;
            for (int i = 0; i < expected.Length; i++)
            {
                NpcRuntimeState npc = FindNpcById(expected[i]);
                if (npc == null || session.Contains(npc) || !IsAtActivityLocation(npc, session.Location))
                {
                    continue;
                }

                NpcBehaviorController controller = npc.GetComponent<NpcBehaviorController>();
                if (controller != null && controller.RequestInFlight)
                {
                    continue;
                }

                if (conversationArbiter != null && conversationArbiter.IsNpcInConversation(npc))
                {
                    continue;
                }

                AddParticipant(session, npc);
            }
        }

        private SocialActivitySession FindCompatibleWaitingSession(
            NpcRuntimeState npc,
            LocationDefinition location,
            string[] requestedParticipantIds)
        {
            if (npc == null || location == null)
            {
                return null;
            }

            string selfId = npc.Profile != null ? npc.Profile.NpcId : string.Empty;
            foreach (SocialActivitySession session in sessionsByKey.Values)
            {
                if (session == null || session.Completed || session.Location != location)
                {
                    continue;
                }

                if (session.ExpectsActorId(selfId)
                    || session.HasExpectedActorOverlap(requestedParticipantIds)
                    || session.HasArrivedActorOverlap(requestedParticipantIds))
                {
                    return session;
                }
            }

            return null;
        }

        private bool TryStartReadyActivity(
            SocialActivitySession session,
            out string reason,
            out NpcActivityJoinResult result)
        {
            reason = string.Empty;
            result = NpcActivityJoinResult.Waiting;
            if (session == null || session.Completed || !HasEnoughParticipants(session))
            {
                reason = session != null ? BuildWaitingReason(session) : "missing activity session";
                return false;
            }

            bool missingRequired = !HasAllRequiredParticipants(session);
            if (missingRequired && !IsWaitExpired(session))
            {
                reason = BuildWaitingReason(session);
                return false;
            }

            NpcRuntimeState[] participants = session.GetParticipants();
            if (session.ParticipantCount > 1 && !AreParticipantsInSpeakingRange(participants))
            {
                reason = "waiting for participants to gather within speaking range";
                if (IsWaitExpired(session))
                {
                    CompleteWaitingSession(session, "failed: participants did not gather within speaking range");
                }

                return false;
            }

            if (session.ActivityDurationMinutes > 0)
            {
                BeginTimedActivity(session, missingRequired);
                reason = $"activity started. duration={session.ActivityDurationMinutes} minutes";
                return true;
            }

            if (session.ParticipantCount < 2)
            {
                CompleteSuccessfulActivitySession(session, "activity completed without conversation because it has one participant");
                reason = "activity completed without conversation because it has one participant";
                return true;
            }

            return TryStartActivityConversation(session, missingRequired, out reason, out result);
        }

        private bool TryStartActivityConversation(
            SocialActivitySession session,
            bool missingRequired,
            out string reason,
            out NpcActivityJoinResult result)
        {
            reason = string.Empty;
            result = NpcActivityJoinResult.Waiting;
            if (session == null || session.Completed)
            {
                reason = "missing activity session";
                return false;
            }

            if (conversationArbiter == null)
            {
                conversationArbiter = ConversationArbiter.GetOrCreate();
            }

            if (conversationArbiter == null)
            {
                CompleteSuccessfulActivitySession(session, "activity completed without conversation because ConversationArbiter is missing");
                reason = "ConversationArbiter is missing";
                return true;
            }

            NpcRuntimeState[] participants = session.GetParticipants();
            if (!AreParticipantsInSpeakingRange(participants))
            {
                reason = "waiting for participants to gather within speaking range";
                if (session.Phase != SocialActivityPhase.PerformingActivity && IsWaitExpired(session))
                {
                    CompleteWaitingSession(session, "failed: participants did not gather within speaking range");
                }

                return false;
            }

            string conversationReason = BuildConversationReason(session, missingRequired);
            bool started = conversationArbiter.TryStartGroupNow(
                participants,
                session.Label,
                conversationReason,
                session.Priority,
                true,
                session.Key,
                true,
                out ConversationArbiter.ConversationStartFailureReason failureReason);

            if (!started)
            {
                reason = $"conversation could not start: {failureReason}";
                if (session.Phase == SocialActivityPhase.PerformingActivity)
                {
                    CompleteSuccessfulActivitySession(session, "activity completed; " + reason);
                    return true;
                }

                if (IsWaitExpired(session))
                {
                    CompleteWaitingSession(session, "failed: " + reason);
                }

                return false;
            }

            if (logDebug)
            {
                Debug.Log($"[NPC Activity] started conversation. key={session.Key}, participants={BuildParticipantNames(session)}", this);
            }

            CompleteSessionWithoutTaskCompletion(session, "activity conversation handed to arbiter");
            reason = "activity conversation started";
            result = NpcActivityJoinResult.StartedConversation;
            return true;
        }

        private void BeginTimedActivity(SocialActivitySession session, bool startedAfterMissingRequired)
        {
            if (session == null || session.Completed || session.Phase == SocialActivityPhase.PerformingActivity)
            {
                return;
            }

            int currentMinute = clock != null ? clock.CurrentTime.TotalMinutes : 0;
            GameDate date = clock != null ? clock.CurrentDate : session.StartDate;
            session.BeginPerforming(date, currentMinute, startedAfterMissingRequired);

            NpcRuntimeState[] participants = session.GetParticipants();
            for (int i = 0; i < participants.Length; i++)
            {
                NpcRuntimeState npc = participants[i];
                NpcMovementAgent movement = npc != null ? npc.GetComponent<NpcMovementAgent>() : null;
                if (movement != null)
                {
                    movement.Stop();
                    movement.SetPause(ActivityPauseReason, true);
                }

                npc?.EnterInsideActivity(session.Location, session.ActivityKind);
                activitySpotSystem?.ReleaseSpot(npc);
                WriteActivityMemory(
                    npc,
                    $"Started {session.ActivityKind} at {(session.Location != null ? session.Location.LocationId : "(unknown)")}.",
                    "activity_started",
                    5);
            }

            if (logDebug)
            {
                Debug.Log(
                    $"[NPC Activity] performing activity. key={session.Key}, activityKind={session.ActivityKind}, duration={session.ActivityDurationMinutes}m, participants={BuildParticipantNames(session)}",
                    this);
            }
        }

        private void UpdatePerformingActivity(SocialActivitySession session)
        {
            if (session == null || session.Completed || session.Phase != SocialActivityPhase.PerformingActivity)
            {
                return;
            }

            if (!IsActivityDurationComplete(session))
            {
                return;
            }

            if (session.StartConversationAfterActivity && session.ParticipantCount > 1)
            {
                RestoreParticipantsToWorld(session);
                if (TryStartActivityConversation(
                        session,
                        session.StartedAfterMissingRequired,
                        out _,
                        out _))
                {
                    return;
                }
            }

            CompleteSuccessfulActivitySession(
                session,
                $"completed {session.ActivityKind} at {(session.Location != null ? session.Location.LocationId : "(unknown)")}");
        }

        private bool IsActivityDurationComplete(SocialActivitySession session)
        {
            if (session == null)
            {
                return false;
            }

            if (session.ActivityDurationMinutes <= 0)
            {
                return true;
            }

            if (clock == null || !clock.CurrentDate.Equals(session.ActivityStartDate))
            {
                return true;
            }

            int elapsed = clock.CurrentTime.TotalMinutes - session.ActivityStartMinute;
            if (elapsed < 0)
            {
                elapsed += 1440;
            }

            return elapsed >= session.ActivityDurationMinutes;
        }

        private int GetRemainingActivityMinutes(SocialActivitySession session)
        {
            if (session == null || session.ActivityDurationMinutes <= 0)
            {
                return 0;
            }

            if (clock == null || !clock.CurrentDate.Equals(session.ActivityStartDate))
            {
                return 0;
            }

            int elapsed = clock.CurrentTime.TotalMinutes - session.ActivityStartMinute;
            if (elapsed < 0)
            {
                elapsed += 1440;
            }

            return Mathf.Max(0, session.ActivityDurationMinutes - elapsed);
        }

        private void CompleteSuccessfulActivitySession(SocialActivitySession session, string reason)
        {
            if (session == null || session.Completed)
            {
                return;
            }

            string completionReason = string.IsNullOrWhiteSpace(reason)
                ? "activity completed"
                : reason;
            NpcRuntimeState[] participants = session.GetParticipants();
            RemoveSession(session, true);
            completedActivityKeys.Add(session.Key);

            for (int i = 0; i < participants.Length; i++)
            {
                NpcRuntimeState npc = participants[i];
                if (npc == null)
                {
                    continue;
                }

                WriteActivityMemory(npc, completionReason, "activity_completed", 6);
                NpcTaskController taskController = npc.GetComponent<NpcTaskController>();
                NpcTask currentTask = taskController != null ? taskController.CurrentTask : null;
                if (IsActivityTask(currentTask))
                {
                    taskController.CompleteCurrentTask(completionReason);
                }
            }

            if (logDebug)
            {
                Debug.Log($"[NPC Activity] completed activity session. key={session.Key}, reason={completionReason}", this);
            }
        }

        private void CompleteWaitingSession(SocialActivitySession session, string reason)
        {
            if (session == null || session.Completed)
            {
                return;
            }

            string missing = BuildMissingRequiredText(session);
            string completionReason = string.IsNullOrWhiteSpace(missing)
                ? reason
                : $"{reason}; missing required participants: {missing}";
            NpcRuntimeState[] participants = session.GetParticipants();
            RemoveSession(session, true);
            completedActivityKeys.Add(session.Key);

            for (int i = 0; i < participants.Length; i++)
            {
                NpcRuntimeState npc = participants[i];
                if (npc == null)
                {
                    continue;
                }

                WriteActivityMemory(npc, completionReason, "activity_wait_result", 6);
                NpcTaskController taskController = npc.GetComponent<NpcTaskController>();
                NpcTask currentTask = taskController != null ? taskController.CurrentTask : null;
                if (IsActivityTask(currentTask))
                {
                    taskController.CompleteCurrentTask("failed: " + completionReason);
                }
            }

            if (logDebug)
            {
                Debug.Log($"[NPC Activity] completed waiting session. key={session.Key}, reason={completionReason}", this);
            }
        }

        private void CompleteSessionWithoutTaskCompletion(SocialActivitySession session, string reason)
        {
            if (session == null || session.Completed)
            {
                return;
            }

            NpcRuntimeState[] participants = session.GetParticipants();
            RemoveSession(session, true);
            completedActivityKeys.Add(session.Key);

            for (int i = 0; i < participants.Length; i++)
            {
                ClearActivityTask(participants[i], string.Empty, reason);
            }
        }

        private void RemoveSession(SocialActivitySession session, bool restoreMovement)
        {
            if (session == null)
            {
                return;
            }

            session.Completed = true;
            sessionsByKey.Remove(session.Key);
            NpcRuntimeState[] participants = session.GetParticipants();
            for (int i = 0; i < participants.Length; i++)
            {
                NpcRuntimeState npc = participants[i];
                RestoreNpcWorldPresence(npc, session.Location);
                activitySpotSystem?.ReleaseSpot(npc);
                if (npc != null && sessionsByNpc.TryGetValue(npc, out SocialActivitySession mapped) && mapped == session)
                {
                    sessionsByNpc.Remove(npc);
                }

                if (restoreMovement)
                {
                    NpcMovementAgent movement = npc != null ? npc.GetComponent<NpcMovementAgent>() : null;
                    movement?.SetPause(ActivityPauseReason, false);
                }
            }
        }

        private void RestoreParticipantsToWorld(SocialActivitySession session)
        {
            if (session == null)
            {
                return;
            }

            NpcRuntimeState[] participants = session.GetParticipants();
            for (int i = 0; i < participants.Length; i++)
            {
                RestoreNpcWorldPresence(participants[i], session.Location);
            }

            FaceActivityCenter(session);
        }

        private void RestoreNpcWorldPresence(NpcRuntimeState npc, LocationDefinition location)
        {
            if (npc == null || npc.PresenceMode != NpcPresenceMode.InsideActivity)
            {
                return;
            }

            if (TryGetActivityAnchor(location, out Vector3 anchor))
            {
                Rigidbody2D body = npc.GetComponent<Rigidbody2D>();
                if (body != null)
                {
                    body.position = anchor;
                    body.linearVelocity = Vector2.zero;
                }

                npc.transform.position = anchor;
            }

            npc.ExitInsideActivity();
        }

        private void ClearActivityTask(NpcRuntimeState npc, string key, string reason)
        {
            NpcTaskController taskController = npc != null ? npc.GetComponent<NpcTaskController>() : null;
            NpcTask currentTask = taskController != null ? taskController.CurrentTask : null;
            if (IsActivityTask(currentTask, key))
            {
                taskController.ClearCurrentTask(reason);
            }
        }

        private void WriteActivityMemory(NpcRuntimeState npc, string text, string tag, int importance)
        {
            if (npc == null || npc.Profile == null || memorySystem == null || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            memorySystem.AddMemory(npc.Profile.NpcId, $"Activity '{text}'", tag, importance);
        }

        private bool HasEnoughParticipants(SocialActivitySession session)
        {
            if (session == null)
            {
                return false;
            }

            int minimum = session.RequiresGroup ? Mathf.Max(2, minimumNpcParticipantsToStart) : 1;
            return session.ParticipantCount >= minimum;
        }

        private bool HasAllRequiredParticipants(SocialActivitySession session)
        {
            if (session == null)
            {
                return false;
            }

            string[] required = session.RequiredActorIds;
            if (required.Length == 0)
            {
                return HasEnoughParticipants(session);
            }

            bool hasRequiredNpc = false;
            for (int i = 0; i < required.Length; i++)
            {
                string id = required[i];
                if (IsPlayerId(id))
                {
                    continue;
                }

                hasRequiredNpc = true;
                if (!session.ContainsActorId(id))
                {
                    return false;
                }
            }

            return hasRequiredNpc ? HasEnoughParticipants(session) : HasEnoughParticipants(session);
        }

        private bool IsWaitExpired(SocialActivitySession session)
        {
            if (session == null)
            {
                return false;
            }

            int patience = session.PatienceMinutes;
            if (patience <= 0)
            {
                return true;
            }

            if (clock == null || !clock.CurrentDate.Equals(session.StartDate))
            {
                return true;
            }

            int elapsed = clock.CurrentTime.TotalMinutes - session.StartMinute;
            if (elapsed < 0)
            {
                elapsed += 1440;
            }

            return elapsed >= patience;
        }

        private bool IsAtActivityLocation(NpcRuntimeState npc, LocationDefinition location)
        {
            if (npc == null || location == null)
            {
                return false;
            }

            if (activitySpotSystem != null
                && activitySpotSystem.TryGetSpot(npc, out ActivitySpot occupiedSpot)
                && occupiedSpot != null
                && occupiedSpot.LocationMarker != null
                && occupiedSpot.LocationMarker.Definition == location
                && Vector2.Distance(npc.transform.position, occupiedSpot.GetUsePosition()) <= nearbyActivityDistance)
            {
                return true;
            }

            if (TryGetActivityAnchor(location, out Vector3 anchor))
            {
                return Vector2.Distance(npc.transform.position, anchor) <= nearbyActivityDistance;
            }

            return false;
        }

        private void UpdateParticipantGathering(SocialActivitySession session)
        {
            if (session == null || session.Completed)
            {
                return;
            }

            NpcRuntimeState[] participants = session.GetParticipants();
            for (int i = 0; i < participants.Length; i++)
            {
                MoveParticipantTowardGatherPoint(session, participants[i]);
            }

            FaceActivityCenter(session);
        }

        private void MoveParticipantTowardGatherPoint(SocialActivitySession session, NpcRuntimeState npc)
        {
            if (session == null || npc == null)
            {
                return;
            }

            NpcMovementAgent movement = npc.GetComponent<NpcMovementAgent>();
            if (movement == null)
            {
                return;
            }

            Vector3 targetPosition;
            Vector3 facePosition;
            ResolveGatherPoint(session, npc, out targetPosition, out facePosition);

            if (Vector2.Distance(npc.transform.position, targetPosition) <= gatherReachDistance)
            {
                movement.Stop();
                movement.SetPause(ActivityPauseReason, true);
                movement.Face(facePosition);
                return;
            }

            movement.SetPause(ActivityPauseReason, false);
            if (!movement.HasTarget || Vector2.Distance(movement.TargetPosition, targetPosition) > gatherReachDistance)
            {
                movement.MoveTo(targetPosition);
            }

            movement.Face(facePosition);
        }

        private void ResolveGatherPoint(SocialActivitySession session, NpcRuntimeState npc, out Vector3 targetPosition, out Vector3 facePosition)
        {
            targetPosition = npc != null ? npc.transform.position : Vector3.zero;
            facePosition = targetPosition;
            if (session == null || npc == null)
            {
                return;
            }

            Vector3 anchor = GetGatherAnchor(session, npc);
            float speakingDistance = GetGroupConversationStartDistance();
            if (activitySpotSystem != null)
            {
                ActivitySpot currentSpot;
                bool canKeepCurrentSpot = activitySpotSystem.TryGetSpot(npc, out currentSpot)
                    && currentSpot != null
                    && currentSpot.LocationMarker != null
                    && currentSpot.LocationMarker.Definition == session.Location
                    && (session.ParticipantCount <= 1
                        || Vector2.Distance(currentSpot.GetUsePosition(), anchor) <= speakingDistance);

                ActivitySpot spot = currentSpot;
                if (!canKeepCurrentSpot
                    && activitySpotSystem.TryAssignSpotNear(npc, session.Location, anchor, speakingDistance, out spot))
                {
                    currentSpot = spot;
                }
                else if (!canKeepCurrentSpot && currentSpot != null)
                {
                    activitySpotSystem.ReleaseSpot(npc);
                    currentSpot = null;
                }

                if (currentSpot != null)
                {
                    targetPosition = currentSpot.GetUsePosition();
                    facePosition = currentSpot.GetFacePosition();
                    return;
                }
            }

            targetPosition = BuildFallbackGatherPosition(session, npc, anchor);
            facePosition = anchor;
        }

        private Vector3 GetGatherAnchor(SocialActivitySession session, NpcRuntimeState npc)
        {
            if (session == null)
            {
                return npc != null ? npc.transform.position : Vector3.zero;
            }

            NpcRuntimeState[] participants = session.GetParticipants();
            Vector3 sum = Vector3.zero;
            int count = 0;
            for (int i = 0; i < participants.Length; i++)
            {
                NpcRuntimeState other = participants[i];
                if (other == null || other == npc)
                {
                    continue;
                }

                sum += other.transform.position;
                count++;
            }

            if (count > 0)
            {
                return sum / count;
            }

            return TryGetActivityAnchor(session.Location, out Vector3 anchor) ? anchor : npc.transform.position;
        }

        private Vector3 BuildFallbackGatherPosition(SocialActivitySession session, NpcRuntimeState npc, Vector3 anchor)
        {
            int index = GetParticipantIndex(session, npc);
            float radius = Mathf.Min(0.75f, GetGroupConversationStartDistance() * 0.35f);
            float angle = index * 137.5f * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
            return anchor + offset;
        }

        private int GetParticipantIndex(SocialActivitySession session, NpcRuntimeState npc)
        {
            if (session == null || npc == null)
            {
                return 0;
            }

            NpcRuntimeState[] participants = session.GetParticipants();
            for (int i = 0; i < participants.Length; i++)
            {
                if (participants[i] == npc)
                {
                    return i;
                }
            }

            return 0;
        }

        private bool AreParticipantsInSpeakingRange(NpcRuntimeState[] participants)
        {
            if (participants == null || participants.Length < 2)
            {
                return false;
            }

            float speakingDistance = GetGroupConversationStartDistance();
            for (int i = 0; i < participants.Length; i++)
            {
                NpcRuntimeState npc = participants[i];
                if (npc == null)
                {
                    return false;
                }

                bool hasNearbyParticipant = false;
                for (int j = 0; j < participants.Length; j++)
                {
                    NpcRuntimeState other = participants[j];
                    if (other == null || other == npc)
                    {
                        continue;
                    }

                    if (Vector2.Distance(npc.transform.position, other.transform.position) <= speakingDistance)
                    {
                        hasNearbyParticipant = true;
                        break;
                    }
                }

                if (!hasNearbyParticipant)
                {
                    return false;
                }
            }

            return true;
        }

        private float GetGroupConversationStartDistance()
        {
            return conversationArbiter != null ? conversationArbiter.GroupConversationStartDistance : nearbyActivityDistance;
        }

        private bool TryGetActivityAnchor(LocationDefinition location, out Vector3 anchor)
        {
            anchor = Vector3.zero;
            LocationSystem locationSystem = FindFirstObjectByType<LocationSystem>();
            if (locationSystem == null || !locationSystem.TryGetMarker(location, out LocationMarker marker))
            {
                return false;
            }

            anchor = marker.GetEntryPosition();
            return true;
        }

        private void FaceActivityCenter(SocialActivitySession session)
        {
            if (session == null || session.ParticipantCount == 0)
            {
                return;
            }

            Vector3 center = Vector3.zero;
            NpcRuntimeState[] participants = session.GetParticipants();
            for (int i = 0; i < participants.Length; i++)
            {
                center += participants[i].transform.position;
            }

            center /= participants.Length;
            for (int i = 0; i < participants.Length; i++)
            {
                NpcMovementAgent movement = participants[i] != null ? participants[i].GetComponent<NpcMovementAgent>() : null;
                movement?.Face(center);
            }
        }

        private string BuildWaitingReason(SocialActivitySession session)
        {
            if (session == null)
            {
                return "no activity session";
            }

            string missing = BuildMissingRequiredText(session);
            if (string.IsNullOrWhiteSpace(missing))
            {
                return $"waiting for enough participants. arrived={BuildParticipantNames(session)}";
            }

            return $"waiting for required participants: {missing}. arrived={BuildParticipantNames(session)}";
        }

        private string BuildConversationReason(SocialActivitySession session, bool startedAfterMissingRequired)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("Shared activity is starting. ");
            builder.Append("activityKind=");
            builder.Append(session.ActivityKind);
            builder.Append(", locationId=");
            builder.Append(session.Location != null ? session.Location.LocationId : "");
            builder.Append(", arrived=");
            builder.Append(BuildParticipantNames(session));
            builder.Append(". ");

            if (session.Phase == SocialActivityPhase.PerformingActivity)
            {
                builder.Append("The location-defined activity has just run for ");
                builder.Append(session.ActivityDurationMinutes);
                builder.Append(" game minutes before this conversation. ");
            }

            string missing = BuildMissingRequiredText(session);
            if (!string.IsNullOrWhiteSpace(missing))
            {
                builder.Append("Not all required participants arrived before patience expired. Missing required participants: ");
                builder.Append(missing);
                builder.Append(". Treat this as real world feedback and decide whether to continue, wait no longer, search, or leave. ");
            }

            if (!string.IsNullOrWhiteSpace(session.Reason))
            {
                builder.Append("Reason: ");
                builder.Append(session.Reason);
                builder.Append(". ");
            }

            if (!string.IsNullOrWhiteSpace(session.Dialogue))
            {
                builder.Append("Original line/context: ");
                builder.Append(session.Dialogue);
                builder.Append(". ");
            }

            builder.Append("This is a group activity, not a private one-on-one. Waiting has already been handled by the activity system.");
            return builder.ToString();
        }

        private string BuildMissingRequiredText(SocialActivitySession session)
        {
            if (session == null || session.RequiredActorIds.Length == 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < session.RequiredActorIds.Length; i++)
            {
                string id = session.RequiredActorIds[i];
                if (IsPlayerId(id) || session.ContainsActorId(id))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(id);
            }

            return builder.ToString();
        }

        private static string BuildParticipantNames(SocialActivitySession session)
        {
            if (session == null || session.ParticipantCount == 0)
            {
                return "(none)";
            }

            StringBuilder builder = new StringBuilder();
            NpcRuntimeState[] participants = session.GetParticipants();
            for (int i = 0; i < participants.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(GetName(participants[i]));
            }

            return builder.ToString();
        }

        private static bool IsActivityTask(NpcTask task, string key = "")
        {
            return task != null
                && task.Kind == NpcTaskKind.AttendActivity
                && (string.IsNullOrWhiteSpace(key) || string.Equals(task.ActivityKey, key, StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildSessionKey(NpcTask task)
        {
            return task != null && !string.IsNullOrWhiteSpace(task.ActivityKey)
                ? task.ActivityKey
                : Guid.NewGuid().ToString("N");
        }

        private static string[] CleanNpcIds(string[] ids)
        {
            if (ids == null || ids.Length == 0)
            {
                return Array.Empty<string>();
            }

            List<string> cleaned = new List<string>();
            for (int i = 0; i < ids.Length; i++)
            {
                string id = string.IsNullOrWhiteSpace(ids[i]) ? string.Empty : ids[i].Trim();
                if (string.IsNullOrWhiteSpace(id) || IsPlayerId(id) || ContainsId(cleaned, id))
                {
                    continue;
                }

                cleaned.Add(id);
            }

            return cleaned.ToArray();
        }

        private static bool ContainsId(List<string> ids, string id)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                if (string.Equals(ids[i], id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPlayerId(string id)
        {
            return string.Equals(id, "player", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsMatchingPerformingSession(SocialActivitySession session, LocationDefinition location, string activityKind)
        {
            if (session == null || session.Completed || session.Phase != SocialActivityPhase.PerformingActivity)
            {
                return false;
            }

            if (location != null && session.Location != location)
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(activityKind)
                || string.Equals(session.ActivityKind, activityKind.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static NpcRuntimeState FindNpcById(string npcId)
        {
            if (string.IsNullOrWhiteSpace(npcId))
            {
                return null;
            }

            NpcRuntimeState[] npcs = FindObjectsByType<NpcRuntimeState>(FindObjectsSortMode.None);
            for (int i = 0; i < npcs.Length; i++)
            {
                NpcRuntimeState npc = npcs[i];
                if (npc != null && npc.Profile != null && string.Equals(npc.Profile.NpcId, npcId, StringComparison.OrdinalIgnoreCase))
                {
                    return npc;
                }
            }

            return null;
        }

        private static string GetName(NpcRuntimeState npc)
        {
            return npc != null && npc.Profile != null ? npc.Profile.DisplayName : "(none)";
        }

        private void HandleDayChanged(GameDate date)
        {
            List<SocialActivitySession> sessions = new List<SocialActivitySession>(sessionsByKey.Values);
            for (int i = 0; i < sessions.Count; i++)
            {
                CompleteWaitingSession(sessions[i], "day changed before activity resolved");
            }

            completedActivityKeys.Clear();
        }

        private enum SocialActivityPhase
        {
            Gathering = 0,
            PerformingActivity = 1
        }

        private sealed class SocialActivitySession
        {
            private readonly List<NpcRuntimeState> participants = new List<NpcRuntimeState>();
            private readonly HashSet<string> participantIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public SocialActivitySession(
                string key,
                string label,
                string activityKind,
                LocationDefinition location,
                int priority,
                string reason,
                string dialogue,
                string[] expectedActorIds,
                string[] requiredActorIds,
                string[] optionalActorIds,
                int patienceMinutes,
                int activityDurationMinutes,
                bool startConversationAfterActivity,
                bool requiresGroup,
                GameDate startDate,
                int startMinute)
            {
                Key = key;
                Label = label;
                ActivityKind = activityKind;
                Location = location;
                Priority = priority;
                Reason = reason;
                Dialogue = dialogue;
                ExpectedActorIds = expectedActorIds ?? Array.Empty<string>();
                RequiredActorIds = requiredActorIds ?? Array.Empty<string>();
                OptionalActorIds = optionalActorIds ?? Array.Empty<string>();
                PatienceMinutes = patienceMinutes;
                ActivityDurationMinutes = Mathf.Max(0, activityDurationMinutes);
                StartConversationAfterActivity = startConversationAfterActivity;
                RequiresGroup = requiresGroup;
                StartDate = startDate;
                StartMinute = startMinute;
                ActivityStartDate = startDate;
                ActivityStartMinute = startMinute;
            }

            public string Key { get; }
            public string Label { get; }
            public string ActivityKind { get; }
            public LocationDefinition Location { get; }
            public int Priority { get; }
            public string Reason { get; }
            public string Dialogue { get; }
            public string[] ExpectedActorIds { get; }
            public string[] RequiredActorIds { get; }
            public string[] OptionalActorIds { get; }
            public int PatienceMinutes { get; }
            public int ActivityDurationMinutes { get; }
            public bool StartConversationAfterActivity { get; }
            public bool RequiresGroup { get; }
            public GameDate StartDate { get; }
            public int StartMinute { get; }
            public GameDate ActivityStartDate { get; private set; }
            public int ActivityStartMinute { get; private set; }
            public bool StartedAfterMissingRequired { get; private set; }
            public SocialActivityPhase Phase { get; private set; }
            public bool Completed { get; set; }
            public int ParticipantCount => participants.Count;

            public void BeginPerforming(GameDate date, int minute, bool startedAfterMissingRequired)
            {
                Phase = SocialActivityPhase.PerformingActivity;
                ActivityStartDate = date;
                ActivityStartMinute = minute;
                StartedAfterMissingRequired = startedAfterMissingRequired;
            }

            public bool Contains(NpcRuntimeState npc)
            {
                return npc != null && participants.Contains(npc);
            }

            public bool ContainsActorId(string actorId)
            {
                return !string.IsNullOrWhiteSpace(actorId) && participantIds.Contains(actorId);
            }

            public bool ExpectsActorId(string actorId)
            {
                if (string.IsNullOrWhiteSpace(actorId))
                {
                    return false;
                }

                return ContainsId(ExpectedActorIds, actorId)
                    || ContainsId(RequiredActorIds, actorId)
                    || ContainsId(OptionalActorIds, actorId);
            }

            public bool HasExpectedActorOverlap(string[] actorIds)
            {
                return HasAnyOverlap(ExpectedActorIds, actorIds)
                    || HasAnyOverlap(RequiredActorIds, actorIds)
                    || HasAnyOverlap(OptionalActorIds, actorIds);
            }

            public bool HasArrivedActorOverlap(string[] actorIds)
            {
                if (actorIds == null)
                {
                    return false;
                }

                for (int i = 0; i < actorIds.Length; i++)
                {
                    if (participantIds.Contains(actorIds[i]))
                    {
                        return true;
                    }
                }

                return false;
            }

            public void AddParticipant(NpcRuntimeState npc)
            {
                if (npc == null || Contains(npc))
                {
                    return;
                }

                participants.Add(npc);
                if (npc.Profile != null && !string.IsNullOrWhiteSpace(npc.Profile.NpcId))
                {
                    participantIds.Add(npc.Profile.NpcId);
                }
            }

            public NpcRuntimeState[] GetParticipants()
            {
                return participants.ToArray();
            }

            private static bool ContainsId(string[] ids, string id)
            {
                if (ids == null || string.IsNullOrWhiteSpace(id))
                {
                    return false;
                }

                for (int i = 0; i < ids.Length; i++)
                {
                    if (string.Equals(ids[i], id, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }

            private static bool HasAnyOverlap(string[] left, string[] right)
            {
                if (left == null || right == null)
                {
                    return false;
                }

                for (int i = 0; i < left.Length; i++)
                {
                    for (int j = 0; j < right.Length; j++)
                    {
                        if (string.Equals(left[i], right[j], StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
        }
    }
}
