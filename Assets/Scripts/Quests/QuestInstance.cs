using System;
using CityStateSim.Core;
using UnityEngine;

namespace CityStateSim.Quests
{
    [Serializable]
    public sealed class QuestInstance
    {
        [SerializeField] private QuestDefinition definition;
        [SerializeField] private QuestState state;
        [SerializeField] private GameDate acceptedDate;
        [SerializeField] private GameTime acceptedTime;
        [SerializeField] private GameDate resolvedDate;
        [SerializeField] private GameTime resolvedTime;
        [SerializeField] private int progress;
        [SerializeField] private string failureReason;

        public QuestDefinition Definition => definition;
        public QuestState State => state;
        public GameDate AcceptedDate => acceptedDate;
        public GameTime AcceptedTime => acceptedTime;
        public GameDate ResolvedDate => resolvedDate;
        public GameTime ResolvedTime => resolvedTime;
        public int Progress => progress;
        public string FailureReason => failureReason;
        public bool IsActive => state == QuestState.Accepted || state == QuestState.ReadyToTurnIn;
        public bool IsTerminal => state == QuestState.Completed || state == QuestState.Failed;

        public QuestInstance(QuestDefinition definition, GameDate acceptedDate, GameTime acceptedTime)
        {
            this.definition = definition;
            this.acceptedDate = acceptedDate;
            this.acceptedTime = acceptedTime;
            state = QuestState.Accepted;
        }

        public bool AddProgress(int amount)
        {
            if (!IsActive || amount <= 0)
            {
                return false;
            }

            int previous = progress;
            progress = Mathf.Max(0, progress + amount);
            return progress != previous;
        }

        public void MarkReady()
        {
            if (state == QuestState.Accepted)
            {
                state = QuestState.ReadyToTurnIn;
            }
        }

        public void MarkCompleted(GameDate date, GameTime time)
        {
            state = QuestState.Completed;
            resolvedDate = date;
            resolvedTime = time;
            failureReason = string.Empty;
        }

        public void MarkFailed(GameDate date, GameTime time, string reason)
        {
            state = QuestState.Failed;
            resolvedDate = date;
            resolvedTime = time;
            failureReason = string.IsNullOrWhiteSpace(reason) ? "failed" : reason.Trim();
        }

        public string ToSummaryLine()
        {
            string questName = definition != null ? definition.DisplayName : "(missing quest)";
            string objective = definition != null ? definition.BuildObjectiveSummary() : "";
            return $"{questName}: state={state}, progress={progress}, objective={objective}, accepted={acceptedDate} {acceptedTime}, resolved={resolvedDate} {resolvedTime}, failure={failureReason}";
        }
    }
}
