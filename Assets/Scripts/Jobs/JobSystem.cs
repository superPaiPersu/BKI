using System;
using CityStateSim.Core;
using CityStateSim.Memory;
using CityStateSim.Player;
using CityStateSim.Relationships;
using UnityEngine;

namespace CityStateSim.Jobs
{
    public sealed class JobSystem : MonoBehaviour
    {
        [SerializeField] private GameClock clock;
        [SerializeField] private PlayerWallet wallet;
        [SerializeField] private RelationshipSystem relationshipSystem;
        [SerializeField] private MemorySystem memorySystem;
        [SerializeField] private bool logJobs = true;

        private JobSession activeSession;

        public JobSession ActiveSession => activeSession;

        public event Action<JobSession> JobStarted;
        public event Action<JobSession, int> JobEnded;
        public event Action<string> JobFailed;

        private void Awake()
        {
            if (clock == null)
            {
                clock = FindFirstObjectByType<GameClock>();
            }

            if (wallet == null)
            {
                wallet = FindFirstObjectByType<PlayerWallet>();
            }

            if (relationshipSystem == null)
            {
                relationshipSystem = FindFirstObjectByType<RelationshipSystem>();
            }

            if (memorySystem == null)
            {
                memorySystem = FindFirstObjectByType<MemorySystem>();
            }
        }

        public bool CanStartJob(JobDefinition job, out string reason)
        {
            if (job == null)
            {
                reason = "No job selected.";
                return false;
            }

            if (activeSession != null && activeSession.IsActive)
            {
                reason = "A job session is already active.";
                return false;
            }

            if (clock != null && !job.IsAvailableAt(clock.CurrentTime))
            {
                reason = "This job is not available at the current time.";
                return false;
            }

            if (job.RequiredTrust > 0 && relationshipSystem != null && job.Shop != null && job.Shop.Owner != null)
            {
                int trust = relationshipSystem.GetOrCreateToPlayer(job.Shop.Owner).Trust;
                if (trust < job.RequiredTrust)
                {
                    reason = $"Requires trust {job.RequiredTrust}, current trust {trust}.";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }

        public bool StartJob(JobDefinition job)
        {
            if (!CanStartJob(job, out string reason))
            {
                Fail(reason);
                return false;
            }

            GameDate date = clock != null ? clock.CurrentDate : new GameDate(1, 1, 1);
            GameTime time = clock != null ? clock.CurrentTime : new GameTime(0, 0);
            activeSession = new JobSession(job, date, time);
            JobStarted?.Invoke(activeSession);

            if (logJobs)
            {
                Debug.Log($"[Job] Started {job.DisplayName}", this);
            }

            return true;
        }

        public void CompleteTask(JobTaskDefinition task)
        {
            if (activeSession == null || !activeSession.IsActive)
            {
                Fail("No active job session.");
                return;
            }

            int value = task != null ? task.ScoreValue : 1;
            activeSession.AddScore(value);
        }

        public void EndActiveJob()
        {
            if (activeSession == null || !activeSession.IsActive)
            {
                Fail("No active job session.");
                return;
            }

            activeSession.End();
            int pay = activeSession.CalculatePay();
            wallet?.AddMoney(pay);
            ApplyJobConsequences(activeSession, pay);
            JobEnded?.Invoke(activeSession, pay);

            if (logJobs)
            {
                Debug.Log($"[Job] Ended {activeSession.Job.DisplayName}. Score {activeSession.Score}, pay {pay}.", this);
            }

            activeSession = null;
        }

        private void ApplyJobConsequences(JobSession session, int pay)
        {
            JobDefinition job = session.Job;
            if (job == null || job.Shop == null || job.Shop.Owner == null)
            {
                return;
            }

            int affinityDelta = session.Score > 0 ? 1 : 0;
            int trustDelta = session.Score >= 3 ? 1 : 0;
            relationshipSystem?.ApplyPlayerDelta(job.Shop.Owner, trustDelta, affinityDelta, 0);
            memorySystem?.AddMemory(job.Shop.Owner.NpcId, $"Player worked at {job.Shop.DisplayName}, score {session.Score}, pay {pay}.", "job", 4);
        }

        private void Fail(string reason)
        {
            JobFailed?.Invoke(reason);
            if (logJobs)
            {
                Debug.LogWarning($"[Job] {reason}", this);
            }
        }
    }
}
