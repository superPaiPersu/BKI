using System;
using UnityEngine;

namespace CityStateSim.Jobs
{
    [Serializable]
    public sealed class JobTaskDefinition
    {
        [SerializeField] private string taskId;
        [SerializeField] private string displayName;
        [SerializeField, TextArea] private string description;
        [SerializeField, Min(1)] private int scoreValue = 1;

        public string TaskId => taskId;
        public string DisplayName => displayName;
        public string Description => description;
        public int ScoreValue => scoreValue;
    }
}
