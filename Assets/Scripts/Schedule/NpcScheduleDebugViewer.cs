using CityStateSim.Core;
using CityStateSim.NPC;
using CityStateSim.SocialPlans;
using CityStateSim.Tasks;
using TMPro;
using UnityEngine;

namespace CityStateSim.Schedule
{
    public sealed class NpcScheduleDebugViewer : MonoBehaviour
    {
        [SerializeField] private NpcScheduleAgent targetAgent;
        [SerializeField] private NpcTaskController targetTaskController;
        [SerializeField] private ScheduleSystem scheduleSystem;
        [SerializeField] private SocialPlanSystem socialPlanSystem;
        [SerializeField] private GameClock clock;
        [SerializeField] private TMP_Text outputText;
        [SerializeField] private TMP_Text taskOutputText;
        [SerializeField] private bool showTaskInfo = true;
        [SerializeField] private bool showSocialPlanInfo = true;
        [SerializeField] private bool showTomorrow = true;
        [SerializeField] private bool refreshEveryFrame = true;
        [SerializeField, TextArea(2, 8)] private string taskDebugText;
        [SerializeField, TextArea(8, 30)] private string debugText;

        public string TaskDebugText => taskDebugText;
        public string DebugText => debugText;

        private void Awake()
        {
            if (targetAgent == null)
            {
                targetAgent = GetComponent<NpcScheduleAgent>();
            }

            if (scheduleSystem == null)
            {
                scheduleSystem = FindFirstObjectByType<ScheduleSystem>();
            }

            if (clock == null)
            {
                clock = FindFirstObjectByType<GameClock>();
            }

            if (socialPlanSystem == null)
            {
                socialPlanSystem = FindFirstObjectByType<SocialPlanSystem>();
            }

            ResolveTaskController();
        }

        private void Start()
        {
            Refresh();
        }

        private void Update()
        {
            if (refreshEveryFrame)
            {
                Refresh();
            }
        }

        public void SetTarget(NpcScheduleAgent agent)
        {
            targetAgent = agent;
            targetTaskController = null;
            ResolveTaskController();
            Refresh();
        }

        public void Refresh()
        {
            taskDebugText = BuildTaskPanelText();

            if (scheduleSystem == null || clock == null || targetAgent == null)
            {
                debugText = AppendTaskText("Missing ScheduleSystem, GameClock, or target NpcScheduleAgent.");
                ApplyText();
                return;
            }

            GameDate date = showTomorrow ? clock.GetNextDate(clock.CurrentDate) : clock.CurrentDate;
            RuntimeScheduleEntry current = scheduleSystem.GetCurrentRuntimeEntry(targetAgent);
            string currentText = current != null
                ? $"Current runtime: {current.StartTime}-{current.EndTime} {current.Label} action={current.ActionName} reason={current.Reason}"
                : "Current runtime: base schedule or none";

            debugText = AppendTaskText(currentText + "\n\n" + scheduleSystem.BuildDebugScheduleText(targetAgent, date));
            ApplyText();
        }

        private string BuildTaskDebugText()
        {
            ResolveTaskController();

            if (targetTaskController == null)
            {
                return "Current task: none (NpcTaskController missing)";
            }

            NpcTask task = targetTaskController.CurrentTask;
            if (task == null)
            {
                return "Current task: none";
            }

            string locationId = task.TargetLocation != null ? task.TargetLocation.LocationId : "";
            return $"Current task: {task.Kind} label={task.Label} targetLocation={locationId} targetActor={task.TargetActorId} activity={task.ActivityKind} activityKey={task.ActivityKey} participants={JoinIds(task.ParticipantActorIds)} required={JoinIds(task.RequiredActorIds)} priority={task.Priority} oneShot={task.OneShot} reason={task.Reason}";
        }

        private string BuildTaskPanelText()
        {
            string taskText = showTaskInfo ? BuildTaskDebugText() : string.Empty;
            string socialPlanText = showSocialPlanInfo ? BuildSocialPlanDebugText() : string.Empty;
            if (string.IsNullOrWhiteSpace(taskText))
            {
                return socialPlanText;
            }

            if (string.IsNullOrWhiteSpace(socialPlanText))
            {
                return taskText;
            }

            return taskText + "\nSocial plans:\n" + socialPlanText;
        }

        private string BuildSocialPlanDebugText()
        {
            if (socialPlanSystem == null)
            {
                socialPlanSystem = FindFirstObjectByType<SocialPlanSystem>();
            }

            NpcRuntimeState state = targetAgent != null ? targetAgent.RuntimeState : null;
            if (state == null && targetAgent != null)
            {
                state = targetAgent.GetComponent<NpcRuntimeState>();
            }

            if (socialPlanSystem == null || state == null || state.Profile == null)
            {
                return "Social plans: none (SocialPlanSystem or target NPC missing)";
            }

            return socialPlanSystem.BuildPlanSummaryForNpc(state.Profile.NpcId);
        }

        private static string JoinIds(string[] ids)
        {
            return ids == null || ids.Length == 0 ? "" : string.Join(",", ids);
        }

        private void ResolveTaskController()
        {
            if (targetTaskController != null)
            {
                return;
            }

            targetTaskController = GetComponent<NpcTaskController>();
            if (targetTaskController != null)
            {
                return;
            }

            if (targetAgent != null)
            {
                targetTaskController = targetAgent.GetComponent<NpcTaskController>();
            }
        }

        private string AppendTaskText(string scheduleText)
        {
            if (!showTaskInfo)
            {
                return scheduleText;
            }

            return taskDebugText + "\n" + scheduleText;
        }

        private void ApplyText()
        {
            if (outputText != null)
            {
                outputText.text = debugText;
            }

            if (taskOutputText != null)
            {
                taskOutputText.text = taskDebugText;
            }
        }
    }
}
