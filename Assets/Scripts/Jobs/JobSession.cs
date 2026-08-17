using CityStateSim.Core;

namespace CityStateSim.Jobs
{
    public sealed class JobSession
    {
        public JobDefinition Job { get; }
        public GameDate StartDate { get; }
        public GameTime StartTime { get; }
        public int Score { get; private set; }
        public bool IsActive { get; private set; } = true;

        public JobSession(JobDefinition job, GameDate startDate, GameTime startTime)
        {
            Job = job;
            StartDate = startDate;
            StartTime = startTime;
        }

        public void AddScore(int value)
        {
            if (IsActive)
            {
                Score += value;
            }
        }

        public int CalculatePay()
        {
            return Job != null ? Job.BasePay + Score * Job.PayPerScore : 0;
        }

        public void End()
        {
            IsActive = false;
        }
    }
}
