using System;
using CityStateSim.Locations;
using CityStateSim.NPC;
using UnityEngine;

namespace CityStateSim.Tasks
{
    [RequireComponent(typeof(NpcRuntimeState))]
    public sealed class NpcTaskController : MonoBehaviour
    {
        [SerializeField] private bool logTasks;
        [SerializeField] private NpcTask currentTask;

        private NpcRuntimeState runtimeState;
        private NpcTask lastCompletedTask;
        private string lastCompletedReason;

        public NpcTask CurrentTask => currentTask;
        public NpcTask LastCompletedTask => lastCompletedTask;
        public string LastCompletedReason => lastCompletedReason;
        public bool HasTask => currentTask != null;
        public bool HasNonScheduleTask => currentTask != null && currentTask.Kind != NpcTaskKind.FollowSchedule;

        public event Action<NpcTask> TaskStarted;
        public event Action<NpcTask, string> TaskCompleted;
        public event Action<NpcTask, NpcTask> TaskChanged;

        private void Awake()
        {
            runtimeState = GetComponent<NpcRuntimeState>();
        }

        private void Update()
        {
            if (currentTask != null && currentTask.IsExpired())
            {
                CompleteCurrentTask("expired");
            }
        }

        public bool TryStartTask(NpcTask task)
        {
            if (task == null)
            {
                return false;
            }

            if (currentTask != null && currentTask.Kind != NpcTaskKind.FollowSchedule)
            {
                bool canInterrupt = currentTask.Interruptible && task.Priority >= currentTask.Priority;
                if (!canInterrupt)
                {
                    return false;
                }
            }

            SetCurrentTask(task);
            return true;
        }

        public void SetScheduleTask(LocationDefinition location, string actionName)
        {
            if (currentTask != null && currentTask.Kind != NpcTaskKind.FollowSchedule && !currentTask.IsExpired())
            {
                return;
            }

            SetCurrentTask(NpcTask.FollowSchedule(location, actionName));
        }

        public void CompleteCurrentTask(string reason)
        {
            if (currentTask == null)
            {
                return;
            }

            NpcTask completed = currentTask;
            currentTask = null;
            lastCompletedTask = completed;
            lastCompletedReason = reason;
            if (logTasks)
            {
                Debug.Log($"[NPC Task] {name}: completed {completed.Kind} ({reason})", this);
            }

            TaskCompleted?.Invoke(completed, reason);
            TaskChanged?.Invoke(completed, null);
        }

        public void ClearCurrentTask(string reason)
        {
            ClearCurrentTask(reason, false);
        }

        public void FinishAndClearCurrentTask(string reason)
        {
            ClearCurrentTask(reason, true);
        }

        private void ClearCurrentTask(string reason, bool raiseCompleted)
        {
            if (currentTask == null)
            {
                return;
            }

            NpcTask previous = currentTask;
            currentTask = null;
            lastCompletedTask = previous;
            lastCompletedReason = reason;
            if (logTasks)
            {
                Debug.Log($"[NPC Task] {name}: cleared {previous.Kind} ({reason})", this);
            }

            if (raiseCompleted)
            {
                TaskCompleted?.Invoke(previous, reason);
            }

            TaskChanged?.Invoke(previous, null);
        }

        private void SetCurrentTask(NpcTask task)
        {
            NpcTask previous = currentTask;
            currentTask = task;
            if (logTasks)
            {
                Debug.Log($"[NPC Task] {name}: started {task.Kind} priority={task.Priority} reason={task.Reason}", this);
            }

            TaskStarted?.Invoke(task);
            TaskChanged?.Invoke(previous, task);
        }
    }
}
