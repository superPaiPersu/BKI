using System;
using System.Collections.Generic;
using System.Text;
using CityStateSim.AI;
using CityStateSim.Activities;
using CityStateSim.Core;
using CityStateSim.Dialogue;
using CityStateSim.Locations;
using CityStateSim.Memory;
using CityStateSim.Movement;
using CityStateSim.NPC;
using CityStateSim.Perception;
using CityStateSim.Schedule;
using CityStateSim.SocialPlans;
using CityStateSim.Tasks;
using UnityEngine;

namespace CityStateSim.Behavior
{
    [RequireComponent(typeof(NpcRuntimeState))]
    [RequireComponent(typeof(NpcMovementAgent))]
    public sealed class NpcActionExecutor : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private LocationSystem locationSystem;
        [SerializeField] private ActivitySpotSystem activitySpotSystem;
        [SerializeField] private ScheduleSystem scheduleSystem;
        [SerializeField] private GameClock clock;
        [SerializeField] private NpcBehaviorState behaviorState;
        [SerializeField] private NpcTaskController taskController;
        [SerializeField] private MemorySystem memorySystem;
        [SerializeField] private MessageDisplayer messageDisplayer;
        [SerializeField] private NpcPerceptionSensor perceptionSensor;
        [SerializeField] private ConversationArbiter conversationArbiter;
        [SerializeField] private PlayerDialogueRequestSystem playerDialogueRequestSystem;
        [SerializeField] private NpcActivitySystem activitySystem;
        [SerializeField] private SocialPlanSystem socialPlanSystem;

        [Header("Policy")]
        [SerializeField] private bool followScheduleTargets = true;
        [SerializeField] private bool routeSharedDailyIntentsThroughActivitySystem = true;
        [SerializeField] private bool stopWhenTalking = true;
        [SerializeField] private bool requestTargetReplyForOneShot = true;
        [SerializeField] private bool filterRepeatedOneShotFollowups = true;
        [SerializeField, Range(0, 100)] private int eventTaskPriority = 80;
        [SerializeField, Range(0, 100)] private int scheduleOverrideTaskPriority = 60;
        [SerializeField, Min(1f)] private float scheduleOverrideTaskSeconds = 30f;
        [SerializeField, Min(1f)] private float repeatedOneShotSuppressionSeconds = 30f;
        [SerializeField, Min(1f)] private float waitForBusyAiBeforeFallbackSeconds = 10f;
        [SerializeField, Min(0f)] private float waitForConversationStartRetrySeconds = 3f;
        [SerializeField, Min(0.05f)] private float actorApproachDistance = 1.2f;
        [SerializeField, Min(0.05f)] private float followActorDistance = 1.35f;
        [SerializeField, Min(0.1f)] private float followActorDestinationDistance = 1.8f;
        [SerializeField, Min(0.05f)] private float actorInterceptDistance = 1.6f;
        [SerializeField, Min(0.1f)] private float actorRetargetInterval = 0.5f;
        [SerializeField, Min(0.05f)] private float actorRetargetDistance = 0.4f;
        [SerializeField, Min(1f)] private float actorPursuitMaxSeconds = 45f;
        [SerializeField, Min(0.5f)] private float actorAvoidDistance = 3f;
        [SerializeField, Min(0.5f)] private float npcConversationMinSeconds = 4f;
        [SerializeField, Min(1f)] private float npcConversationCharactersPerSecond = 24f;
        [SerializeField, Min(0f)] private float npcConversationExtraSeconds = 0.75f;
        [SerializeField] private bool logExecution = true;

        [Header("Rolling Goal")]
        [SerializeField] private string activeRollingPlanId;
        [SerializeField, TextArea] private string activeOriginalGoal;
        [SerializeField, TextArea] private string activeCurrentGoal;
        [SerializeField, TextArea] private string completedGoalResults;
        [SerializeField] private int rollingPlanRevision;

        private const string ConversationPauseReason = "conversation";

        private enum ConversationHandOffResult
        {
            Failed = 0,
            Waiting = 1,
            Started = 2
        }

        private sealed class DelayedNpcTask
        {
            public DelayedNpcTask(NpcTask task, GameDate dueDate, GameTime dueTime, string key)
            {
                Task = task;
                DueDate = dueDate;
                DueTime = dueTime;
                Key = key;
            }

            public NpcTask Task { get; }
            public GameDate DueDate { get; }
            public GameTime DueTime { get; }
            public string Key { get; }
        }

        private NpcRuntimeState runtimeState;
        private NpcMovementAgent movementAgent;
        private NpcMovementAgent conversationTargetMovement;
        private NpcTask activeMovementTask;
        private NpcTask followReleaseTask;
        private Vector2 followReleaseDestination;
        private Coroutine unlockConversationCoroutine;
        private Coroutine pendingAfterTaskDecisionCoroutine;
        private Coroutine pendingSharedEventConversationCoroutine;
        private NpcTask activeActorPursuitTask;
        private string lastOneShotSignature;
        private float lastOneShotCompletedRealtime = -999f;
        private string lastFailedTaskSignature;
        private float lastFailedTaskRealtime = -999f;
        private readonly HashSet<string> completedActivityKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> attemptedDailyIntentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<NpcTask> playerFollowConsentGrantedTasks = new HashSet<NpcTask>();
        private readonly List<DelayedNpcTask> delayedTasks = new List<DelayedNpcTask>();
        private float activeActorPursuitStartedRealtime = -999f;
        private float nextActorRetargetRealtime;
        private bool resolvingScheduleFromDecision;
        private bool warnedMissingSocialPlanSystem;
        private NpcScheduleAgent scheduleAgent;

        public event Action<LocationDefinition> MovingToLocation;
        public event Action<NpcIntentType> IntentExecuted;
        public event Action<string> ExecutionFailed;

        public bool IsFollowingPlayer
        {
            get
            {
                NpcTask task = taskController != null ? taskController.CurrentTask : null;
                return IsPlayerFollowTask(task)
                    && playerFollowConsentGrantedTasks.Contains(task)
                    && !IsFollowReleaseTask(task);
            }
        }

        public string BuildRollingGoalSummary()
        {
            if (string.IsNullOrWhiteSpace(activeOriginalGoal)
                && string.IsNullOrWhiteSpace(activeCurrentGoal)
                && string.IsNullOrWhiteSpace(completedGoalResults))
            {
                return "(none)";
            }

            StringBuilder builder = new StringBuilder();
            builder.Append("planId=");
            builder.Append(string.IsNullOrWhiteSpace(activeRollingPlanId) ? "(none)" : activeRollingPlanId);
            builder.Append(", revision=");
            builder.Append(rollingPlanRevision);
            builder.AppendLine();
            builder.Append("originalGoal: ");
            builder.AppendLine(string.IsNullOrWhiteSpace(activeOriginalGoal) ? "(none)" : activeOriginalGoal);
            builder.Append("currentGoal: ");
            builder.AppendLine(string.IsNullOrWhiteSpace(activeCurrentGoal) ? "(none)" : activeCurrentGoal);
            builder.Append("completedResults: ");
            builder.Append(string.IsNullOrWhiteSpace(completedGoalResults) ? "(none)" : completedGoalResults);
            return builder.ToString();
        }

        public Sprite GetHeadIcon()
        {
            NpcProfile profile = runtimeState != null ? runtimeState.Profile : null;
            if (profile == null)
            {
                return null;
            }

            string emotion = behaviorState != null ? behaviorState.Emotion : string.Empty;
            Sprite portrait = NpcPortraitCatalog.LoadPortrait(profile.NpcId, profile.DisplayName, emotion);
            if (portrait != null)
            {
                return portrait;
            }

            string fallback = NpcPortraitCatalog.GetFallbackPortraitName(profile.NpcId, profile.DisplayName);
            return NpcPortraitCatalog.LoadPortrait(profile.NpcId, profile.DisplayName, fallback);
        }

        private void Awake()
        {
            runtimeState = GetComponent<NpcRuntimeState>();
            movementAgent = GetComponent<NpcMovementAgent>();

            if (behaviorState == null)
            {
                behaviorState = GetComponent<NpcBehaviorState>();
            }

            if (taskController == null)
            {
                taskController = GetComponent<NpcTaskController>();
            }

            if (locationSystem == null)
            {
                locationSystem = FindFirstObjectByType<LocationSystem>();
            }

            if (scheduleSystem == null)
            {
                scheduleSystem = FindFirstObjectByType<ScheduleSystem>();
            }

            if (activitySpotSystem == null)
            {
                activitySpotSystem = FindFirstObjectByType<ActivitySpotSystem>();
            }

            if (clock == null)
            {
                clock = FindFirstObjectByType<GameClock>();
            }

            if (memorySystem == null)
            {
                memorySystem = FindFirstObjectByType<MemorySystem>();
            }

            if (messageDisplayer == null)
            {
                messageDisplayer = FindFirstObjectByType<MessageDisplayer>();
            }

            if (perceptionSensor == null)
            {
                perceptionSensor = GetComponent<NpcPerceptionSensor>();
            }

            if (conversationArbiter == null)
            {
                conversationArbiter = ConversationArbiter.GetOrCreate();
            }

            if (playerDialogueRequestSystem == null)
            {
                playerDialogueRequestSystem = FindFirstObjectByType<PlayerDialogueRequestSystem>();
            }

            if (activitySystem == null)
            {
                activitySystem = FindFirstObjectByType<NpcActivitySystem>();
            }

            if (socialPlanSystem == null)
            {
                socialPlanSystem = FindFirstObjectByType<SocialPlanSystem>();
            }

            scheduleAgent = GetComponent<NpcScheduleAgent>();
        }

        private void OnEnable()
        {
            runtimeState.ScheduleTargetChanged += HandleScheduleTargetChanged;
            movementAgent.TargetReached += HandleMovementTargetReached;

            if (behaviorState != null)
            {
                behaviorState.DecisionApplied += HandleDecisionApplied;
            }

            if (taskController != null)
            {
                taskController.TaskStarted += HandleTaskStarted;
                taskController.TaskCompleted += HandleTaskCompleted;
                taskController.TaskChanged += HandleTaskChanged;
            }

            if (clock != null)
            {
                clock.DayChanged += HandleDayChanged;
                clock.MinuteChanged += HandleMinuteChanged;
            }
        }

        private void OnDisable()
        {
            runtimeState.ScheduleTargetChanged -= HandleScheduleTargetChanged;
            movementAgent.TargetReached -= HandleMovementTargetReached;

            if (behaviorState != null)
            {
                behaviorState.DecisionApplied -= HandleDecisionApplied;
            }

            if (taskController != null)
            {
                taskController.TaskStarted -= HandleTaskStarted;
                taskController.TaskCompleted -= HandleTaskCompleted;
                taskController.TaskChanged -= HandleTaskChanged;
            }

            if (clock != null)
            {
                clock.DayChanged -= HandleDayChanged;
                clock.MinuteChanged -= HandleMinuteChanged;
            }

            if (unlockConversationCoroutine != null)
            {
                StopCoroutine(unlockConversationCoroutine);
                unlockConversationCoroutine = null;
            }

            if (pendingAfterTaskDecisionCoroutine != null)
            {
                StopCoroutine(pendingAfterTaskDecisionCoroutine);
                pendingAfterTaskDecisionCoroutine = null;
            }

            if (pendingSharedEventConversationCoroutine != null)
            {
                StopCoroutine(pendingSharedEventConversationCoroutine);
                pendingSharedEventConversationCoroutine = null;
            }

            playerFollowConsentGrantedTasks.Clear();
        }

        private void Update()
        {
            UpdateActorTargetMovement();
        }

        public void ExecuteCurrentScheduleTarget()
        {
            if (!EnsureTaskController())
            {
                return;
            }

            if (TryStartDueSocialPlanTask())
            {
                return;
            }

            if (TryStartCurrentDailyIntentTask())
            {
                return;
            }

            taskController.SetScheduleTask(runtimeState.PlannedLocation, runtimeState.CurrentAction);
        }

        public void MoveToLocation(LocationDefinition location)
        {
            if (location == null)
            {
                ReportFailure("Cannot move to a null location.");
                return;
            }

            if (!EnsureTaskController())
            {
                return;
            }

            taskController.TryStartTask(new NpcTask(
                "Move to location",
                NpcTaskKind.MoveToLocation,
                location,
                string.Empty,
                scheduleOverrideTaskPriority,
                true,
                false,
                "manual movement request",
                scheduleOverrideTaskSeconds));
        }

        public void FaceActor(GameObject actor)
        {
            if (actor != null)
            {
                movementAgent.Face(actor.transform.position);
            }
        }

        public void Stop()
        {
            activeMovementTask = null;
            activitySpotSystem?.ReleaseSpot(runtimeState);
            movementAgent.Stop();
        }

        public bool StopFollowingPlayer(string reason = "")
        {
            if (!EnsureTaskController())
            {
                return false;
            }

            NpcTask task = taskController.CurrentTask;
            if (!IsPlayerFollowTask(task))
            {
                return false;
            }

            PlayerMovementController player = FindFirstObjectByType<PlayerMovementController>();
            if (player == null || movementAgent == null || !movementAgent.CanMove)
            {
                playerFollowConsentGrantedTasks.Remove(task);
                if (activeMovementTask == task)
                {
                    activeMovementTask = null;
                }

                if (activeActorPursuitTask == task)
                {
                    activeActorPursuitTask = null;
                }

                movementAgent?.Stop();

                string completionReason = string.IsNullOrWhiteSpace(reason)
                    ? "player dismissed the follower; stop following player and decide next action."
                    : reason;
                taskController.CompleteCurrentTask(completionReason);
                return true;
            }

            followReleaseTask = task;
            followReleaseDestination = player.transform.position;
            playerFollowConsentGrantedTasks.Remove(task);
            activeMovementTask = task;
            if (activeActorPursuitTask != task)
            {
                activeActorPursuitTask = task;
                activeActorPursuitStartedRealtime = Time.realtimeSinceStartup;
            }

            activitySpotSystem?.ReleaseSpot(runtimeState);
            movementAgent.MoveTo(followReleaseDestination);
            movementAgent.Face(followReleaseDestination);

            if (logExecution)
            {
                Debug.Log(
                    $"[NPC Action] {name}: follow released; moving to player's last position {followReleaseDestination} before deciding next action.",
                    this);
            }
            return true;
        }

        public void ResolveScheduleNow()
        {
            ResolveCurrentScheduleAfterDecision();
        }

        public bool TryStartDueSocialPlanTask()
        {
            if (socialPlanSystem == null)
            {
                socialPlanSystem = FindFirstObjectByType<SocialPlanSystem>();
            }

            if (socialPlanSystem == null || runtimeState == null)
            {
                return false;
            }

            if (!EnsureTaskController())
            {
                return false;
            }

            if (!socialPlanSystem.TryCreateDueTaskForNpc(runtimeState, completedActivityKeys, out NpcTask task, out SocialPlan plan, out string reason))
            {
                return false;
            }

            if (taskController.HasNonScheduleTask || IsTemporarilyBlockedForDelayedTask())
            {
                return true;
            }

            if (!NpcTaskConstraintValidator.ValidateAtExecutionPoint(task, out string constraintFailure))
            {
                if (task.Kind == NpcTaskKind.AttendActivity && !string.IsNullOrWhiteSpace(task.ActivityKey))
                {
                    socialPlanSystem.MarkPlanFailed(task.ActivityKey, "failed: " + constraintFailure);
                }

                ReportFailure(constraintFailure);
                RequestDecisionAfterTask(task, "failed: " + constraintFailure);
                return true;
            }

            if (!taskController.TryStartTask(task))
            {
                return true;
            }

            socialPlanSystem.MarkPlanTaskStarted(runtimeState, task);
            if (logExecution)
            {
                Debug.Log(
                    $"[NPC Action] {name}: started social plan task. plan={plan?.PlanId}, reason={reason}, targetLocation={(task.TargetLocation != null ? task.TargetLocation.LocationId : string.Empty)}",
                    this);
            }

            return true;
        }

        private void HandleScheduleTargetChanged(NpcRuntimeState state)
        {
            if (!followScheduleTargets)
            {
                return;
            }

            ExecuteCurrentScheduleTarget();
        }

        private void HandleTaskStarted(NpcTask task)
        {
            ExecuteTask(task);
        }

        private void HandleTaskCompleted(NpcTask task, string reason)
        {
            if (activeMovementTask == task)
            {
                activeMovementTask = null;
                movementAgent.Stop();
            }

            if (followReleaseTask == task)
            {
                ClearFollowReleaseState();
            }

            if (task == null)
            {
                return;
            }

            playerFollowConsentGrantedTasks.Remove(task);

            if (activeActorPursuitTask == task)
            {
                activeActorPursuitTask = null;
            }

            if (task.Kind != NpcTaskKind.FollowSchedule && reason != "replaced")
            {
                if (task.Kind == NpcTaskKind.AttendActivity
                    && !string.IsNullOrWhiteSpace(task.ActivityKey)
                    && IsFailureReason(reason))
                {
                    socialPlanSystem?.MarkPlanFailed(task.ActivityKey, reason);
                }

                RequestDecisionAfterTask(task, reason);
            }
        }

        private void HandleTaskChanged(NpcTask previousTask, NpcTask currentTask)
        {
            if (previousTask != null && previousTask != currentTask)
            {
                playerFollowConsentGrantedTasks.Remove(previousTask);
            }

            if (previousTask != null
                && previousTask.Kind == NpcTaskKind.AttendActivity
                && previousTask != currentTask
                && !string.IsNullOrWhiteSpace(previousTask.ActivityKey))
            {
                completedActivityKeys.Add(previousTask.ActivityKey);
            }
        }

        private void HandleDayChanged(GameDate date)
        {
            completedActivityKeys.Clear();
            attemptedDailyIntentKeys.Clear();
        }

        private void HandleMinuteChanged(GameDate date, GameTime time)
        {
            if (TryStartDueDelayedTask())
            {
                return;
            }

            if (TryStartDueSocialPlanTask())
            {
                return;
            }

            if (followScheduleTargets)
            {
                TryStartCurrentDailyIntentTask();
            }
        }

        private void UpdateActorTargetMovement()
        {
            NpcTask task = activeMovementTask;
            if (!IsActorTargetTask(task) || movementAgent == null || !movementAgent.CanMove)
            {
                return;
            }

            if (IsFollowReleaseTask(task))
            {
                UpdateFollowReleaseTask(task);
                return;
            }

            GameObject target = FindActorById(task.TargetActorId);
            if (target == null)
            {
                return;
            }

            if (task.Kind == NpcTaskKind.FollowActor)
            {
                UpdateFollowActorTask(task, target);
                return;
            }

            if (IsCloseEnoughToActor(target, Mathf.Max(actorInterceptDistance, GetActorConversationDistance())))
            {
                CompleteActorTargetMovementEarly(task, target);
                return;
            }

            if (!ShouldDynamicallyPursueActor(task))
            {
                return;
            }

            if (Time.realtimeSinceStartup - activeActorPursuitStartedRealtime > actorPursuitMaxSeconds)
            {
                return;
            }

            if (Time.realtimeSinceStartup < nextActorRetargetRealtime)
            {
                return;
            }

            Vector2 desiredPosition = GetApproachPosition(target.transform.position);
            nextActorRetargetRealtime = Time.realtimeSinceStartup + actorRetargetInterval;
            if (Vector2.Distance(movementAgent.TargetPosition, desiredPosition) < actorRetargetDistance)
            {
                return;
            }

            movementAgent.MoveTo(desiredPosition);
            movementAgent.Face(target.transform.position);
            if (logExecution)
            {
                Debug.Log($"[NPC Action] {name}: retargeting moving actor {task.TargetActorId}, task={task.Kind}", this);
            }
        }

        private void CompleteActorTargetMovementEarly(NpcTask task, GameObject target)
        {
            if (task == null || target == null || taskController == null || taskController.CurrentTask != task)
            {
                return;
            }

            activeMovementTask = null;
            if (activeActorPursuitTask == task)
            {
                activeActorPursuitTask = null;
            }

            movementAgent.Stop();
            movementAgent.Face(target.transform.position);
            if (logExecution)
            {
                Debug.Log($"[NPC Action] {name}: intercepted moving actor {task.TargetActorId}, task={task.Kind}", this);
            }

            if (task.Kind == NpcTaskKind.FindActor)
            {
                CompleteFindActorTask(task, target);
                return;
            }

            if (task.Kind == NpcTaskKind.TalkToActor)
            {
                StopForConversation(task);
                return;
            }

            if (task.OneShot)
            {
                StartCoroutine(CompleteOneShotTaskAfterInteraction(task));
            }
        }

        private void HandleMovementTargetReached(NpcMovementAgent agent)
        {
            NpcTask task = activeMovementTask;
            activeMovementTask = null;
            if (task == null)
            {
                return;
            }

            if (task.Kind == NpcTaskKind.FollowActor)
            {
                GameObject followTarget = FindActorById(task.TargetActorId);
                activeMovementTask = task;
                UpdateFollowActorTask(task, followTarget);
                return;
            }

            if (TryContinueActorPursuitAfterStaleArrival(task))
            {
                return;
            }

            FaceTaskTarget(task);
            SyncActualLocationFromReachedTask(task);

            if (logExecution)
            {
                Debug.Log($"[NPC Action] {name}: reached task target. kind={task.Kind}, intent={task.SourceIntent}, targetActor={task.TargetActorId}", this);
            }

            GameObject target = FindActorById(task.TargetActorId);
            if (task.Kind == NpcTaskKind.FindActor)
            {
                CompleteFindActorTask(task, target);
                return;
            }

            if (task.Kind == NpcTaskKind.TalkToActor)
            {
                StopForConversation(task);
                return;
            }

            if (task.OneShot)
            {
                StartCoroutine(CompleteOneShotTaskAfterInteraction(task));
                return;
            }

            BeginReachedLocationTask(task);
        }

        private void UpdateRollingGoalFromDecision(NpcAiDecision decision, string source)
        {
            if (decision == null)
            {
                return;
            }

            string status = NormalizeGoalStatus(decision.goalStatus);
            bool createsExecutableTask = ToTaskKind(decision.ParsedIntent) != NpcTaskKind.FollowSchedule;
            if ((status == "completed" || status == "abandoned") && !createsExecutableTask)
            {
                RecordRollingGoalFinalState(decision, status, source);
                ClearRollingGoal(status + ": " + decision.goalStatusReason);
                return;
            }

            if ((status == "completed" || status == "abandoned") && createsExecutableTask)
            {
                status = "active";
            }

            bool hasGoalText = !string.IsNullOrWhiteSpace(decision.originalGoal) || !string.IsNullOrWhiteSpace(decision.currentGoal);
            if (!createsExecutableTask)
            {
                if (decision.ParsedIntent == NpcIntentType.ContinueCurrentAction
                    && !hasGoalText
                    && !string.IsNullOrWhiteSpace(activeOriginalGoal))
                {
                    ClearRollingGoal("AI continued routine without an active next step.");
                }

                if (!string.IsNullOrWhiteSpace(activeOriginalGoal) && !string.IsNullOrWhiteSpace(decision.currentGoal))
                {
                    activeCurrentGoal = CleanGoalText(decision.currentGoal);
                }

                return;
            }

            string incomingOriginalGoal = CleanGoalText(decision.originalGoal);
            string incomingCurrentGoal = CleanGoalText(decision.currentGoal);
            bool isPlayerDialogueAction = string.Equals(decision.dialogueContextKind, "player_dialogue", StringComparison.OrdinalIgnoreCase);
            bool shouldStartNewPlan = string.IsNullOrWhiteSpace(activeOriginalGoal)
                || (isPlayerDialogueAction
                    && !string.IsNullOrWhiteSpace(incomingOriginalGoal)
                    && !SameLoose(activeOriginalGoal, incomingOriginalGoal));

            if (shouldStartNewPlan)
            {
                activeRollingPlanId = Guid.NewGuid().ToString("N");
                activeOriginalGoal = !string.IsNullOrWhiteSpace(incomingOriginalGoal)
                    ? incomingOriginalGoal
                    : BuildFallbackOriginalGoal(decision);
                activeCurrentGoal = !string.IsNullOrWhiteSpace(incomingCurrentGoal)
                    ? incomingCurrentGoal
                    : BuildFallbackCurrentGoal(decision);
                completedGoalResults = string.Empty;
                rollingPlanRevision = 1;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(activeRollingPlanId))
                {
                    activeRollingPlanId = Guid.NewGuid().ToString("N");
                }

                if (!string.IsNullOrWhiteSpace(incomingCurrentGoal))
                {
                    activeCurrentGoal = incomingCurrentGoal;
                }
                else if (createsExecutableTask && string.IsNullOrWhiteSpace(activeCurrentGoal))
                {
                    activeCurrentGoal = BuildFallbackCurrentGoal(decision);
                }

                rollingPlanRevision++;
            }

            decision.originalGoal = activeOriginalGoal;
            decision.currentGoal = activeCurrentGoal;
            decision.goalStatus = string.IsNullOrWhiteSpace(status) || status == "none" ? "active" : status;

            if (logExecution && createsExecutableTask)
            {
                Debug.Log(
                    $"[NPC Plan] {name}: revision={rollingPlanRevision}, source={source}, originalGoal={activeOriginalGoal}, currentGoal={activeCurrentGoal}",
                    this);
            }
        }

        private void RecordRollingGoalFinalState(NpcAiDecision decision, string status, string source)
        {
            if (!logExecution)
            {
                return;
            }

            string original = !string.IsNullOrWhiteSpace(activeOriginalGoal)
                ? activeOriginalGoal
                : CleanGoalText(decision != null ? decision.originalGoal : string.Empty);
            Debug.Log(
                $"[NPC Plan] {name}: {status}. source={source}, originalGoal={original}, reason={decision?.goalStatusReason}",
                this);
        }

        private void ClearRollingGoal(string reason)
        {
            if (logExecution && !string.IsNullOrWhiteSpace(activeOriginalGoal))
            {
                Debug.Log($"[NPC Plan] {name}: cleared rolling goal. reason={reason}", this);
            }

            activeRollingPlanId = string.Empty;
            activeOriginalGoal = string.Empty;
            activeCurrentGoal = string.Empty;
            completedGoalResults = string.Empty;
            rollingPlanRevision = 0;
        }

        private static string NormalizeGoalStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return "none";
            }

            string normalized = status.Trim().ToLowerInvariant();
            return normalized == "active"
                || normalized == "completed"
                || normalized == "abandoned"
                || normalized == "none"
                ? normalized
                : "none";
        }

        private static string CleanGoalText(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        }

        private static string BuildFallbackOriginalGoal(NpcAiDecision decision)
        {
            if (decision == null)
            {
                return string.Empty;
            }

            return FirstNonEmpty(
                decision.currentGoal,
                decision.nextActionPreference,
                decision.dialogue,
                $"{decision.intent} targetActorId={decision.targetActorId} targetLocationId={decision.targetLocationId}");
        }

        private static string BuildFallbackCurrentGoal(NpcAiDecision decision)
        {
            if (decision == null)
            {
                return string.Empty;
            }

            return FirstNonEmpty(
                decision.currentGoal,
                decision.nextActionPreference,
                $"{decision.intent} targetActorId={decision.targetActorId} targetLocationId={decision.targetLocationId}");
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    return CleanGoalText(values[i]);
                }
            }

            return string.Empty;
        }

        private static bool SameLoose(string left, string right)
        {
            return string.Equals(NormalizeLoose(left), NormalizeLoose(right), StringComparison.OrdinalIgnoreCase);
        }

        private void HandleDecisionApplied(NpcBehaviorState state, NpcAiDecision decision)
        {
            if (decision == null)
            {
                return;
            }

            if (conversationArbiter != null && conversationArbiter.IsNpcInConversation(runtimeState))
            {
                if (logExecution)
                {
                    Debug.Log(
                        $"[NPC Action] {name}: ignored executable decision while NPC conversation is active. intent={decision.intent}, targetActor={decision.targetActorId}, targetLocation={decision.targetLocationId}",
                        this);
                }

                return;
            }

            UpdateRollingGoalFromDecision(decision, "decision applied");
            NpcIntentType intent = decision.ParsedIntent;
            if (intent != NpcIntentType.TalkToPlayer && intent != NpcIntentType.TalkToNpc)
            {
                UnlockConversationMovement();
            }

            if (intent == NpcIntentType.ContinueCurrentAction)
            {
                StartFollowScheduleTask("AI chose ContinueCurrentAction");
                IntentExecuted?.Invoke(intent);
                return;
            }

            if (intent == NpcIntentType.SelfTalk)
            {
                ExecuteSelfTalk(decision);
                IntentExecuted?.Invoke(intent);
                return;
            }

            if (intent == NpcIntentType.AttendActivity && TryRegisterSocialPlanDecision(decision))
            {
                if (decision.ParsedTimingMode == NpcTimingMode.Immediate)
                {
                    TryStartDueSocialPlanTask();
                }

                IntentExecuted?.Invoke(intent);
                return;
            }

            if (TryCreateTaskFromDecision(decision, out NpcTask task, out string suppressedReason, out string taskCreationFailureReason))
            {
                if (TryScheduleDelayedTask(decision, task))
                {
                    IntentExecuted?.Invoke(intent);
                    return;
                }

                if (EnsureTaskController() && taskController.TryStartTask(task))
                {
                    IntentExecuted?.Invoke(intent);
                    return;
                }

                ReportFailure($"Task was rejected. intent={decision.intent}, targetLocation={decision.targetLocationId}, targetActor={decision.targetActorId}");
            }

            if (!string.IsNullOrWhiteSpace(suppressedReason))
            {
                StartFollowScheduleTask(suppressedReason);
                IntentExecuted?.Invoke(intent);
                return;
            }

            RequestDecisionAfterExecutionFailure(
                decision,
                string.IsNullOrWhiteSpace(taskCreationFailureReason)
                    ? "Decision could not be converted into an executable task."
                    : taskCreationFailureReason);

            IntentExecuted?.Invoke(intent);
        }

        private bool TryCreateTaskFromDecision(
            NpcAiDecision decision,
            out NpcTask task,
            out string suppressedReason,
            out string failureReason)
        {
            task = null;
            suppressedReason = string.Empty;
            failureReason = string.Empty;
            if (decision == null)
            {
                failureReason = "Decision was empty and could not be converted into an executable task.";
                return false;
            }

            string targetActorId = string.IsNullOrWhiteSpace(decision.targetActorId) ? string.Empty : decision.targetActorId.Trim();
            if (decision.ParsedIntent == NpcIntentType.TalkToPlayer && string.IsNullOrWhiteSpace(targetActorId))
            {
                targetActorId = "player";
            }

            NpcIntentType intent = NormalizeTaskIntent(decision, targetActorId);
            NpcTaskKind kind = ToTaskKind(intent);
            if (kind == NpcTaskKind.FollowSchedule)
            {
                failureReason = $"Intent {decision.intent} does not create a non-schedule executable task.";
                return false;
            }

            LocationDefinition targetLocation = ResolveTargetLocation(decision);
            LocationDefinition plannedTargetLocation = ResolvePlannedTargetLocation(decision);
            if (kind == NpcTaskKind.FindActor && !string.IsNullOrWhiteSpace(targetActorId))
            {
                if (plannedTargetLocation == null)
                {
                    plannedTargetLocation = targetLocation;
                }

                targetLocation = null;
            }

            NpcDailyIntent currentIntent = kind == NpcTaskKind.AttendActivity ? GetCurrentDailyIntent() : null;
            if (kind == NpcTaskKind.AttendActivity && targetLocation == null && currentIntent != null)
            {
                targetLocation = ResolveTargetLocation(currentIntent.TargetLocationId);
            }

            if (kind == NpcTaskKind.AttendActivity && targetLocation == null && plannedTargetLocation != null)
            {
                targetLocation = plannedTargetLocation;
            }

            if (kind == NpcTaskKind.FollowActor && targetLocation == null && plannedTargetLocation != null)
            {
                targetLocation = plannedTargetLocation;
            }

            if (kind == NpcTaskKind.FollowActor
                && !PrepareFollowActorTargets(targetActorId, ref targetLocation, ref plannedTargetLocation, out failureReason))
            {
                return false;
            }

            if (kind == NpcTaskKind.AttendActivity
                && string.IsNullOrWhiteSpace(targetActorId)
                && currentIntent != null
                && !string.IsNullOrWhiteSpace(currentIntent.TargetActorId))
            {
                targetActorId = currentIntent.TargetActorId.Trim();
            }

            bool hasActorTarget = !string.IsNullOrWhiteSpace(targetActorId);
            bool hasLocationTarget = targetLocation != null;
            bool oneShot = IsOneShotEvent(decision);
            bool scheduleOverride = IsScheduleOverrideEvent(decision);
            if (intent == NpcIntentType.TalkToPlayer)
            {
                oneShot = true;
            }
            else if (intent == NpcIntentType.TalkToNpc)
            {
                oneShot = false;
                scheduleOverride = true;
            }
            else if (intent == NpcIntentType.FindActor)
            {
                oneShot = true;
            }
            else if (intent == NpcIntentType.FollowActor)
            {
                oneShot = false;
                scheduleOverride = true;
            }

            string activityKind = string.Empty;
            string activityKey = string.Empty;
            string[] participantActorIds = Array.Empty<string>();
            string[] requiredActorIds = Array.Empty<string>();
            string[] optionalActorIds = Array.Empty<string>();
            int patienceMinutes = 0;

            bool hasSharedPlanMetadata = HasSharedPlanMetadata(decision);
            if (kind == NpcTaskKind.AttendActivity || hasSharedPlanMetadata)
            {
                activityKind = !string.IsNullOrWhiteSpace(decision.activityKind)
                    ? decision.activityKind.Trim()
                    : !string.IsNullOrWhiteSpace(currentIntent?.ActivityKind)
                        ? currentIntent.ActivityKind
                        : string.Empty;
                participantActorIds = BuildActivityParticipantIds(currentIntent, decision, targetLocation ?? plannedTargetLocation);
                requiredActorIds = BuildActivityRequiredIds(currentIntent, decision, participantActorIds);
                optionalActorIds = BuildActivityOptionalIds(currentIntent, decision);
                patienceMinutes = decision.patienceMinutes > 0
                    ? decision.patienceMinutes
                    : currentIntent != null && currentIntent.PatienceMinutes > 0
                        ? currentIntent.PatienceMinutes
                        : hasSharedPlanMetadata ? 20 : 0;
            }

            if (kind == NpcTaskKind.AttendActivity)
            {
                if (!hasLocationTarget)
                {
                    hasLocationTarget = targetLocation != null;
                }

                if (!hasLocationTarget)
                {
                    failureReason = "AttendActivity requires a valid targetLocationId or plannedTargetLocationId.";
                    return false;
                }

                if (patienceMinutes <= 0)
                {
                    patienceMinutes = 20;
                }

                activityKey = BuildActivityKey(currentIntent, targetLocation, activityKind, participantActorIds);
                if (completedActivityKeys.Contains(activityKey))
                {
                    failureReason = $"AttendActivity was already completed today for activityKey={activityKey}.";
                    return false;
                }

                oneShot = false;
                scheduleOverride = true;
            }

            if (IsRepeatedFailedTaskFollowup(decision, targetActorId, targetLocation))
            {
                if (logExecution)
                {
                    Debug.Log($"[NPC Action] {name}: ignored repeated failed-task follow-up. intent={decision.intent}, targetActor={targetActorId}, targetLocation={decision.targetLocationId}", this);
                }

                suppressedReason = "ignored repeated failed-task follow-up";
                failureReason = suppressedReason;
                return false;
            }

            if (filterRepeatedOneShotFollowups && IsRepeatedOneShotFollowup(decision, targetActorId, targetLocation))
            {
                if (logExecution)
                {
                    Debug.Log($"[NPC Action] {name}: ignored repeated one-shot follow-up. intent={decision.intent}, targetActor={targetActorId}, targetLocation={decision.targetLocationId}", this);
                }

                suppressedReason = "ignored repeated one-shot follow-up";
                failureReason = suppressedReason;
                return false;
            }

            if (!hasActorTarget && !hasLocationTarget && RequiresTarget(kind))
            {
                failureReason = $"Intent {decision.intent} requires a target actor or location.";
                return false;
            }

            if (intent == NpcIntentType.TalkToNpc && !hasActorTarget)
            {
                failureReason = "TalkToNpc requires targetActorId.";
                return false;
            }

            if (intent == NpcIntentType.FollowActor && !hasActorTarget)
            {
                failureReason = "FollowActor requires targetActorId.";
                return false;
            }

            int priority = kind == NpcTaskKind.AttendActivity && currentIntent != null
                ? currentIntent.Priority
                : oneShot ? eventTaskPriority : scheduleOverrideTaskPriority;
            float duration = kind == NpcTaskKind.AttendActivity
                ? -1f
                : kind == NpcTaskKind.TalkToActor
                    ? -1f
                : kind == NpcTaskKind.FollowActor
                    ? (targetLocation == null ? scheduleOverrideTaskSeconds : -1f)
                    : scheduleOverride && !oneShot ? scheduleOverrideTaskSeconds : -1f;
            string reason = string.IsNullOrWhiteSpace(decision.nextActionPreference)
                ? decision.dialogue
                : decision.nextActionPreference;

            NpcTask candidate = new NpcTask(
                BuildTaskLabel(decision),
                kind,
                targetLocation,
                targetActorId,
                priority,
                true,
                oneShot,
                reason,
                duration,
                intent.ToString(),
                decision.eventKind,
                decision.dialogue,
                activityKind,
                activityKey,
                participantActorIds,
                requiredActorIds,
                optionalActorIds,
                patienceMinutes,
                plannedTargetLocation != null ? plannedTargetLocation.LocationId : string.Empty,
                decision.dialogueContextKind,
                decision.dialogueSourceActorId,
                decision.dialogueSubjectActorId,
                decision.dialogueSubjectLocationId,
                decision.dialogueSourceText,
                activeRollingPlanId,
                activeOriginalGoal,
                activeCurrentGoal,
                completedGoalResults);
            if (!NpcTaskConstraintValidator.ValidateAtExecutionPoint(candidate, out failureReason))
            {
                return false;
            }

            task = candidate;
            return true;
        }

        private bool TryRegisterSocialPlanDecision(NpcAiDecision decision)
        {
            if (decision == null)
            {
                return false;
            }

            if (HasExplicitSocialPlanChanges(decision))
            {
                return true;
            }

            if (socialPlanSystem == null)
            {
                socialPlanSystem = FindFirstObjectByType<SocialPlanSystem>();
            }

            if (socialPlanSystem == null)
            {
                if (!warnedMissingSocialPlanSystem && HasSharedPlanMetadata(decision))
                {
                    warnedMissingSocialPlanSystem = true;
                    Debug.LogWarning(
                        $"[NPC Action] {name}: SocialPlanSystem is missing, so this shared plan cannot be registered for every participant. " +
                        "Add SocialPlanSystem to a scene manager object and link its Clock, LocationSystem, and MemorySystem references.",
                        this);
                }

                return false;
            }

            string context =
                $"{decision.dialogue} {decision.nextActionPreference} " +
                $"targetLocationId={decision.targetLocationId} plannedTargetLocationId={decision.plannedTargetLocationId} " +
                $"activityKind={decision.activityKind}";
            List<NpcRuntimeState> related = new List<NpcRuntimeState>();
            if (runtimeState != null)
            {
                related.Add(runtimeState);
            }

            return socialPlanSystem.TryRegisterFromDecision(
                runtimeState,
                decision,
                context,
                related,
                false,
                out _);
        }

        private static bool HasExplicitSocialPlanChanges(NpcAiDecision decision)
        {
            return decision != null && decision.socialPlanChanges != null && decision.socialPlanChanges.Length > 0;
        }

        private bool TryScheduleDelayedTask(NpcAiDecision decision, NpcTask task)
        {
            if (decision == null || task == null || !TryResolveDelayedDueTime(decision, out GameDate dueDate, out GameTime dueTime))
            {
                return false;
            }

            if (IsDueNowOrPast(dueDate, dueTime))
            {
                return false;
            }

            string key = BuildDelayedTaskKey(task, dueDate, dueTime);
            if (IsDelayedTaskPending(key))
            {
                if (logExecution)
                {
                    Debug.Log($"[NPC Action] {name}: delayed task already pending. due={dueDate} {dueTime}, key={key}", this);
                }

                return true;
            }

            delayedTasks.Add(new DelayedNpcTask(task, dueDate, dueTime, key));
            if (logExecution)
            {
                Debug.Log(
                    $"[NPC Action] {name}: scheduled delayed task for {dueDate} {dueTime}. " +
                    $"kind={task.Kind}, targetActor={task.TargetActorId}, targetLocation={(task.TargetLocation != null ? task.TargetLocation.LocationId : string.Empty)}, reason={task.Reason}",
                    this);
            }

            return true;
        }

        private bool TryResolveDelayedDueTime(NpcAiDecision decision, out GameDate dueDate, out GameTime dueTime)
        {
            dueDate = clock != null ? clock.CurrentDate : new GameDate(1, 1, 1);
            dueTime = clock != null ? clock.CurrentTime : new GameTime(0, 0);
            if (decision == null || clock == null)
            {
                return false;
            }

            switch (decision.ParsedTimingMode)
            {
                case NpcTimingMode.DelayMinutes:
                    if (decision.delayMinutes <= 0)
                    {
                        return false;
                    }

                    int totalMinutes = clock.CurrentTime.TotalMinutes + decision.delayMinutes;
                    dueDate = clock.CurrentDate;
                    while (totalMinutes >= 1440)
                    {
                        totalMinutes -= 1440;
                        dueDate = clock.GetNextDate(dueDate);
                    }

                    dueTime = GameTime.FromTotalMinutes(totalMinutes);
                    return true;
                case NpcTimingMode.TodayAtTime:
                    if (decision.scheduledStartHour < 0 || decision.scheduledStartMinute < 0)
                    {
                        return false;
                    }

                    dueDate = clock.CurrentDate;
                    dueTime = new GameTime(decision.scheduledStartHour, decision.scheduledStartMinute);
                    return true;
                case NpcTimingMode.NextDayAtTime:
                    if (decision.scheduledStartHour < 0 || decision.scheduledStartMinute < 0)
                    {
                        return false;
                    }

                    dueDate = clock.GetNextDate(dueDate);
                    dueTime = new GameTime(decision.scheduledStartHour, decision.scheduledStartMinute);
                    return true;
                case NpcTimingMode.Immediate:
                default:
                    return false;
            }
        }

        private bool TryStartDueDelayedTask()
        {
            if (delayedTasks.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < delayedTasks.Count; i++)
            {
                DelayedNpcTask delayedTask = delayedTasks[i];
                if (delayedTask == null || !IsDueNowOrPast(delayedTask.DueDate, delayedTask.DueTime))
                {
                    continue;
                }

                if (IsTemporarilyBlockedForDelayedTask())
                {
                    return true;
                }

                if (!EnsureTaskController())
                {
                    return false;
                }

                if (!taskController.TryStartTask(delayedTask.Task))
                {
                    return true;
                }

                delayedTasks.RemoveAt(i);
                if (logExecution)
                {
                    Debug.Log(
                        $"[NPC Action] {name}: started delayed task due at {delayedTask.DueDate} {delayedTask.DueTime}. " +
                        $"kind={delayedTask.Task.Kind}, targetActor={delayedTask.Task.TargetActorId}, targetLocation={(delayedTask.Task.TargetLocation != null ? delayedTask.Task.TargetLocation.LocationId : string.Empty)}",
                        this);
                }

                return true;
            }

            return false;
        }

        private bool IsTemporarilyBlockedForDelayedTask()
        {
            if (conversationArbiter != null && conversationArbiter.IsNpcInConversation(runtimeState))
            {
                return true;
            }

            if (IsInPlayerConversation())
            {
                return true;
            }

            if (activitySystem != null && activitySystem.IsNpcInActivity(runtimeState))
            {
                return true;
            }

            NpcBehaviorController controller = GetComponent<NpcBehaviorController>();
            if (controller != null && controller.RequestInFlight)
            {
                return true;
            }

            return movementAgent != null && !movementAgent.CanMove;
        }

        private void ExecuteTask(NpcTask task)
        {
            if (task == null)
            {
                return;
            }

            if (task.Kind != NpcTaskKind.FollowSchedule)
            {
                UnlockConversationMovement();
            }

            if (!NpcTaskConstraintValidator.ValidateAtExecutionPoint(task, out string constraintFailure))
            {
                FailTask(task, constraintFailure);
                return;
            }

            switch (task.Kind)
            {
                case NpcTaskKind.FollowSchedule:
                case NpcTaskKind.MoveToLocation:
                case NpcTaskKind.UseActivitySpot:
                case NpcTaskKind.WorkAtLocation:
                case NpcTaskKind.RestAtLocation:
                case NpcTaskKind.JoinFestival:
                case NpcTaskKind.AttendActivity:
                    ExecuteLocationTask(task);
                    break;

                case NpcTaskKind.TalkToActor:
                    ExecuteTalkTask(task);
                    break;

                case NpcTaskKind.ReactToEvent:
                    ExecuteApproachActorTask(task);
                    break;

                case NpcTaskKind.AvoidActor:
                    ExecuteAvoidActorTask(task);
                    break;

                case NpcTaskKind.FindActor:
                    ExecuteFindActorTask(task);
                    break;

                case NpcTaskKind.FollowActor:
                    ExecuteFollowActorTask(task);
                    break;

                default:
                    break;
            }
        }

        private void ExecuteLocationTask(NpcTask task)
        {
            if (task.TargetLocation == null)
            {
                if (task.Kind == NpcTaskKind.FollowSchedule)
                {
                    return;
                }

                FailTask(task, $"Task {task.Kind} has no target location.");
                return;
            }

            if (!TryMoveToLocationTarget(
                task.TargetLocation,
                task.Kind == NpcTaskKind.FollowSchedule || task.Kind == NpcTaskKind.UseActivitySpot || task.Kind == NpcTaskKind.AttendActivity,
                out Vector3 targetPosition))
            {
                if (task.Kind != NpcTaskKind.FollowSchedule)
                {
                    FailTask(task, $"Could not move to location {task.TargetLocation.DisplayName}.");
                }

                return;
            }

            activeMovementTask = task;
            movementAgent.MoveTo(targetPosition);
            if (logExecution)
            {
                Debug.Log($"[NPC Action] {name}: task {task.Kind} moving to {task.TargetLocation.DisplayName}", this);
            }

            MovingToLocation?.Invoke(task.TargetLocation);
        }

        private void BeginReachedLocationTask(NpcTask task)
        {
            if (task == null)
            {
                return;
            }

            SyncActualLocationFromReachedTask(task);

            switch (task.Kind)
            {
                case NpcTaskKind.UseActivitySpot:
                    BeginUseActivitySpot(task);
                    break;
                case NpcTaskKind.WorkAtLocation:
                    BeginWorkAtLocation(task);
                    break;
                case NpcTaskKind.RestAtLocation:
                    BeginRestAtLocation(task);
                    break;
                case NpcTaskKind.JoinFestival:
                    BeginJoinFestival(task);
                    break;
                case NpcTaskKind.AttendActivity:
                    BeginAttendActivity(task);
                    break;
                case NpcTaskKind.MoveToLocation:
                    taskController?.CompleteCurrentTask("arrived at location");
                    break;
                case NpcTaskKind.FollowSchedule:
                default:
                break;
            }
        }

        private void SyncActualLocationFromReachedTask(NpcTask task)
        {
            if (runtimeState == null || task == null || task.TargetLocation == null)
            {
                return;
            }

            runtimeState.SetActualLocation(task.TargetLocation);
        }

        private void BeginUseActivitySpot(NpcTask task)
        {
            LogExecutionPlaceholder(task, "activity spot behavior is not implemented yet");
        }

        private void BeginWorkAtLocation(NpcTask task)
        {
            LogExecutionPlaceholder(task, "work behavior is not implemented yet");
        }

        private void BeginRestAtLocation(NpcTask task)
        {
            if (!logExecution || task == null)
            {
                return;
            }

            Debug.Log($"[NPC Action] {name}: resting at current location. task={task.Kind}, label={task.Label}", this);
        }

        private void BeginJoinFestival(NpcTask task)
        {
            LogExecutionPlaceholder(task, "festival behavior is not implemented yet");
        }

        private void BeginAttendActivity(NpcTask task)
        {
            if (activitySystem == null)
            {
                activitySystem = FindFirstObjectByType<NpcActivitySystem>();
            }

            if (activitySystem == null)
            {
                FailTask(task, "NpcActivitySystem is missing. Add it to a scene manager object to run shared activities.");
                return;
            }

            NpcActivityJoinResult result = activitySystem.TryJoinActivity(runtimeState, task, out string reason);
            if (result == NpcActivityJoinResult.Rejected)
            {
                taskController?.CompleteCurrentTask("failed: " + reason);
                return;
            }

            if (logExecution)
            {
                Debug.Log($"[NPC Action] {name}: activity {result}. key={task.ActivityKey}, reason={reason}", this);
            }
        }

        private void LogExecutionPlaceholder(NpcTask task, string note)
        {
            if (!logExecution || task == null)
            {
                return;
            }

            Debug.Log($"[NPC Action] {name}: {note}. task={task.Kind}, label={task.Label}", this);
        }

        private void ExecuteTalkTask(NpcTask task)
        {
            GameObject target = FindActorById(task.TargetActorId);
            if (target == null)
            {
                FailTask(task, $"Task target actor id '{task.TargetActorId}' was not found.");
                return;
            }

            if (target == gameObject)
            {
                StopForConversation(task);
                return;
            }

            if (!IsCloseEnoughToActor(target))
            {
                if (task.TargetLocation != null
                    && ShouldCheckExpectedLocationBeforeApproachingActor(target, task.TargetLocation)
                    && !CanCurrentlyPerceiveActor(task.TargetActorId))
                {
                    ExecuteLocationTask(task);
                    return;
                }

                MoveNearActor(target, task);
                return;
            }

            StopForConversation(task);
        }

        private void ExecuteSelfTalk(NpcAiDecision decision)
        {
            string line = decision != null ? decision.dialogue : string.Empty;
            if (!string.IsNullOrWhiteSpace(line))
            {
                messageDisplayer ??= FindFirstObjectByType<MessageDisplayer>();
                messageDisplayer?.ShowMessage(runtimeState, line);
            }

            ResumeAfterInstantDecision("self-talk finished");
        }

        private void ResumeAfterInstantDecision(string reason)
        {
            if (!EnsureTaskController())
            {
                return;
            }

            NpcTask task = taskController.CurrentTask;
            if (task != null && task.Kind != NpcTaskKind.FollowSchedule)
            {
                if (activeMovementTask == task)
                {
                    activeMovementTask = null;
                    movementAgent.Stop();
                }

                activitySpotSystem?.ReleaseSpot(runtimeState);
                taskController.ClearCurrentTask(reason);
            }

            StartFollowScheduleTask(reason);
        }

        private void ExecuteApproachActorTask(NpcTask task)
        {
            GameObject target = FindActorById(task.TargetActorId);
            if (target != null)
            {
                MoveNearActor(target, task);
                return;
            }

            if (task.TargetLocation != null)
            {
                ExecuteLocationTask(task);
                return;
            }

            FailTask(task, $"Task {task.Kind} has no reachable actor or location target.");
        }

        private void ExecuteAvoidActorTask(NpcTask task)
        {
            GameObject target = FindActorById(task.TargetActorId);
            if (target == null)
            {
                FailTask(task, $"Task target actor id '{task.TargetActorId}' was not found.");
                return;
            }

            Vector2 fromTarget = (Vector2)transform.position - (Vector2)target.transform.position;
            if (fromTarget.sqrMagnitude < 0.0001f)
            {
                fromTarget = Vector2.down;
            }

            activeMovementTask = task;
            activitySpotSystem?.ReleaseSpot(runtimeState);
            movementAgent.MoveTo((Vector2)transform.position + fromTarget.normalized * actorAvoidDistance);
            if (logExecution)
            {
                Debug.Log($"[NPC Action] {name}: avoiding actor {task.TargetActorId}, intent={task.SourceIntent}", this);
            }
        }

        private void ExecuteFindActorTask(NpcTask task)
        {
            ExecuteApproachActorTask(task);
        }

        private void ExecuteFollowActorTask(NpcTask task)
        {
            if (!ValidateFollowActorTaskAtExecution(task, out string validationFailure))
            {
                FailTask(task, validationFailure);
                return;
            }

            if (IsPlayerFollowTask(task) && !playerFollowConsentGrantedTasks.Contains(task))
            {
                RequestPlayerFollowConsent(task);
                return;
            }

            GameObject target = FindActorById(task.TargetActorId);
            if (target == null)
            {
                FailTask(task, $"Task target actor id '{task.TargetActorId}' was not found for following.");
                return;
            }

            activeMovementTask = task;
            if (activeActorPursuitTask != task)
            {
                activeActorPursuitTask = task;
                activeActorPursuitStartedRealtime = Time.realtimeSinceStartup;
            }

            UpdateFollowActorTask(task, target);
        }

        private bool PrepareFollowActorTargets(
            string targetActorId,
            ref LocationDefinition targetLocation,
            ref LocationDefinition plannedTargetLocation,
            out string failureReason)
        {
            failureReason = string.Empty;
            if (string.IsNullOrWhiteSpace(targetActorId))
            {
                failureReason = "FollowActor requires targetActorId.";
                return false;
            }

            targetActorId = targetActorId.Trim();
            if (string.Equals(targetActorId, "player", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string selfId = GetSelfActorId();
            if (!string.IsNullOrWhiteSpace(selfId) && string.Equals(targetActorId, selfId, StringComparison.OrdinalIgnoreCase))
            {
                failureReason = "FollowActor failed: an NPC cannot follow themself.";
                return false;
            }

            NpcRuntimeState leader = FindNpcById(targetActorId);
            if (leader == null || leader.Profile == null)
            {
                failureReason = $"FollowActor failed: targetActorId={targetActorId} is not a registered NPC or player.";
                return false;
            }

            if (WouldCreateFollowActorCycle(targetActorId))
            {
                failureReason = $"FollowActor failed: targetActorId={targetActorId} would create a follow cycle.";
                return false;
            }

            if (targetLocation != null || plannedTargetLocation != null)
            {
                return true;
            }

            if (TryResolveLeaderFollowDestination(leader, out LocationDefinition inheritedDestination, out _))
            {
                targetLocation = inheritedDestination;
                plannedTargetLocation = inheritedDestination;
                return true;
            }

            if (IsLeaderActivelyMoving(leader))
            {
                return true;
            }

            failureReason =
                $"FollowActor failed: targetActorId={targetActorId} is not a valid NPC leader because no destination or active movement goal is known. " +
                "Choose a location-based action, ask for clarification, FindActor for the actual subject, or continue the current schedule.";
            return false;
        }

        private bool ValidateFollowActorTaskAtExecution(NpcTask task, out string failureReason)
        {
            failureReason = string.Empty;
            if (task == null || task.Kind != NpcTaskKind.FollowActor)
            {
                return true;
            }

            string targetActorId = string.IsNullOrWhiteSpace(task.TargetActorId) ? string.Empty : task.TargetActorId.Trim();
            if (string.IsNullOrWhiteSpace(targetActorId))
            {
                failureReason = "FollowActor failed: task has no targetActorId.";
                return false;
            }

            if (string.Equals(targetActorId, "player", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string selfId = GetSelfActorId();
            if (!string.IsNullOrWhiteSpace(selfId) && string.Equals(targetActorId, selfId, StringComparison.OrdinalIgnoreCase))
            {
                failureReason = "FollowActor failed: an NPC cannot follow themself.";
                return false;
            }

            NpcRuntimeState leader = FindNpcById(targetActorId);
            if (leader == null || leader.Profile == null)
            {
                failureReason = $"FollowActor failed: targetActorId={targetActorId} is not a registered NPC.";
                return false;
            }

            if (WouldCreateFollowActorCycle(targetActorId))
            {
                failureReason = $"FollowActor failed: targetActorId={targetActorId} would create a follow cycle.";
                return false;
            }

            if (GetFollowActorDestination(task) != null)
            {
                return true;
            }

            if (task.HasRealtimeExpiry && IsLeaderActivelyMoving(leader))
            {
                return true;
            }

            failureReason =
                $"FollowActor failed: targetActorId={targetActorId} is not a valid NPC leader because no destination or active movement goal is known.";
            return false;
        }

        private bool TryResolveLeaderFollowDestination(
            NpcRuntimeState leader,
            out LocationDefinition destination,
            out string reason)
        {
            destination = null;
            reason = string.Empty;
            if (leader == null)
            {
                reason = "leader is missing";
                return false;
            }

            NpcTaskController leaderTaskController = leader.GetComponent<NpcTaskController>();
            NpcTask leaderTask = leaderTaskController != null ? leaderTaskController.CurrentTask : null;
            if (leaderTask == null)
            {
                reason = "leader has no active task";
                return false;
            }

            if (leaderTask.Kind == NpcTaskKind.FollowSchedule)
            {
                reason = "leader is only following their base schedule";
                return false;
            }

            if (leaderTask.TargetLocation != null)
            {
                destination = leaderTask.TargetLocation;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(leaderTask.PlannedTargetLocationId))
            {
                destination = ResolveTargetLocation(leaderTask.PlannedTargetLocationId);
                if (destination != null)
                {
                    return true;
                }
            }

            reason = $"leader task {leaderTask.Kind} has no explicit destination";
            return false;
        }

        private bool IsLeaderActivelyMoving(NpcRuntimeState leader)
        {
            if (leader == null)
            {
                return false;
            }

            NpcMovementAgent leaderMovement = leader.GetComponent<NpcMovementAgent>();
            if (leaderMovement == null || !leaderMovement.HasTarget)
            {
                return false;
            }

            NpcTaskController leaderTaskController = leader.GetComponent<NpcTaskController>();
            NpcTask leaderTask = leaderTaskController != null ? leaderTaskController.CurrentTask : null;
            return leaderTask == null || leaderTask.Kind != NpcTaskKind.FollowSchedule;
        }

        private bool WouldCreateFollowActorCycle(string targetActorId)
        {
            string selfId = GetSelfActorId();
            if (string.IsNullOrWhiteSpace(selfId) || string.IsNullOrWhiteSpace(targetActorId))
            {
                return false;
            }

            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string currentActorId = targetActorId.Trim();
            while (!string.IsNullOrWhiteSpace(currentActorId)
                && !string.Equals(currentActorId, "player", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(currentActorId, selfId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!visited.Add(currentActorId))
                {
                    return true;
                }

                NpcRuntimeState currentNpc = FindNpcById(currentActorId);
                NpcTaskController currentTaskController = currentNpc != null ? currentNpc.GetComponent<NpcTaskController>() : null;
                NpcTask currentTask = currentTaskController != null ? currentTaskController.CurrentTask : null;
                if (currentTask == null || currentTask.Kind != NpcTaskKind.FollowActor)
                {
                    return false;
                }

                currentActorId = string.IsNullOrWhiteSpace(currentTask.TargetActorId)
                    ? string.Empty
                    : currentTask.TargetActorId.Trim();
            }

            return false;
        }

        private bool TryMoveToLocationTarget(LocationDefinition location, bool mayUseActivitySpot, out Vector3 targetPosition)
        {
            targetPosition = transform.position;
            if (!movementAgent.CanMove)
            {
                return false;
            }

            if (locationSystem == null)
            {
                ReportFailure("No LocationSystem found.");
                return false;
            }

            if (!locationSystem.TryGetMarker(location, out LocationMarker marker))
            {
                ReportFailure($"No LocationMarker registered for location '{location.DisplayName}'.");
                return false;
            }

            targetPosition = marker.GetEntryPosition();
            if (mayUseActivitySpot && activitySpotSystem != null && activitySpotSystem.TryAssignSpot(runtimeState, location, out ActivitySpot spot))
            {
                targetPosition = spot.GetUsePosition();
                movementAgent.Face(spot.GetFacePosition());
            }
            else
            {
                activitySpotSystem?.ReleaseSpot(runtimeState);
            }

            return true;
        }

        private void MoveNearActor(GameObject target, NpcTask task)
        {
            MoveNearActor(target, task, actorApproachDistance);
        }

        private void MoveNearActor(GameObject target, NpcTask task, float desiredDistance)
        {
            if (activeActorPursuitTask != task)
            {
                activeActorPursuitTask = task;
                activeActorPursuitStartedRealtime = Time.realtimeSinceStartup;
            }

            activeMovementTask = task;
            activitySpotSystem?.ReleaseSpot(runtimeState);
            movementAgent.MoveTo(GetApproachPosition(target.transform.position, desiredDistance));
            movementAgent.Face(target.transform.position);
            nextActorRetargetRealtime = Time.realtimeSinceStartup + actorRetargetInterval;

            if (logExecution)
            {
                Debug.Log($"[NPC Action] {name}: moving near actor {task.TargetActorId}, task={task.Kind}", this);
            }
        }

        private Vector2 GetApproachPosition(Vector3 targetPosition)
        {
            return GetApproachPosition(targetPosition, actorApproachDistance);
        }

        private Vector2 GetApproachPosition(Vector3 targetPosition, float desiredDistance)
        {
            Vector2 fromTarget = (Vector2)transform.position - (Vector2)targetPosition;
            if (fromTarget.sqrMagnitude < 0.0001f)
            {
                fromTarget = Vector2.down;
            }

            return (Vector2)targetPosition + fromTarget.normalized * Mathf.Max(0.05f, desiredDistance);
        }

        private bool IsCloseEnoughToActor(GameObject target)
        {
            return IsCloseEnoughToActor(target, GetActorConversationDistance());
        }

        private bool IsCloseEnoughToActor(GameObject target, float distance)
        {
            return target != null && Vector2.Distance(transform.position, target.transform.position) <= distance;
        }

        private bool IsCloseEnoughToPosition(Vector2 position, float distance)
        {
            return Vector2.Distance(transform.position, position) <= distance;
        }

        private float GetActorConversationDistance()
        {
            return conversationArbiter != null
                ? conversationArbiter.OneOnOneStartDistance
                : actorApproachDistance + 0.15f;
        }

        private bool CanCurrentlyPerceiveActor(string actorId)
        {
            if (string.IsNullOrWhiteSpace(actorId))
            {
                return false;
            }

            if (perceptionSensor == null)
            {
                perceptionSensor = GetComponent<NpcPerceptionSensor>();
            }

            return perceptionSensor != null && perceptionSensor.CanCurrentlyPerceive(actorId);
        }

        private void UpdateFollowActorTask(NpcTask task, GameObject target)
        {
            if (task == null || taskController == null || taskController.CurrentTask != task)
            {
                return;
            }

            if (IsFollowReleaseTask(task))
            {
                UpdateFollowReleaseTask(task);
                return;
            }

            if (target == null)
            {
                FailTask(task, $"Task target actor id '{task.TargetActorId}' was not found for following.");
                return;
            }

            if (HasFollowActorReachedDestination(task, target))
            {
                CompleteFollowActorTask(task, target);
                return;
            }

            activeMovementTask = task;
            if (activeActorPursuitTask != task)
            {
                activeActorPursuitTask = task;
                activeActorPursuitStartedRealtime = Time.realtimeSinceStartup;
            }

            float desiredDistance = Mathf.Max(followActorDistance, actorApproachDistance);
            if (IsCloseEnoughToActor(target, desiredDistance))
            {
                movementAgent?.Stop();
                movementAgent?.Face(target.transform.position);
                return;
            }

            if (Time.realtimeSinceStartup < nextActorRetargetRealtime
                && movementAgent != null
                && movementAgent.HasTarget)
            {
                return;
            }

            MoveNearActor(target, task, desiredDistance);
        }

        private void UpdateFollowReleaseTask(NpcTask task)
        {
            if (task == null || taskController == null || taskController.CurrentTask != task || !IsFollowReleaseTask(task))
            {
                return;
            }

            if (IsCloseEnoughToPosition(followReleaseDestination, Mathf.Max(followActorDistance, actorApproachDistance)))
            {
                CompleteFollowActorTask(task, null);
                return;
            }

            if (Time.realtimeSinceStartup < nextActorRetargetRealtime
                && movementAgent != null
                && movementAgent.HasTarget)
            {
                return;
            }

            activeMovementTask = task;
            if (activeActorPursuitTask != task)
            {
                activeActorPursuitTask = task;
                activeActorPursuitStartedRealtime = Time.realtimeSinceStartup;
            }

            activitySpotSystem?.ReleaseSpot(runtimeState);
            movementAgent.MoveTo(followReleaseDestination);
            movementAgent.Face(followReleaseDestination);
            nextActorRetargetRealtime = Time.realtimeSinceStartup + actorRetargetInterval;

            if (logExecution)
            {
                Debug.Log($"[NPC Action] {name}: finishing released follow by moving to {followReleaseDestination}.", this);
            }
        }

        private bool IsFollowReleaseTask(NpcTask task)
        {
            return task != null && followReleaseTask == task;
        }

        private bool HasFollowActorReachedDestination(NpcTask task, GameObject target)
        {
            LocationDefinition destination = GetFollowActorDestination(task);
            if (destination == null || target == null)
            {
                return false;
            }

            return IsActorAtLocation(task.TargetActorId, target, destination)
                && IsSelfAtLocation(destination)
                && IsCloseEnoughToActor(target, Mathf.Max(followActorDestinationDistance, followActorDistance));
        }

        private LocationDefinition GetFollowActorDestination(NpcTask task)
        {
            if (task == null)
            {
                return null;
            }

            if (task.TargetLocation != null)
            {
                return task.TargetLocation;
            }

            return ResolveTargetLocation(task.PlannedTargetLocationId);
        }

        private bool IsActorAtLocation(string actorId, GameObject actor, LocationDefinition location)
        {
            if (location == null)
            {
                return false;
            }

            if (string.Equals(actorId, "player", StringComparison.OrdinalIgnoreCase))
            {
                return locationSystem != null && IsSameLocation(locationSystem.CurrentLocation, location);
            }

            NpcRuntimeState actorState = actor != null ? actor.GetComponent<NpcRuntimeState>() : FindNpcById(actorId);
            return actorState != null && IsSameLocation(actorState.ActualLocation, location);
        }

        private bool IsSelfAtLocation(LocationDefinition location)
        {
            if (location == null)
            {
                return false;
            }

            if (runtimeState != null && IsSameLocation(runtimeState.ActualLocation, location))
            {
                return true;
            }

            if (locationSystem != null && locationSystem.TryGetMarker(location, out LocationMarker marker) && marker != null)
            {
                return Vector2.Distance(transform.position, marker.GetEntryPosition()) <= followActorDestinationDistance;
            }

            return false;
        }

        private static bool IsSameLocation(LocationDefinition first, LocationDefinition second)
        {
            if (first == null || second == null)
            {
                return false;
            }

            return first == second
                || string.Equals(first.LocationId, second.LocationId, StringComparison.OrdinalIgnoreCase);
        }

        private bool TryContinueActorPursuitAfterStaleArrival(NpcTask task)
        {
            if (!ShouldDynamicallyPursueActor(task))
            {
                return false;
            }

            GameObject target = FindActorById(task.TargetActorId);
            if (target == null || IsCloseEnoughToActor(target))
            {
                return false;
            }

            if (Time.realtimeSinceStartup - activeActorPursuitStartedRealtime > actorPursuitMaxSeconds)
            {
                return false;
            }

            MoveNearActor(target, task);
            if (logExecution)
            {
                Debug.Log($"[NPC Action] {name}: actor target {task.TargetActorId} moved before arrival; continuing pursuit.", this);
            }

            return true;
        }

        private static bool IsActorTargetTask(NpcTask task)
        {
            return task != null
                && !string.IsNullOrWhiteSpace(task.TargetActorId)
                && (!string.Equals(task.TargetActorId, "player", StringComparison.OrdinalIgnoreCase)
                    || task.Kind == NpcTaskKind.FindActor
                    || task.Kind == NpcTaskKind.FollowActor)
                && (task.Kind == NpcTaskKind.TalkToActor
                    || task.Kind == NpcTaskKind.ReactToEvent
                    || task.Kind == NpcTaskKind.FindActor
                    || task.Kind == NpcTaskKind.FollowActor);
        }

        private bool ShouldDynamicallyPursueActor(NpcTask task)
        {
            if (task != null && task.Kind == NpcTaskKind.FollowActor)
            {
                return true;
            }

            if (task != null
                && (task.Kind == NpcTaskKind.FindActor
                    || task.Kind == NpcTaskKind.ReactToEvent))
            {
                return true;
            }

            return IsActorTargetTask(task)
                && (task.TargetLocation == null || CanCurrentlyPerceiveActor(task.TargetActorId));
        }

        private static bool ShouldCheckExpectedLocationBeforeApproachingActor(GameObject target, LocationDefinition expectedLocation)
        {
            if (target == null || expectedLocation == null)
            {
                return false;
            }

            NpcRuntimeState targetNpc = target.GetComponent<NpcRuntimeState>();
            if (targetNpc == null)
            {
                return false;
            }

            return !IsSameLocation(targetNpc.ActualLocation, expectedLocation);
        }

        private void StopForConversation(NpcTask task)
        {
            if (!stopWhenTalking)
            {
                return;
            }

            if (string.Equals(task.TargetActorId, "player", StringComparison.OrdinalIgnoreCase) && !IsInPlayerConversation())
            {
                CompletePlayerTalkTask(task);
                return;
            }

            if (conversationArbiter == null)
            {
                conversationArbiter = ConversationArbiter.GetOrCreate();
            }

            if (!string.Equals(task.TargetActorId, "player", StringComparison.OrdinalIgnoreCase)
                && conversationArbiter != null
                && task.Kind == NpcTaskKind.TalkToActor)
            {
                NpcRuntimeState targetForConversation = FindNpcById(task.TargetActorId);
                if (targetForConversation != null)
                {
                    bool started = conversationArbiter.TryStartOneOnOneNow(
                        runtimeState,
                        targetForConversation,
                        task.Label,
                        BuildConversationReasonWithPlanContext(task),
                        task.Priority,
                        false,
                        BuildDialogueContextFromTask(task),
                        out ConversationArbiter.ConversationStartFailureReason failureReason);
                    if (started)
                    {
                        taskController?.ClearCurrentTask("conversation handed to arbiter");
                        return;
                    }

                    if (ShouldRetryConversationStart(failureReason))
                    {
                        StartCoroutine(RetryStartConversationTask(task, failureReason));
                        return;
                    }

                    if (logExecution)
                    {
                        Debug.Log(
                            $"[NPC Action] {name}: could not start NPC conversation with {task.TargetActorId}; reason={failureReason}; resolving task as failed conversation.",
                            this);
                    }

                    taskController?.CompleteCurrentTask("failed: " + BuildConversationFailureReason(task, failureReason));
                    return;
                }

                taskController?.CompleteCurrentTask("failed: " + BuildConversationFailureReason(
                    task,
                    ConversationArbiter.ConversationStartFailureReason.TargetMissing));
                return;
            }

            movementAgent.SetPause(ConversationPauseReason, true);

            NpcRuntimeState targetNpc = FindNpcById(task.TargetActorId);
            if (targetNpc == null)
            {
                return;
            }

            NpcMovementAgent targetMovement = targetNpc.GetComponent<NpcMovementAgent>();
            conversationTargetMovement = targetMovement;
            targetMovement?.SetPause(ConversationPauseReason, true);
            movementAgent.Face(targetNpc.transform.position);
            targetMovement?.Face(transform.position);

            if (unlockConversationCoroutine != null)
            {
                StopCoroutine(unlockConversationCoroutine);
            }

            unlockConversationCoroutine = StartCoroutine(UnlockNpcConversationAfterDelay(CalculateConversationSeconds(task.Dialogue)));
        }

        private void FaceTaskTarget(NpcTask task)
        {
            if (task == null || string.IsNullOrWhiteSpace(task.TargetActorId))
            {
                return;
            }

            GameObject target = FindActorById(task.TargetActorId);
            if (target != null)
            {
                movementAgent.Face(target.transform.position);
            }
        }

        private static string BuildConversationReasonWithPlanContext(NpcTask task)
        {
            if (task == null)
            {
                return string.Empty;
            }

            string reason = string.IsNullOrWhiteSpace(task.Reason) ? task.Dialogue : task.Reason;
            bool hasPlanContext = !string.IsNullOrWhiteSpace(task.PlannedTargetLocationId)
                || !string.IsNullOrWhiteSpace(task.ActivityKind)
                || HasAnyActorId(task.ParticipantActorIds)
                || HasAnyActorId(task.RequiredActorIds)
                || HasAnyActorId(task.OptionalActorIds);
            if (!hasPlanContext)
            {
                return reason;
            }

            return
                reason + " " +
                "Shared plan context carried by the current task: " +
                $"plannedTargetLocationId={task.PlannedTargetLocationId}; " +
                $"activityKind={task.ActivityKind}; " +
                $"activityKey={task.ActivityKey}; " +
                $"participantActorIds={JoinIds(task.ParticipantActorIds)}; " +
                $"requiredActorIds={JoinIds(task.RequiredActorIds)}; " +
                $"optionalActorIds={JoinIds(task.OptionalActorIds)}; " +
                $"patienceMinutes={task.PatienceMinutes}.";
        }

        private DialogueContextInfo BuildDialogueContextFromTask(NpcTask task)
        {
            if (task == null)
            {
                return null;
            }

            string contextKind = string.IsNullOrWhiteSpace(task.DialogueContextKind)
                ? $"{task.Kind}_task"
                : task.DialogueContextKind;
            string sourceActorId = string.IsNullOrWhiteSpace(task.DialogueSourceActorId)
                ? GetSelfActorId()
                : task.DialogueSourceActorId;
            string subjectActorId = string.IsNullOrWhiteSpace(task.DialogueSubjectActorId)
                ? InferSubjectActorId(task)
                : task.DialogueSubjectActorId;
            string subjectLocationId = string.IsNullOrWhiteSpace(task.DialogueSubjectLocationId)
                ? task.TargetLocation != null ? task.TargetLocation.LocationId : task.PlannedTargetLocationId
                : task.DialogueSubjectLocationId;

            return new DialogueContextInfo(
                contextKind,
                sourceActorId,
                subjectActorId,
                subjectLocationId,
                task.DialogueSourceText,
                BuildConversationReasonWithPlanContext(task),
                task.ActivityKey);
        }

        private string InferSubjectActorId(NpcTask task)
        {
            if (task == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(task.TargetActorId)
                && !string.Equals(task.TargetActorId, "player", StringComparison.OrdinalIgnoreCase))
            {
                return task.TargetActorId.Trim();
            }

            string selfId = GetSelfActorId();
            string participant = FirstOtherNpcId(selfId, task.RequiredActorIds);
            if (!string.IsNullOrWhiteSpace(participant))
            {
                return participant;
            }

            participant = FirstOtherNpcId(selfId, task.ParticipantActorIds);
            if (!string.IsNullOrWhiteSpace(participant))
            {
                return participant;
            }

            return FirstOtherNpcId(selfId, task.OptionalActorIds);
        }

        private string GetSelfActorId()
        {
            return runtimeState != null && runtimeState.Profile != null
                ? runtimeState.Profile.NpcId
                : string.Empty;
        }

        private static string FirstOtherNpcId(string selfId, string[] actorIds)
        {
            if (actorIds == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < actorIds.Length; i++)
            {
                string actorId = string.IsNullOrWhiteSpace(actorIds[i]) ? string.Empty : actorIds[i].Trim();
                if (string.IsNullOrWhiteSpace(actorId)
                    || string.Equals(actorId, "player", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(actorId, selfId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return actorId;
            }

            return string.Empty;
        }

        private bool TryStartCurrentDailyIntentTask()
        {
            if (!routeSharedDailyIntentsThroughActivitySystem || scheduleSystem == null)
            {
                return false;
            }

            if (conversationArbiter != null && conversationArbiter.IsNpcInConversation(runtimeState))
            {
                return true;
            }

            if (IsInPlayerConversation())
            {
                return true;
            }

            if (activitySystem != null && activitySystem.IsNpcInActivity(runtimeState))
            {
                return true;
            }

            NpcBehaviorController controller = GetComponent<NpcBehaviorController>();
            if (controller != null && controller.RequestInFlight)
            {
                return true;
            }

            if (taskController != null && taskController.HasNonScheduleTask)
            {
                return true;
            }

            if (movementAgent != null && !movementAgent.CanMove)
            {
                return true;
            }

            NpcDailyIntent intent = GetCurrentDailyIntent();
            if (intent == null)
            {
                return false;
            }

            string intentKey = BuildDailyIntentKey(intent);
            if (attemptedDailyIntentKeys.Contains(intentKey))
            {
                return false;
            }

            LocationDefinition targetLocation = ResolveTargetLocation(intent.TargetLocationId);
            if (ShouldTreatIntentAsActivity(intent, targetLocation))
            {
                return TryStartDailyActivity(intent, targetLocation);
            }

            if (!TryCreateTaskFromDailyIntent(intent, targetLocation, out NpcTask task))
            {
                return false;
            }

            if (!EnsureTaskController() || !taskController.TryStartTask(task))
            {
                return false;
            }

            attemptedDailyIntentKeys.Add(intentKey);
            if (logExecution)
            {
                Debug.Log($"[NPC Action] {name}: started daily intent task. key={intentKey}, kind={task.Kind}, targetActor={task.TargetActorId}, targetLocation={(task.TargetLocation != null ? task.TargetLocation.LocationId : string.Empty)}", this);
            }

            return true;
        }

        private bool TryStartDailyActivity(NpcDailyIntent intent, LocationDefinition location)
        {
            if (location == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(intent.ActivityKind)
                || !location.TryGetAvailableTaskTemplate(intent.ActivityKind, NpcTaskKind.AttendActivity.ToString(), out _))
            {
                if (logExecution)
                {
                    Debug.Log(
                        $"[NPC Action] {name}: daily intent activity is unsupported. " +
                        $"activityKind={intent.ActivityKind}, targetLocation={location.LocationId}, " +
                        $"allowed={location.BuildAvailableTaskTemplateList(NpcTaskKind.AttendActivity.ToString())}",
                        this);
                }

                return false;
            }

            string[] participantIds = BuildActivityParticipantIds(intent, null, location);
            string activityKind = intent.ActivityKind.Trim();
            string activityKey = BuildActivityKey(intent, location, activityKind, participantIds);
            if (completedActivityKeys.Contains(activityKey))
            {
                return false;
            }

            if (!EnsureTaskController())
            {
                return false;
            }

            NpcTask currentTask = taskController.CurrentTask;
            if (currentTask != null
                && currentTask.Kind == NpcTaskKind.AttendActivity
                && string.Equals(currentTask.ActivityKey, activityKey, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            NpcTask activityTask = new NpcTask(
                string.IsNullOrWhiteSpace(intent.Label) ? "Attend activity" : intent.Label,
                NpcTaskKind.AttendActivity,
                location,
                string.IsNullOrWhiteSpace(intent.TargetActorId) ? string.Empty : intent.TargetActorId.Trim(),
                intent.Priority,
                intent.CanInterruptRoutine,
                false,
                string.IsNullOrWhiteSpace(intent.Reason) ? intent.DesiredOutcome : intent.Reason,
                -1f,
                NpcIntentType.AttendActivity.ToString(),
                NpcEventKind.ScheduleOverride.ToString(),
                intent.DesiredOutcome,
                activityKind,
                activityKey,
                participantIds,
                BuildActivityRequiredIds(intent, null, participantIds),
                CleanActorIds(intent.OptionalActorIds),
                intent.PatienceMinutes > 0 ? intent.PatienceMinutes : 20);

            if (!NpcTaskConstraintValidator.ValidateAtExecutionPoint(activityTask, out string constraintFailure))
            {
                if (logExecution)
                {
                    Debug.Log($"[NPC Action] {name}: daily activity rejected by constraints. {constraintFailure}", this);
                }

                return false;
            }

            return taskController.TryStartTask(activityTask);
        }

        private bool TryCreateTaskFromDailyIntent(NpcDailyIntent intent, LocationDefinition targetLocation, out NpcTask task)
        {
            task = null;
            if (intent == null)
            {
                return false;
            }

            string targetActorId = string.IsNullOrWhiteSpace(intent.TargetActorId) ? string.Empty : intent.TargetActorId.Trim();
            bool hasActorTarget = !string.IsNullOrWhiteSpace(targetActorId);
            bool hasLocationTarget = targetLocation != null;
            if (!hasActorTarget && !hasLocationTarget)
            {
                return false;
            }

            NpcTaskKind kind = hasActorTarget ? NpcTaskKind.TalkToActor : NpcTaskKind.MoveToLocation;
            string label = string.IsNullOrWhiteSpace(intent.Label) ? "Daily intent" : intent.Label;
            string reason = string.IsNullOrWhiteSpace(intent.Reason) ? intent.DesiredOutcome : intent.Reason;
            string dialogue = string.IsNullOrWhiteSpace(intent.DesiredOutcome) ? reason : intent.DesiredOutcome;

            task = new NpcTask(
                label,
                kind,
                targetLocation,
                targetActorId,
                intent.Priority,
                intent.CanInterruptRoutine,
                kind != NpcTaskKind.TalkToActor,
                reason,
                -1f,
                kind == NpcTaskKind.TalkToActor ? NpcIntentType.TalkToNpc.ToString() : NpcIntentType.MoveToLocation.ToString(),
                kind == NpcTaskKind.TalkToActor ? NpcEventKind.None.ToString() : NpcEventKind.OneShot.ToString(),
                dialogue);
            return true;
        }

        private NpcDailyIntent GetCurrentDailyIntent()
        {
            if (scheduleSystem == null)
            {
                return null;
            }

            if (scheduleAgent == null)
            {
                scheduleAgent = GetComponent<NpcScheduleAgent>();
            }

            return scheduleSystem.GetCurrentIntent(scheduleAgent);
        }

        private bool ShouldTreatIntentAsActivity(NpcDailyIntent intent, LocationDefinition targetLocation)
        {
            if (intent == null || targetLocation == null || string.IsNullOrWhiteSpace(intent.ActivityKind))
            {
                return false;
            }

            string[] participants = BuildActivityParticipantIds(intent, null);
            if (CountNpcParticipants(participants) < 2)
            {
                return false;
            }

            return targetLocation.TryGetAvailableTaskTemplate(
                intent.ActivityKind,
                NpcTaskKind.AttendActivity.ToString(),
                out _);
        }

        private string[] BuildActivityParticipantIds(NpcDailyIntent intent, NpcAiDecision decision, LocationDefinition location = null)
        {
            List<string> ids = new List<string>();
            AddActorId(ids, runtimeState != null && runtimeState.Profile != null ? runtimeState.Profile.NpcId : string.Empty);
            AddActorIds(ids, intent != null ? intent.ParticipantActorIds : null);
            AddActorIds(ids, intent != null ? intent.RequiredActorIds : null);
            AddActorIds(ids, intent != null ? intent.OptionalActorIds : null);
            AddActorIds(ids, decision != null ? decision.participantActorIds : null);
            AddActorIds(ids, decision != null ? decision.requiredActorIds : null);
            AddActorIds(ids, decision != null ? decision.optionalActorIds : null);
            AddActorId(ids, intent != null ? intent.TargetActorId : string.Empty);
            AddActorId(ids, decision != null ? decision.targetActorId : string.Empty);

            string locationText = location != null ? $"{location.LocationId} {location.DisplayName} {location.Description}" : string.Empty;
            string text = (intent != null ? BuildIntentSearchText(intent) : string.Empty) + " " + locationText + " " +
                (decision != null ? $"{decision.dialogue} {decision.nextActionPreference} {decision.plannedTargetLocationId} {decision.activityKind}" : string.Empty);
            NpcRuntimeState[] npcs = FindObjectsByType<NpcRuntimeState>(FindObjectsSortMode.None);
            for (int i = 0; i < npcs.Length; i++)
            {
                NpcRuntimeState npc = npcs[i];
                NpcProfile profile = npc != null ? npc.Profile : null;
                if (profile == null || string.IsNullOrWhiteSpace(profile.NpcId))
                {
                    continue;
                }

                if (ContainsLoose(text, profile.NpcId) || ContainsLoose(text, profile.DisplayName))
                {
                    AddActorId(ids, profile.NpcId);
                }
            }

            ids.Sort(StringComparer.OrdinalIgnoreCase);
            return ids.ToArray();
        }

        private string[] BuildActivityRequiredIds(NpcDailyIntent intent, NpcAiDecision decision, string[] participantIds)
        {
            string[] decisionRequired = CleanActorIds(decision != null ? decision.requiredActorIds : null);
            if (decisionRequired.Length > 0)
            {
                return decisionRequired;
            }

            string[] required = CleanActorIds(intent != null ? intent.RequiredActorIds : null);
            if (required.Length > 0)
            {
                return required;
            }

            return CleanActorIds(participantIds);
        }

        private string[] BuildActivityOptionalIds(NpcDailyIntent intent, NpcAiDecision decision)
        {
            List<string> ids = new List<string>();
            AddActorIds(ids, intent != null ? intent.OptionalActorIds : null);
            AddActorIds(ids, decision != null ? decision.optionalActorIds : null);
            ids.Sort(StringComparer.OrdinalIgnoreCase);
            return ids.ToArray();
        }

        private static bool HasSharedPlanMetadata(NpcAiDecision decision)
        {
            if (decision == null)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(decision.plannedTargetLocationId)
                || !string.IsNullOrWhiteSpace(decision.activityKind)
                || HasAnyActorId(decision.participantActorIds)
                || HasAnyActorId(decision.requiredActorIds)
                || HasAnyActorId(decision.optionalActorIds);
        }

        private static bool HasAnyActorId(string[] ids)
        {
            if (ids == null)
            {
                return false;
            }

            for (int i = 0; i < ids.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(ids[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static string JoinIds(string[] ids)
        {
            return ids == null || ids.Length == 0 ? "" : string.Join(",", ids);
        }

        private string BuildActivityKey(NpcDailyIntent intent, LocationDefinition location, string activityKind, string[] participantIds)
        {
            GameDate date = clock != null ? clock.CurrentDate : new GameDate(1, 1, 1);
            string locationId = location != null ? location.LocationId : "none";
            string window = intent != null ? $"{intent.EarliestStart}-{intent.LatestEnd}" : "now";
            return $"{date.Key}:{locationId}:{NormalizeLoose(activityKind)}:{NormalizeLoose(window)}:{string.Join(",", CleanActorIds(participantIds))}";
        }

        private string BuildDailyIntentKey(NpcDailyIntent intent)
        {
            if (intent == null)
            {
                return string.Empty;
            }

            GameDate date = clock != null ? clock.CurrentDate : new GameDate(1, 1, 1);
            string window = $"{intent.EarliestStart}-{intent.LatestEnd}";
            return
                $"{date.Key}:" +
                $"{NormalizeLoose(window)}:" +
                $"{NormalizeLoose(intent.Label)}:" +
                $"{NormalizeLoose(intent.TargetActorId)}:" +
                $"{NormalizeLoose(intent.TargetLocationId)}:" +
                $"{NormalizeLoose(intent.DesiredOutcome)}:" +
                $"{NormalizeLoose(intent.CompletionCondition)}";
        }

        private string BuildDelayedTaskKey(NpcTask task, GameDate dueDate, GameTime dueTime)
        {
            if (task == null)
            {
                return string.Empty;
            }

            string target = !string.IsNullOrWhiteSpace(task.TargetActorId)
                ? $"actor:{task.TargetActorId}"
                : task.TargetLocation != null
                    ? $"location:{task.TargetLocation.LocationId}"
                    : "none";
            string location = task.TargetLocation != null ? task.TargetLocation.LocationId : string.Empty;
            return
                $"{dueDate.Key}:{dueTime.TotalMinutes}:" +
                $"{task.Kind}:{target}:at:{location}:{NormalizeLoose(task.SourceIntent)}:{NormalizeLoose(task.Reason)}";
        }

        private bool IsDelayedTaskPending(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            for (int i = 0; i < delayedTasks.Count; i++)
            {
                DelayedNpcTask task = delayedTasks[i];
                if (task != null && string.Equals(task.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsDueNowOrPast(GameDate dueDate, GameTime dueTime)
        {
            if (clock == null)
            {
                return true;
            }

            int dateComparison = CompareDates(dueDate, clock.CurrentDate);
            if (dateComparison < 0)
            {
                return true;
            }

            if (dateComparison > 0)
            {
                return false;
            }

            return dueTime.TotalMinutes <= clock.CurrentTime.TotalMinutes;
        }

        private static int CompareDates(GameDate left, GameDate right)
        {
            return left.CompareTo(right);
        }

        private static string BuildIntentSearchText(NpcDailyIntent intent)
        {
            if (intent == null)
            {
                return string.Empty;
            }

            return $"{intent.Label} {intent.ActivityKind} {intent.TargetActorId} {intent.DesiredOutcome} {intent.AllowedBehaviors} {intent.CompletionCondition} {intent.Reason}";
        }

        private static string[] CleanActorIds(string[] ids)
        {
            if (ids == null || ids.Length == 0)
            {
                return Array.Empty<string>();
            }

            List<string> cleaned = new List<string>();
            for (int i = 0; i < ids.Length; i++)
            {
                AddActorId(cleaned, ids[i]);
            }

            cleaned.Sort(StringComparer.OrdinalIgnoreCase);
            return cleaned.ToArray();
        }

        private static void AddActorIds(List<string> ids, string[] values)
        {
            if (values == null)
            {
                return;
            }

            for (int i = 0; i < values.Length; i++)
            {
                AddActorId(ids, values[i]);
            }
        }

        private static void AddActorId(List<string> ids, string id)
        {
            id = string.IsNullOrWhiteSpace(id) ? string.Empty : id.Trim();
            if (string.IsNullOrWhiteSpace(id) || string.Equals(id, "player", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            for (int i = 0; i < ids.Count; i++)
            {
                if (string.Equals(ids[i], id, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            ids.Add(id);
        }

        private static int CountNpcParticipants(string[] ids)
        {
            return ids != null ? ids.Length : 0;
        }

        private LocationDefinition ResolveTargetLocation(NpcAiDecision decision)
        {
            if (decision == null || string.IsNullOrWhiteSpace(decision.targetLocationId))
            {
                return null;
            }

            return ResolveTargetLocation(decision.targetLocationId);
        }

        private LocationDefinition ResolvePlannedTargetLocation(NpcAiDecision decision)
        {
            if (decision == null || string.IsNullOrWhiteSpace(decision.plannedTargetLocationId))
            {
                return null;
            }

            return ResolveTargetLocation(decision.plannedTargetLocationId);
        }

        private LocationDefinition ResolveTargetLocation(string locationId)
        {
            if (string.IsNullOrWhiteSpace(locationId))
            {
                return null;
            }

            if (locationSystem == null)
            {
                ReportFailure("No LocationSystem found for AI target location.");
                return null;
            }

            if (!locationSystem.TryGetMarker(locationId.Trim(), out LocationMarker marker) || marker.Definition == null)
            {
                ReportFailure($"AI target location id '{locationId}' has no registered marker.");
                return null;
            }

            return marker.Definition;
        }

        private void RequestDecisionAfterTask(NpcTask completedTask, string reason)
        {
            NpcBehaviorController controller = GetComponent<NpcBehaviorController>();
            if (controller == null)
            {
                ResolveCurrentScheduleAfterDecision();
                return;
            }

            if (controller.RequestInFlight)
            {
                ScheduleDecisionAfterControllerIdle(controller, completedTask, reason);
                return;
            }

            WriteTaskResultMemory(completedTask, reason);
            RememberFailedTask(completedTask, reason);
            RecordCompletedRollingGoalResult(completedTask, reason);
            string failureContext = BuildTaskFailureContext(completedTask, reason);
            string summary =
                "A real-time task just ended. " +
                $"Task kind={completedTask.Kind}, sourceIntent={completedTask.SourceIntent}, targetActor={completedTask.TargetActorId}, " +
                $"targetLocation={(completedTask.TargetLocation != null ? completedTask.TargetLocation.LocationId : string.Empty)}, endReason={reason}. " +
                $"Original reason: {completedTask.Reason}. " +
                $"Rolling goal context: {BuildRollingGoalSummary()}. " +
                BuildCompletedTaskAnchorContext(completedTask, reason) +
                failureContext +
                $"Current direct perception after reaching/reacting: {BuildImmediatePerceptionSummary()}. " +
                "This attempt has ended; endReason tells whether it succeeded or failed. Repeating the same check-in, question, greeting, or approach is useful only when new evidence justifies escalation. " +
                "Decide what to do next based on urgency and current obligations. If nothing else matters, continue the normal schedule.";

            if (completedTask.OneShot)
            {
                bool requested = controller.TryRequestPreviewDecision(
                    summary,
                    decision =>
                    {
                        PrepareDecisionAfterTask(completedTask, decision);
                        if (ShouldIgnorePostOneShotDecision(completedTask, decision))
                        {
                            StartFollowScheduleTask("ignored repeated one-shot follow-up");
                            return;
                        }

                        behaviorState?.ApplyDecision(decision);
                    },
                    _ => ResolveCurrentScheduleAfterDecision());

                if (!requested)
                {
                    ResolveCurrentScheduleAfterDecision();
                }

                return;
            }

            controller.SetObservedEventSummary(summary);
            controller.ForceRequestDecision();
        }

        private string BuildCompletedTaskAnchorContext(NpcTask completedTask, string reason)
        {
            if (completedTask == null)
            {
                return string.Empty;
            }

            string targetActorId = string.IsNullOrWhiteSpace(completedTask.TargetActorId)
                ? "(none)"
                : completedTask.TargetActorId.Trim();
            string result = IsFailureReason(reason) ? "failed" : "completed";
            if (completedTask.Kind == NpcTaskKind.FollowActor)
            {
                string destination = completedTask.TargetLocation != null
                    ? completedTask.TargetLocation.LocationId
                    : completedTask.PlannedTargetLocationId;
                return
                    $"FollowActor result anchor: result={result}, followedTargetActorId={targetActorId}, destinationLocationId={destination}, followResult={reason}. " +
                    "A successful FollowActor means the NPC stayed with the target until the follow task ended or the destination was reached. " +
                    "If the NPC followed the player to a destination, decide what to do at that destination now. " +
                    "Non-dialogue intents keep dialogue empty. ";
            }

            if (completedTask.Kind != NpcTaskKind.FindActor)
            {
                return string.Empty;
            }

            return
                $"FindActor result anchor: result={result}, foundOrSearchedTargetActorId={targetActorId}, findAndObservationResult={reason}. " +
                "A successful FindActor means the NPC located, approached, and inspected this target's visible/audible state. " +
                "This observation is direct evidence about foundOrSearchedTargetActorId. " +
                "The player is a reporter/listener unless targetActorId=player, so reports to the player should name the found target in third person. " +
                "Non-dialogue intents keep dialogue empty. ";
        }

        private void RecordCompletedRollingGoalResult(NpcTask completedTask, string reason)
        {
            if (completedTask == null)
            {
                return;
            }

            string taskOriginalGoal = CleanGoalText(completedTask.OriginalGoal);
            if (string.IsNullOrWhiteSpace(taskOriginalGoal) && string.IsNullOrWhiteSpace(activeOriginalGoal))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(activeOriginalGoal))
            {
                activeOriginalGoal = taskOriginalGoal;
            }

            if (string.IsNullOrWhiteSpace(activeRollingPlanId) && !string.IsNullOrWhiteSpace(completedTask.RollingPlanId))
            {
                activeRollingPlanId = completedTask.RollingPlanId;
            }

            if (!string.IsNullOrWhiteSpace(completedTask.CurrentGoal))
            {
                activeCurrentGoal = completedTask.CurrentGoal;
            }

            string line = BuildCompletedRollingGoalResultLine(completedTask, reason);
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            completedGoalResults = AppendBoundedLine(completedGoalResults, line, 8);
            rollingPlanRevision = Mathf.Max(1, rollingPlanRevision + 1);

            if (logExecution)
            {
                Debug.Log($"[NPC Plan] {name}: task result appended. {line}", this);
            }
        }

        private string BuildCompletedRollingGoalResultLine(NpcTask task, string reason)
        {
            if (task == null)
            {
                return string.Empty;
            }

            string targetLocationId = task.TargetLocation != null ? task.TargetLocation.LocationId : string.Empty;
            string targetActorId = string.IsNullOrWhiteSpace(task.TargetActorId) ? string.Empty : task.TargetActorId.Trim();
            string result = IsFailureReason(reason) ? "failed" : "succeeded";
            StringBuilder builder = new StringBuilder();
            builder.Append(result);
            builder.Append(": ");
            builder.Append(task.Kind);
            if (!string.IsNullOrWhiteSpace(targetActorId))
            {
                builder.Append(" targetActorId=");
                builder.Append(targetActorId);
            }

            if (!string.IsNullOrWhiteSpace(targetLocationId))
            {
                builder.Append(" targetLocationId=");
                builder.Append(targetLocationId);
            }

            if (!string.IsNullOrWhiteSpace(task.ActivityKind))
            {
                builder.Append(" activityKind=");
                builder.Append(task.ActivityKind);
            }

            builder.Append(" result=");
            builder.Append(string.IsNullOrWhiteSpace(reason) ? "(no reason)" : CleanGoalText(reason));
            return builder.ToString();
        }

        private static string AppendBoundedLine(string existing, string line, int maxLines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return existing ?? string.Empty;
            }

            List<string> lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(existing))
            {
                string[] parts = existing.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length; i++)
                {
                    string part = CleanGoalText(parts[i]);
                    if (!string.IsNullOrWhiteSpace(part))
                    {
                        lines.Add(part);
                    }
                }
            }

            lines.Add(CleanGoalText(line));
            int limit = Mathf.Max(1, maxLines);
            while (lines.Count > limit)
            {
                lines.RemoveAt(0);
            }

            return string.Join("\n", lines);
        }

        private void PrepareDecisionAfterTask(NpcTask completedTask, NpcAiDecision decision)
        {
            if (decision == null)
            {
                return;
            }

            NpcIntentType intent = decision.ParsedIntent;
            if (intent != NpcIntentType.TalkToPlayer
                && intent != NpcIntentType.TalkToNpc
                && intent != NpcIntentType.SelfTalk)
            {
                decision.dialogue = string.Empty;
            }

        }


        private void ScheduleDecisionAfterControllerIdle(NpcBehaviorController controller, NpcTask completedTask, string reason)
        {
            if (pendingAfterTaskDecisionCoroutine != null)
            {
                StopCoroutine(pendingAfterTaskDecisionCoroutine);
            }

            pendingAfterTaskDecisionCoroutine = StartCoroutine(RequestAfterTaskWhenIdle(controller, completedTask, reason));
        }

        private System.Collections.IEnumerator RequestAfterTaskWhenIdle(NpcBehaviorController controller, NpcTask completedTask, string reason)
        {
            float deadline = Time.realtimeSinceStartup + waitForBusyAiBeforeFallbackSeconds;
            while (controller != null && controller.RequestInFlight && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            pendingAfterTaskDecisionCoroutine = null;
            if (controller == null || controller.RequestInFlight)
            {
                ResolveCurrentScheduleAfterDecision();
                yield break;
            }

            RequestDecisionAfterTask(completedTask, reason);
        }

        private string BuildImmediatePerceptionSummary()
        {
            if (perceptionSensor == null)
            {
                return "(no perception sensor)";
            }

            return perceptionSensor.BuildObservationSummary();
        }

        private string BuildTargetPerceptionSummary(string targetActorId)
        {
            if (perceptionSensor == null || string.IsNullOrWhiteSpace(targetActorId))
            {
                return string.Empty;
            }

            perceptionSensor.BuildObservationSummary();
            IReadOnlyList<PerceptionObservation> observations = perceptionSensor.Observations;
            for (int i = 0; i < observations.Count; i++)
            {
                PerceptionObservation observation = observations[i];
                if (observation == null || !string.Equals(observation.EntityId, targetActorId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return
                    $"id={observation.EntityId}, name={observation.DisplayName}, type={observation.EntityType}, " +
                    $"distance={observation.Distance:0.0}, channels={observation.Channels}, observed={observation.Description}";
            }

            return string.Empty;
        }

        private System.Collections.IEnumerator CompleteOneShotTaskAfterInteraction(NpcTask task)
        {
            RememberCompletedOneShot(task);
            if (IsPlayerTalkTask(task))
            {
                CompletePlayerTalkTask(task);
                yield break;
            }

            ConversationHandOffResult handOffResult = TryHandOneShotTaskToConversation(task, out ConversationArbiter.ConversationStartFailureReason failureReason);
            if (handOffResult == ConversationHandOffResult.Started)
            {
                taskController?.ClearCurrentTask("one-shot conversation handed to arbiter");
                yield break;
            }

            if (handOffResult == ConversationHandOffResult.Waiting)
            {
                if (pendingSharedEventConversationCoroutine != null)
                {
                    StopCoroutine(pendingSharedEventConversationCoroutine);
                }

                pendingSharedEventConversationCoroutine = StartCoroutine(WaitForSharedEventConversation(task));
                yield break;
            }

            if (ShouldRetryConversationStart(failureReason))
            {
                yield return RetryHandOneShotTaskToConversation(task, failureReason);
                if (taskController == null || taskController.CurrentTask != task)
                {
                    yield break;
                }

                if (conversationArbiter != null && conversationArbiter.IsNpcInActiveConversation(runtimeState))
                {
                    taskController?.ClearCurrentTask("one-shot conversation handed to arbiter after retry");
                    yield break;
                }
            }

            bool replyShown = false;
            if (!IsMissingOrUnreachableTargetFailure(task, failureReason))
            {
                yield return RequestTargetReplyIfPossible(task, shown => replyShown = shown);
            }

            taskController?.CompleteCurrentTask(BuildOneShotCompletionReason(task, failureReason, replyShown));
        }

        private void CompletePlayerTalkTask(NpcTask task)
        {
            if (task == null)
            {
                return;
            }

            PlayerMovementController player = FindFirstObjectByType<PlayerMovementController>();
            if (player == null)
            {
                taskController?.CompleteCurrentTask("failed: could not request player dialogue because player actor was not found.");
                return;
            }

            if (playerDialogueRequestSystem == null)
            {
                playerDialogueRequestSystem = FindFirstObjectByType<PlayerDialogueRequestSystem>();
            }

            if (playerDialogueRequestSystem == null)
            {
                taskController?.CompleteCurrentTask("failed: could not request player dialogue because PlayerDialogueRequestSystem was not found.");
                return;
            }

            string openingLine = string.IsNullOrWhiteSpace(task.Dialogue) ? string.Empty : task.Dialogue;
            bool requested = playerDialogueRequestSystem.TryShowRequest(
                runtimeState,
                player.gameObject,
                task.Reason,
                openingLine,
                result => HandlePlayerDialogueRequestFinished(task, result));

            if (!requested)
            {
                taskController?.CompleteCurrentTask("failed: could not request player dialogue because the player dialogue request system could not accept this request.");
            }
        }

        private void RequestPlayerFollowConsent(NpcTask task)
        {
            if (task == null || taskController == null || taskController.CurrentTask != task)
            {
                return;
            }

            PlayerMovementController player = FindFirstObjectByType<PlayerMovementController>();
            if (player == null)
            {
                taskController.CompleteCurrentTask("failed: could not request player follow because player actor was not found.");
                return;
            }

            if (playerDialogueRequestSystem == null)
            {
                playerDialogueRequestSystem = FindFirstObjectByType<PlayerDialogueRequestSystem>();
            }

            if (playerDialogueRequestSystem == null)
            {
                taskController.CompleteCurrentTask("failed: could not request player follow because PlayerDialogueRequestSystem was not found.");
                return;
            }

            bool requested = playerDialogueRequestSystem.TryShowFollowRequest(
                runtimeState,
                player.gameObject,
                task.Reason,
                result => HandlePlayerFollowRequestFinished(task, result));

            if (!requested)
            {
                taskController.CompleteCurrentTask("failed: could not request player follow because the player request system could not accept this request.");
            }
        }

        private void CompleteFindActorTask(NpcTask task, GameObject target)
        {
            if (task == null || taskController == null || taskController.CurrentTask != task)
            {
                return;
            }

            RememberCompletedOneShot(task);

            if (target != null)
            {
                movementAgent?.Stop();
                movementAgent?.Face(target.transform.position);
            }

            string completionReason = BuildFindActorCompletionReason(task, target);
            taskController.CompleteCurrentTask(completionReason);
        }

        private void CompleteFollowActorTask(NpcTask task, GameObject target)
        {
            if (task == null || taskController == null || taskController.CurrentTask != task)
            {
                return;
            }

            if (IsFollowReleaseTask(task))
            {
                ClearFollowReleaseState();
            }

            if (target != null)
            {
                movementAgent?.Stop();
                movementAgent?.Face(target.transform.position);
            }

            string targetActorId = string.IsNullOrWhiteSpace(task.TargetActorId) ? "(none)" : task.TargetActorId.Trim();
            LocationDefinition destination = GetFollowActorDestination(task);
            string destinationText = destination != null
                ? $"destinationLocationId={destination.LocationId} ({destination.DisplayName})"
                : "no destination";
            taskController.CompleteCurrentTask($"followed targetActorId={targetActorId}; {destinationText}");
        }

        private void ClearFollowReleaseState()
        {
            followReleaseTask = null;
            followReleaseDestination = default;
        }

        private string BuildFindActorCompletionReason(NpcTask task, GameObject target)
        {
            if (task == null)
            {
                return "found actor";
            }

            string actorId = task.TargetActorId;
            string locationId = task.TargetLocation != null ? task.TargetLocation.LocationId : string.Empty;
            string locationName = task.TargetLocation != null ? task.TargetLocation.DisplayName : string.Empty;
            string locationText = BuildLocationText(locationId, locationName);
            NpcRuntimeState targetNpc = target != null ? target.GetComponent<NpcRuntimeState>() : FindNpcById(actorId);

            if (target != null)
            {
                if (task.TargetLocation == null)
                {
                    return BuildFindActorSuccessReason(task, target, $"found actor {actorId}");
                }

                if (targetNpc != null && IsSameLocation(targetNpc.ActualLocation, task.TargetLocation))
                {
                    return BuildFindActorSuccessReason(task, target, $"found actor {actorId}{locationText}");
                }

                if (string.Equals(actorId, "player", StringComparison.OrdinalIgnoreCase)
                    && locationSystem != null
                    && IsSameLocation(locationSystem.CurrentLocation, task.TargetLocation))
                {
                    return BuildFindActorSuccessReason(task, target, $"found actor {actorId}{locationText}");
                }

                if (CanCurrentlyPerceiveActor(actorId))
                {
                    return BuildFindActorSuccessReason(task, target, $"found actor {actorId}");
                }
            }

            if (task.TargetLocation != null)
            {
                return $"failed: searched{locationText} but targetActorId={actorId} was not there.";
            }

            return $"failed: could not find targetActorId={actorId}.";
        }

        private string BuildFindActorSuccessReason(NpcTask task, GameObject target, string foundText)
        {
            string targetActorId = task == null || string.IsNullOrWhiteSpace(task.TargetActorId)
                ? "(none)"
                : task.TargetActorId.Trim();
            string targetPerception = BuildTargetPerceptionSummary(targetActorId);
            if (string.IsNullOrWhiteSpace(targetPerception))
            {
                targetPerception = target != null
                    ? $"targetActorId={targetActorId} is nearby, but no detailed perception record was available"
                    : $"targetActorId={targetActorId} could not be observed because the actor was not found";
            }

            return $"{foundText}; observed targetActorId={targetActorId}: {targetPerception}";
        }

        private static string BuildLocationText(string locationId, string locationName)
        {
            if (string.IsNullOrWhiteSpace(locationId) && string.IsNullOrWhiteSpace(locationName))
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(locationName))
            {
                return $" at expectedLocationId={locationId}";
            }

            return $" at expectedLocationId={locationId} ({locationName})";
        }

        private void HandlePlayerDialogueRequestFinished(NpcTask task, PlayerDialogueRequestResult result)
        {
            if (taskController == null || taskController.CurrentTask != task)
            {
                return;
            }

            switch (result)
            {
                case PlayerDialogueRequestResult.Accepted:
                    taskController.ClearCurrentTask("player dialogue accepted");
                    break;
                case PlayerDialogueRequestResult.TimedOut:
                    taskController.CompleteCurrentTask("failed: player dialogue request timed out and was treated as rejected.");
                    break;
                case PlayerDialogueRequestResult.Rejected:
                    taskController.CompleteCurrentTask("failed: player rejected the dialogue request.");
                    break;
                default:
                    taskController.CompleteCurrentTask("failed: player dialogue request could not start.");
                    break;
            }
        }

        private void HandlePlayerFollowRequestFinished(NpcTask task, PlayerDialogueRequestResult result)
        {
            if (taskController == null || taskController.CurrentTask != task)
            {
                return;
            }

            switch (result)
            {
                case PlayerDialogueRequestResult.Accepted:
                    playerFollowConsentGrantedTasks.Add(task);
                    ExecuteFollowActorTask(task);
                    break;
                case PlayerDialogueRequestResult.TimedOut:
                    taskController.CompleteCurrentTask("failed: player follow request timed out and was treated as rejected.");
                    break;
                case PlayerDialogueRequestResult.Rejected:
                    taskController.CompleteCurrentTask("failed: player rejected the follow request.");
                    break;
                default:
                    taskController.CompleteCurrentTask("failed: player follow request could not start.");
                    break;
            }
        }

        private static bool IsPlayerTalkTask(NpcTask task)
        {
            return task != null
                && task.Kind == NpcTaskKind.TalkToActor
                && string.Equals(task.TargetActorId, "player", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPlayerFollowTask(NpcTask task)
        {
            return task != null
                && task.Kind == NpcTaskKind.FollowActor
                && string.Equals(task.TargetActorId, "player", StringComparison.OrdinalIgnoreCase);
        }

        private ConversationHandOffResult TryHandOneShotTaskToConversation(NpcTask task, out ConversationArbiter.ConversationStartFailureReason failureReason)
        {
            failureReason = ConversationArbiter.ConversationStartFailureReason.InvalidProposal;
            if (conversationArbiter == null)
            {
                conversationArbiter = ConversationArbiter.GetOrCreate();
            }

            if (task == null
                || conversationArbiter == null
                || string.IsNullOrWhiteSpace(task.TargetActorId)
                || string.Equals(task.TargetActorId, "player", StringComparison.OrdinalIgnoreCase))
            {
                return ConversationHandOffResult.Failed;
            }

            NpcRuntimeState targetNpc = FindNpcById(task.TargetActorId);
            if (targetNpc == null || targetNpc.Profile == null || !IsCloseEnoughToActor(targetNpc.gameObject))
            {
                failureReason = targetNpc == null
                    ? ConversationArbiter.ConversationStartFailureReason.TargetMissing
                    : ConversationArbiter.ConversationStartFailureReason.TooFar;
                return ConversationHandOffResult.Failed;
            }

            ConversationArbiter.SharedEventConversationResult result = conversationArbiter.TryJoinSharedEventConversation(
                runtimeState,
                targetNpc,
                task.Label,
                BuildConversationReasonWithPlanContext(task),
                task.Priority,
                BuildDialogueContextFromTask(task),
                out failureReason);

            if (result == ConversationArbiter.SharedEventConversationResult.Started && logExecution)
            {
                Debug.Log(
                    $"[NPC Action] {name}: started shared one-shot {task.Kind} conversation with {task.TargetActorId}.",
                    this);
            }

            if (result == ConversationArbiter.SharedEventConversationResult.WaitingForMoreParticipants)
            {
                if (logExecution)
                {
                    Debug.Log(
                        $"[NPC Action] {name}: waiting for shared one-shot conversation with {task.TargetActorId}.",
                        this);
                }

                return ConversationHandOffResult.Waiting;
            }

            return result == ConversationArbiter.SharedEventConversationResult.Started
                ? ConversationHandOffResult.Started
                : ConversationHandOffResult.Failed;
        }

        private System.Collections.IEnumerator RetryStartConversationTask(
            NpcTask task,
            ConversationArbiter.ConversationStartFailureReason originalFailureReason)
        {
            float deadline = Time.realtimeSinceStartup + waitForConversationStartRetrySeconds;
            while (task != null
                && taskController != null
                && taskController.CurrentTask == task
                && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                NpcRuntimeState targetForConversation = FindNpcById(task.TargetActorId);
                if (targetForConversation == null)
                {
                    continue;
                }

                if (!IsCloseEnoughToActor(targetForConversation.gameObject))
                {
                    TryRetargetActorApproachForConversationRetry(task, targetForConversation.gameObject);
                    continue;
                }

                StopActorApproachForConversation(task, targetForConversation.gameObject);
                if (conversationArbiter != null
                    && conversationArbiter.TryStartOneOnOneNow(
                        runtimeState,
                        targetForConversation,
                        task.Label,
                        string.IsNullOrWhiteSpace(task.Reason) ? task.Dialogue : task.Reason,
                        task.Priority,
                        false,
                        BuildDialogueContextFromTask(task),
                        out _))
                {
                    taskController.ClearCurrentTask("conversation handed to arbiter after retry");
                    yield break;
                }
            }

            if (logExecution)
            {
                Debug.Log(
                    $"[NPC Action] {name}: could not start NPC conversation with {task?.TargetActorId}; reason={originalFailureReason}; retry expired.",
                    this);
            }

            if (taskController != null && taskController.CurrentTask == task)
            {
                ConversationArbiter.ConversationStartFailureReason finalFailureReason = ResolveCurrentConversationFailureReason(task);
                if (finalFailureReason == ConversationArbiter.ConversationStartFailureReason.InvalidProposal)
                {
                    finalFailureReason = originalFailureReason;
                }

                taskController.CompleteCurrentTask("failed: " + BuildConversationFailureReason(task, finalFailureReason));
            }
        }

        private System.Collections.IEnumerator RetryHandOneShotTaskToConversation(
            NpcTask task,
            ConversationArbiter.ConversationStartFailureReason originalFailureReason)
        {
            float deadline = Time.realtimeSinceStartup + waitForConversationStartRetrySeconds;
            while (task != null
                && taskController != null
                && taskController.CurrentTask == task
                && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                NpcRuntimeState targetNpc = FindNpcById(task.TargetActorId);
                if (targetNpc == null)
                {
                    continue;
                }

                if (targetNpc != null && !IsCloseEnoughToActor(targetNpc.gameObject))
                {
                    TryRetargetActorApproachForConversationRetry(task, targetNpc.gameObject);
                    continue;
                }

                StopActorApproachForConversation(task, targetNpc.gameObject);
                ConversationHandOffResult result = TryHandOneShotTaskToConversation(task, out _);
                if (result == ConversationHandOffResult.Started)
                {
                    yield break;
                }

                if (result == ConversationHandOffResult.Waiting)
                {
                    yield return WaitForSharedEventConversation(task);
                    yield break;
                }
            }

            if (logExecution)
            {
                Debug.Log(
                    $"[NPC Action] {name}: could not start one-shot NPC conversation with {task?.TargetActorId}; reason={originalFailureReason}; retry expired, falling back to single reply.",
                    this);
            }
        }

        private bool TryRetargetActorApproachForConversationRetry(NpcTask task, GameObject target)
        {
            if (task == null
                || target == null
                || movementAgent == null
                || !movementAgent.CanMove
                || IsCloseEnoughToActor(target)
                || !ShouldDynamicallyPursueActor(task))
            {
                return false;
            }

            if (activeActorPursuitTask == task
                && Time.realtimeSinceStartup - activeActorPursuitStartedRealtime > actorPursuitMaxSeconds)
            {
                return false;
            }

            if (activeMovementTask == task && Time.realtimeSinceStartup < nextActorRetargetRealtime)
            {
                return true;
            }

            MoveNearActor(target, task);
            return true;
        }

        private void StopActorApproachForConversation(NpcTask task, GameObject target)
        {
            if (task == null || movementAgent == null)
            {
                return;
            }

            if (activeMovementTask == task)
            {
                activeMovementTask = null;
                movementAgent.Stop();
            }

            if (activeActorPursuitTask == task)
            {
                activeActorPursuitTask = null;
            }

            if (target != null)
            {
                movementAgent.Face(target.transform.position);
            }
        }

        private System.Collections.IEnumerator WaitForSharedEventConversation(NpcTask task)
        {
            float deadline = Time.realtimeSinceStartup + waitForConversationStartRetrySeconds + 4f;
            while (task != null
                && taskController != null
                && taskController.CurrentTask == task
                && conversationArbiter != null
                && conversationArbiter.IsNpcWaitingForSharedEvent(runtimeState)
                && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            pendingSharedEventConversationCoroutine = null;

            if (conversationArbiter != null && conversationArbiter.IsNpcInActiveConversation(runtimeState))
            {
                taskController?.ClearCurrentTask("one-shot conversation handed to shared event arbiter");
                yield break;
            }

            if (taskController == null || taskController.CurrentTask != task)
            {
                yield break;
            }

            if (logExecution)
            {
                Debug.Log(
                    $"[NPC Action] {name}: shared one-shot conversation with {task.TargetActorId} did not start, falling back to single reply.",
                    this);
            }

            ConversationArbiter.ConversationStartFailureReason finalFailureReason =
                ResolveCurrentConversationFailureReason(task);

            if (ShouldRetryConversationStart(finalFailureReason))
            {
                yield return RetryHandOneShotTaskToConversation(task, finalFailureReason);
                if (conversationArbiter != null && conversationArbiter.IsNpcInActiveConversation(runtimeState))
                {
                    taskController?.ClearCurrentTask("one-shot conversation handed to arbiter after shared-event retry");
                    yield break;
                }

                if (taskController == null || taskController.CurrentTask != task)
                {
                    yield break;
                }

                finalFailureReason = ResolveCurrentConversationFailureReason(task);
            }

            bool replyShown = false;
            if (!IsMissingOrUnreachableTargetFailure(task, finalFailureReason))
            {
                yield return RequestTargetReplyIfPossible(task, shown => replyShown = shown);
                if (replyShown)
                {
                    finalFailureReason = ConversationArbiter.ConversationStartFailureReason.None;
                }
            }

            taskController?.CompleteCurrentTask(BuildOneShotCompletionReason(task, finalFailureReason, replyShown));
        }

        private static bool ShouldRetryConversationStart(ConversationArbiter.ConversationStartFailureReason failureReason)
        {
            return failureReason == ConversationArbiter.ConversationStartFailureReason.BrainBusy
                || failureReason == ConversationArbiter.ConversationStartFailureReason.InitiatorAlreadyInConversation
                || failureReason == ConversationArbiter.ConversationStartFailureReason.TargetAlreadyInConversation
                || failureReason == ConversationArbiter.ConversationStartFailureReason.PlayerDialogueActive
                || failureReason == ConversationArbiter.ConversationStartFailureReason.PendingPlayerReply
                || failureReason == ConversationArbiter.ConversationStartFailureReason.TooFar;
        }

        private System.Collections.IEnumerator RequestTargetReplyIfPossible(NpcTask task, Action<bool> onReplyShown = null)
        {
            if (!requestTargetReplyForOneShot || task == null || string.IsNullOrWhiteSpace(task.TargetActorId))
            {
                yield break;
            }

            NpcRuntimeState targetNpc = FindNpcById(task.TargetActorId);
            if (targetNpc == null || targetNpc.Profile == null)
            {
                yield break;
            }

            if (!IsCloseEnoughToActor(targetNpc.gameObject))
            {
                if (logExecution)
                {
                    Debug.Log(
                        $"[NPC Action] {name}: skipped one-shot reply from {task.TargetActorId} because the target is no longer nearby.",
                        this);
                }

                yield break;
            }

            NpcBehaviorController targetController = targetNpc.GetComponent<NpcBehaviorController>();
            if (targetController == null || targetController.RequestInFlight)
            {
                yield break;
            }

            NpcMovementAgent targetMovement = targetNpc.GetComponent<NpcMovementAgent>();
            NpcPerceptionSensor targetSensor = targetNpc.GetComponent<NpcPerceptionSensor>();
            if (targetMovement != null)
            {
                targetMovement.SetPause("one_shot_reply", true);
                targetMovement.Face(transform.position);
            }

            string speakerName = runtimeState != null && runtimeState.Profile != null ? runtimeState.Profile.DisplayName : name;
            string observedEvent =
                $"{speakerName} came to check on you because of this one-shot event: {task.Reason}. " +
                $"They said or implied: {(string.IsNullOrWhiteSpace(task.Dialogue) ? "(no spoken line)" : task.Dialogue)}. " +
                $"Your immediate sight/hearing: {(targetSensor != null ? targetSensor.BuildObservationSummary() : "(no perception sensor)")}. " +
                "Reply once, naturally and briefly, as the target of this interaction. Dialogue response fields: intent=TalkToNpc, eventKind=None, targetActorId=speaker id when available, no movement or schedule changes.";

            bool finished = false;
            NpcAiDecision reply = null;
            bool requested = targetController.TryRequestPreviewDecision(
                observedEvent,
                decision =>
                {
                    reply = decision;
                    finished = true;
                },
                _ => finished = true);

            if (requested)
            {
                float replyDeadline = Time.realtimeSinceStartup + waitForBusyAiBeforeFallbackSeconds;
                while (!finished && Time.realtimeSinceStartup < replyDeadline)
                {
                    yield return null;
                }

                if (finished && reply != null && !string.IsNullOrWhiteSpace(reply.dialogue))
                {
                    messageDisplayer ??= FindFirstObjectByType<MessageDisplayer>();
                    messageDisplayer?.ShowMessage(targetNpc, reply.dialogue);
                    WriteOneShotReplyMemory(task, targetNpc, reply.dialogue);
                    onReplyShown?.Invoke(true);
                }
            }

            if (targetMovement != null)
            {
                targetMovement.SetPause("one_shot_reply", false);
            }
        }

        private string BuildOneShotCompletionReason(
            NpcTask task,
            ConversationArbiter.ConversationStartFailureReason failureReason,
            bool replyShown)
        {
            if (replyShown)
            {
                return "one-shot interaction resolved with target reply";
            }

            if (task == null
                || string.IsNullOrWhiteSpace(task.TargetActorId)
                || string.Equals(task.TargetActorId, "player", StringComparison.OrdinalIgnoreCase))
            {
                return "one-shot interaction resolved";
            }

            if (failureReason == ConversationArbiter.ConversationStartFailureReason.None)
            {
                return "one-shot interaction resolved";
            }

            return "failed: " + BuildConversationFailureReason(task, failureReason);
        }

        private string BuildConversationFailureReason(
            NpcTask task,
            ConversationArbiter.ConversationStartFailureReason failureReason)
        {
            string actorId = task != null ? task.TargetActorId : string.Empty;
            string locationId = task != null && task.TargetLocation != null ? task.TargetLocation.LocationId : string.Empty;
            string locationName = task != null && task.TargetLocation != null ? task.TargetLocation.DisplayName : string.Empty;

            switch (failureReason)
            {
                case ConversationArbiter.ConversationStartFailureReason.TargetMissing:
                    return $"could not talk to targetActorId={actorId} because that actor could not be found.";
                case ConversationArbiter.ConversationStartFailureReason.TooFar:
                    return
                        $"could not talk to targetActorId={actorId} because the target was found but was still too far to speak to" +
                        BuildExpectedLocationClause(locationId, locationName) + ".";
                case ConversationArbiter.ConversationStartFailureReason.BrainBusy:
                    return $"could not talk to targetActorId={actorId} because the target was already thinking/responding.";
                case ConversationArbiter.ConversationStartFailureReason.InitiatorAlreadyInConversation:
                    return $"could not talk to targetActorId={actorId} because I was already in another conversation.";
                case ConversationArbiter.ConversationStartFailureReason.TargetAlreadyInConversation:
                    return $"could not talk to targetActorId={actorId} because the target was already in another conversation.";
                case ConversationArbiter.ConversationStartFailureReason.PlayerDialogueActive:
                    return $"could not talk to targetActorId={actorId} because player dialogue blocked the target.";
                case ConversationArbiter.ConversationStartFailureReason.PendingPlayerReply:
                    return $"could not talk to targetActorId={actorId} because the target was waiting on a player reply.";
                default:
                    return $"could not start conversation with targetActorId={actorId}; failureReason={failureReason}.";
            }
        }

        private static string BuildExpectedLocationClause(string locationId, string locationName)
        {
            if (string.IsNullOrWhiteSpace(locationId) && string.IsNullOrWhiteSpace(locationName))
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(locationName))
            {
                return $" at expectedLocationId={locationId}";
            }

            return $" at expectedLocationId={locationId} ({locationName})";
        }

        private bool IsMissingOrUnreachableTargetFailure(
            NpcTask task,
            ConversationArbiter.ConversationStartFailureReason failureReason)
        {
            if (task == null || string.IsNullOrWhiteSpace(task.TargetActorId))
            {
                return false;
            }

            return failureReason == ConversationArbiter.ConversationStartFailureReason.TargetMissing
                || failureReason == ConversationArbiter.ConversationStartFailureReason.TooFar;
        }

        private ConversationArbiter.ConversationStartFailureReason ResolveCurrentConversationFailureReason(NpcTask task)
        {
            if (task == null || string.IsNullOrWhiteSpace(task.TargetActorId))
            {
                return ConversationArbiter.ConversationStartFailureReason.InvalidProposal;
            }

            NpcRuntimeState targetNpc = FindNpcById(task.TargetActorId);
            if (targetNpc == null || targetNpc.Profile == null)
            {
                return ConversationArbiter.ConversationStartFailureReason.TargetMissing;
            }

            return IsCloseEnoughToActor(targetNpc.gameObject)
                ? ConversationArbiter.ConversationStartFailureReason.InvalidProposal
                : ConversationArbiter.ConversationStartFailureReason.TooFar;
        }

        private string BuildTaskFailureContext(NpcTask completedTask, string reason)
        {
            if (!IsFailureReason(reason))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            builder.Append("Failure facts: ");
            builder.Append(reason);
            builder.Append(' ');

            if (completedTask != null && !string.IsNullOrWhiteSpace(completedTask.TargetActorId))
            {
                builder.Append("Required target actor was ");
                builder.Append(completedTask.TargetActorId);
                builder.Append(". ");

                NpcRuntimeState targetNpc = FindNpcById(completedTask.TargetActorId);
                if (targetNpc == null)
                {
                    builder.Append("The target actor is not registered in the scene. ");
                }
                else
                {
                    float distance = Vector2.Distance(transform.position, targetNpc.transform.position);
                    builder.Append("Distance to target after failure=");
                    builder.Append(distance.ToString("0.0"));
                    builder.Append(". ");
                    if (targetNpc.ActualLocation != null)
                    {
                        builder.Append("Target runtime state currently reports location id=");
                        builder.Append(targetNpc.ActualLocation.LocationId);
                        builder.Append(", name=");
                        builder.Append(targetNpc.ActualLocation.DisplayName);
                        builder.Append(". ");
                    }

                    if (!IsCloseEnoughToActor(targetNpc.gameObject))
                    {
                        builder.Append("Direct interaction failed because the required target was not close enough to speak to. ");
                        if (CanCurrentlyPerceiveActor(completedTask.TargetActorId))
                        {
                            builder.Append("Direct perception currently includes this target, so the issue is conversation distance, not absence. ");
                        }
                    }
                }
            }

            if (completedTask != null && completedTask.TargetLocation != null)
            {
                builder.Append("Expected place was id=");
                builder.Append(completedTask.TargetLocation.LocationId);
                builder.Append(", name=");
                builder.Append(completedTask.TargetLocation.DisplayName);
                builder.Append(". ");
            }

            builder.Append("The missing target did not hear this event. ");
            builder.Append("Nearby companions are context, not substitutes for the missing required target. ");
            builder.Append("A human-like response can be to search a likely place, ask one nearby person for information, wait briefly, report uncertainty, or return to routine. ");
            return builder.ToString();
        }

        private void WriteTaskResultMemory(NpcTask completedTask, string reason)
        {
            if (memorySystem == null
                || runtimeState == null
                || runtimeState.Profile == null
                || completedTask == null)
            {
                return;
            }

            string location = completedTask.TargetLocation != null
                ? $"{completedTask.TargetLocation.DisplayName} ({completedTask.TargetLocation.LocationId})"
                : "(no specific place)";
            string target = string.IsNullOrWhiteSpace(completedTask.TargetActorId)
                ? "(no specific actor)"
                : completedTask.TargetActorId;
            bool failed = IsFailureReason(reason);
            bool completedFindActor = !failed && completedTask.Kind == NpcTaskKind.FindActor;
            bool completedFollowActor = !failed && completedTask.Kind == NpcTaskKind.FollowActor;
            string source = failed ? "task_failure" : completedFindActor ? "observation_result" : completedFollowActor ? "follow_result" : completedTask.OneShot ? "one_shot_result" : "task_result";
            int importance = failed || completedTask.OneShot || completedFindActor || completedFollowActor ? 7 : 5;

            StringBuilder builder = new StringBuilder();
            if (failed)
            {
                builder.Append("My task failed");
            }
            else
            {
                builder.Append("I completed task");
            }

            builder.Append(": kind=");
            builder.Append(completedTask.Kind);
            builder.Append(", sourceIntent=");
            builder.Append(completedTask.SourceIntent);
            builder.Append(", target=");
            builder.Append(target);
            builder.Append(", place=");
            builder.Append(location);
            builder.Append(", result=");
            builder.Append(string.IsNullOrWhiteSpace(reason) ? "(no reason)" : reason);

            if (completedFindActor)
            {
                builder.Append(". I have already reached and observed this target for the current verification attempt");
            }

            if (completedFollowActor)
            {
                builder.Append(". I have already followed this target for the current follow attempt");
            }

            string targetPerception = BuildTargetPerceptionSummary(completedTask.TargetActorId);
            if (!string.IsNullOrWhiteSpace(targetPerception))
            {
                builder.Append(". Target perception at completion: ");
                builder.Append(targetPerception);
            }

            MemoryRecord record = memorySystem.AddMemory(
                runtimeState.Profile.NpcId,
                builder.ToString(),
                source,
                importance);
            if (record != null && !string.IsNullOrWhiteSpace(completedTask.TargetActorId))
            {
                record.SetTargetActor(completedTask.TargetActorId.Trim());
                record.SetTags($"{source};{completedTask.Kind};{completedTask.SourceIntent}");
            }

            if (!failed)
            {
                WriteCurrentPerceptionFacts(source, importance);
            }
        }

        private void WriteCurrentPerceptionFacts(string source, int importance)
        {
            if (memorySystem == null || runtimeState == null || runtimeState.Profile == null || perceptionSensor == null)
            {
                return;
            }

            perceptionSensor.BuildObservationSummary();
            IReadOnlyList<PerceptionObservation> observations = perceptionSensor.Observations;
            for (int i = 0; i < observations.Count; i++)
            {
                PerceptionObservation observation = observations[i];
                if (observation == null
                    || string.IsNullOrWhiteSpace(observation.EntityId)
                    || string.Equals(observation.EntityId, "player", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                memorySystem.AddFact(
                    runtimeState.Profile.NpcId,
                    observation.EntityId,
                    runtimeState.Profile.NpcId,
                    runtimeState.Profile.DisplayName,
                    $"I directly observed {observation.DisplayName}: {observation.Description}",
                    string.IsNullOrWhiteSpace(source) ? "direct_perception" : $"direct_perception;{source}",
                    Mathf.Max(importance, 6));
            }
        }

        private void RememberFailedTask(NpcTask task, string reason)
        {
            if (!IsFailureReason(reason) || task == null)
            {
                return;
            }

            lastFailedTaskSignature = BuildOneShotSignature(task);
            lastFailedTaskRealtime = Time.realtimeSinceStartup;
        }

        private bool IsRepeatedFailedTaskFollowup(NpcAiDecision decision, string targetActorId, LocationDefinition targetLocation)
        {
            if (string.IsNullOrWhiteSpace(lastFailedTaskSignature))
            {
                return false;
            }

            if (Time.realtimeSinceStartup - lastFailedTaskRealtime > repeatedOneShotSuppressionSeconds)
            {
                return false;
            }

            NpcTaskKind kind = ToTaskKind(decision.ParsedIntent);
            if (kind != NpcTaskKind.TalkToActor
                && kind != NpcTaskKind.ReactToEvent
                && kind != NpcTaskKind.FindActor
                && kind != NpcTaskKind.FollowActor)
            {
                return false;
            }

            string signature = BuildOneShotSignature(decision, targetActorId, targetLocation);
            return string.Equals(signature, lastFailedTaskSignature, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFailureReason(string reason)
        {
            return !string.IsNullOrWhiteSpace(reason)
                && reason.TrimStart().StartsWith("failed:", StringComparison.OrdinalIgnoreCase);
        }

        private void WriteOneShotReplyMemory(NpcTask task, NpcRuntimeState targetNpc, string reply)
        {
            if (memorySystem == null || targetNpc == null || targetNpc.Profile == null)
            {
                return;
            }

            string selfId = runtimeState != null && runtimeState.Profile != null ? runtimeState.Profile.NpcId : string.Empty;
            string targetId = targetNpc.Profile.NpcId;
            if (!string.IsNullOrWhiteSpace(selfId))
            {
                memorySystem.AddMemory(selfId, $"{targetNpc.Profile.DisplayName} replied during one-shot interaction: {reply}", "one_shot_reply", 6);
            }

            memorySystem.AddMemory(targetId, $"I replied during one-shot interaction: {reply}", "one_shot_reply", 6);
        }

        private bool ShouldIgnorePostOneShotDecision(NpcTask completedTask, NpcAiDecision decision)
        {
            if (!filterRepeatedOneShotFollowups || completedTask == null || decision == null)
            {
                return false;
            }

            string targetActor = string.IsNullOrWhiteSpace(decision.targetActorId) ? string.Empty : decision.targetActorId.Trim();
            bool sameActor = !string.IsNullOrWhiteSpace(completedTask.TargetActorId)
                && string.Equals(completedTask.TargetActorId, targetActor, StringComparison.OrdinalIgnoreCase);
            if (!sameActor)
            {
                return false;
            }

            string completedLocationId = completedTask.TargetLocation != null ? completedTask.TargetLocation.LocationId : string.Empty;
            string decisionLocationId = string.IsNullOrWhiteSpace(decision.targetLocationId) ? string.Empty : decision.targetLocationId.Trim();
            if (string.IsNullOrWhiteSpace(completedLocationId) && !string.IsNullOrWhiteSpace(decisionLocationId))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(completedLocationId)
                && !string.Equals(completedLocationId, decisionLocationId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            NpcIntentType intent = decision.ParsedIntent;
            return intent == NpcIntentType.TalkToNpc
                || intent == NpcIntentType.ReactToEvent
                || intent == NpcIntentType.FindActor
                || intent == NpcIntentType.FollowActor;
        }

        private void RememberCompletedOneShot(NpcTask task)
        {
            lastOneShotSignature = BuildOneShotSignature(task);
            lastOneShotCompletedRealtime = Time.realtimeSinceStartup;
        }

        private bool IsRepeatedOneShotFollowup(NpcAiDecision decision, string targetActorId, LocationDefinition targetLocation)
        {
            if (string.IsNullOrWhiteSpace(lastOneShotSignature))
            {
                return false;
            }

            if (Time.realtimeSinceStartup - lastOneShotCompletedRealtime > repeatedOneShotSuppressionSeconds)
            {
                return false;
            }

            string signature = BuildOneShotSignature(decision, targetActorId, targetLocation);
            return string.Equals(signature, lastOneShotSignature, StringComparison.OrdinalIgnoreCase);
        }

        private string BuildOneShotSignature(NpcTask task)
        {
            if (task == null)
            {
                return string.Empty;
            }

            string target = !string.IsNullOrWhiteSpace(task.TargetActorId)
                ? $"actor:{task.TargetActorId}"
                : task.TargetLocation != null
                    ? $"location:{task.TargetLocation.LocationId}"
                    : "none";
            string location = task.TargetLocation != null ? task.TargetLocation.LocationId : string.Empty;
            return $"{task.Kind}:{target}:at:{location}";
        }

        private string BuildOneShotSignature(NpcAiDecision decision, string targetActorId, LocationDefinition targetLocation)
        {
            NpcTaskKind kind = ToTaskKind(decision.ParsedIntent);
            string target = !string.IsNullOrWhiteSpace(targetActorId)
                ? $"actor:{targetActorId}"
                : targetLocation != null
                    ? $"location:{targetLocation.LocationId}"
                    : "none";
            string location = targetLocation != null ? targetLocation.LocationId : string.Empty;
            return $"{kind}:{target}:at:{location}";
        }

        private void ResolveCurrentScheduleAfterDecision()
        {
            if (!EnsureTaskController())
            {
                return;
            }

            if (resolvingScheduleFromDecision)
            {
                return;
            }

            resolvingScheduleFromDecision = true;
            try
            {
                if (TryStartDueSocialPlanTask())
                {
                    return;
                }

                if (scheduleSystem != null)
                {
                    NpcScheduleAgent agent = GetComponent<NpcScheduleAgent>();
                    if (agent != null && scheduleSystem.Resolve(agent))
                    {
                        taskController.SetScheduleTask(runtimeState.PlannedLocation, runtimeState.CurrentAction);
                        return;
                    }
                }

                taskController.SetScheduleTask(runtimeState.PlannedLocation, runtimeState.CurrentAction);
            }
            finally
            {
                resolvingScheduleFromDecision = false;
            }
        }

        private void StartFollowScheduleTask(string reason)
        {
            if (logExecution)
            {
                Debug.Log($"[NPC Action] {name}: following schedule. reason={reason}", this);
            }

            ResolveCurrentScheduleAfterDecision();
        }

        private void RequestDecisionAfterExecutionFailure(NpcAiDecision failedDecision, string failureReason)
        {
            NpcBehaviorController controller = GetComponent<NpcBehaviorController>();
            if (controller == null || controller.RequestInFlight)
            {
                StartFollowScheduleTask(failureReason);
                return;
            }

            controller.SetObservedEventSummary(
                "The previous AI decision could not be executed. " +
                $"Failure reason: {failureReason}. " +
                $"Failed intent={failedDecision.intent}, targetActorId={failedDecision.targetActorId}, targetLocationId={failedDecision.targetLocationId}. " +
                "Choose a different feasible next decision, or ContinueCurrentAction.");
            controller.ForceRequestDecision();
        }

        private bool EnsureTaskController()
        {
            if (taskController != null)
            {
                return true;
            }

            ReportFailure("NpcTaskController is missing. Add NpcTaskController to the same NPC object as NpcActionExecutor.");
            return false;
        }

        private bool IsInPlayerConversation()
        {
            DialogueController dialogueController = FindFirstObjectByType<DialogueController>();
            return dialogueController != null && dialogueController.IsConversationWith(runtimeState);
        }

        private void UnlockConversationMovement()
        {
            if (movementAgent != null)
            {
                movementAgent.SetPause(ConversationPauseReason, false);
            }

            if (conversationTargetMovement != null)
            {
                conversationTargetMovement.SetPause(ConversationPauseReason, false);
            }

            conversationTargetMovement = null;
        }

        private System.Collections.IEnumerator UnlockNpcConversationAfterDelay(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            unlockConversationCoroutine = null;
            UnlockConversationMovement();
            taskController?.CompleteCurrentTask("conversation ended");
        }

        private float CalculateConversationSeconds(string dialogue)
        {
            int length = string.IsNullOrEmpty(dialogue) ? 0 : dialogue.Length;
            float readingSeconds = length / Mathf.Max(1f, npcConversationCharactersPerSecond);
            return Mathf.Max(npcConversationMinSeconds, readingSeconds + npcConversationExtraSeconds);
        }

        private static bool RequiresTarget(NpcTaskKind kind)
        {
            return kind != NpcTaskKind.TalkToActor;
        }

        private static NpcTaskKind ToTaskKind(NpcIntentType intent)
        {
            switch (intent)
            {
                case NpcIntentType.TalkToPlayer:
                case NpcIntentType.TalkToNpc:
                    return NpcTaskKind.TalkToActor;
                case NpcIntentType.MoveToLocation:
                    return NpcTaskKind.MoveToLocation;
                case NpcIntentType.WorkAtLocation:
                    return NpcTaskKind.WorkAtLocation;
                case NpcIntentType.RestAtLocation:
                    return NpcTaskKind.RestAtLocation;
                case NpcIntentType.ReactToEvent:
                    return NpcTaskKind.ReactToEvent;
                case NpcIntentType.AvoidActor:
                    return NpcTaskKind.AvoidActor;
                case NpcIntentType.JoinFestival:
                    return NpcTaskKind.JoinFestival;
                case NpcIntentType.AttendActivity:
                    return NpcTaskKind.AttendActivity;
                case NpcIntentType.FindActor:
                    return NpcTaskKind.FindActor;
                case NpcIntentType.FollowActor:
                    return NpcTaskKind.FollowActor;
                case NpcIntentType.SelfTalk:
                case NpcIntentType.ContinueCurrentAction:
                default:
                    return NpcTaskKind.FollowSchedule;
            }
        }

        private NpcIntentType NormalizeTaskIntent(NpcAiDecision decision, string targetActorId)
        {
            if (decision == null)
            {
                return NpcIntentType.ContinueCurrentAction;
            }

            NpcIntentType intent = decision.ParsedIntent;
            if (intent == NpcIntentType.MoveToLocation && !string.IsNullOrWhiteSpace(targetActorId))
            {
                if (logExecution)
                {
                    Debug.Log(
                        $"[NPC Action] {name}: normalized MoveToLocation with targetActorId={targetActorId} into FindActor. targetLocationId={decision.targetLocationId}",
                        this);
                }

                return NpcIntentType.FindActor;
            }

            return intent;
        }

        private static string BuildTaskLabel(NpcAiDecision decision)
        {
            if (!string.IsNullOrWhiteSpace(decision.nextActionPreference))
            {
                return decision.nextActionPreference;
            }

            return string.IsNullOrWhiteSpace(decision.intent) ? "AI task" : decision.intent;
        }

        private static bool IsOneShotEvent(NpcAiDecision decision)
        {
            if (decision == null)
            {
                return false;
            }

            if (string.Equals(decision.eventKind, NpcEventKind.OneShot.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            NpcIntentType intent = decision.ParsedIntent;
            return intent == NpcIntentType.ReactToEvent
                || intent == NpcIntentType.AvoidActor
                || intent == NpcIntentType.FindActor
                || intent == NpcIntentType.FollowActor;
        }

        private static bool IsScheduleOverrideEvent(NpcAiDecision decision)
        {
            if (decision == null)
            {
                return false;
            }

            if (string.Equals(decision.eventKind, NpcEventKind.ScheduleOverride.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            NpcIntentType intent = decision.ParsedIntent;
            return intent == NpcIntentType.WorkAtLocation
                || intent == NpcIntentType.RestAtLocation
                || intent == NpcIntentType.JoinFestival;
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

        private static GameObject FindActorById(string actorId)
        {
            if (string.IsNullOrWhiteSpace(actorId))
            {
                return null;
            }

            if (string.Equals(actorId, "player", StringComparison.OrdinalIgnoreCase))
            {
                PlayerMovementController player = FindFirstObjectByType<PlayerMovementController>();
                return player != null ? player.gameObject : null;
            }

            NpcRuntimeState npc = FindNpcById(actorId);
            return npc != null ? npc.gameObject : null;
        }

        private static bool ContainsAnyLoose(string text, params string[] needles)
        {
            if (string.IsNullOrWhiteSpace(text) || needles == null)
            {
                return false;
            }

            for (int i = 0; i < needles.Length; i++)
            {
                if (ContainsLoose(text, needles[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsLoose(string text, string needle)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(needle))
            {
                return false;
            }

            return NormalizeLoose(text).Contains(NormalizeLoose(needle));
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

        private void ReportFailure(string message)
        {
            ExecutionFailed?.Invoke(message);
            Debug.LogWarning($"[NPC Action] {name}: {message}", this);
        }

        private void FailTask(NpcTask task, string message)
        {
            ReportFailure(message);
            if (taskController != null && taskController.CurrentTask == task)
            {
                taskController.CompleteCurrentTask("failed: " + message);
            }
        }
    }
}
