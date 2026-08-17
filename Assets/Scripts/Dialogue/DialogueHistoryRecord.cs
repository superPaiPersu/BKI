using System;
using UnityEngine;

namespace CityStateSim.Dialogue
{
    [Serializable]
    public sealed class DialogueHistoryRecord
    {
        [SerializeField] private string speakerId;
        [SerializeField] private string speakerName;
        [SerializeField, TextArea] private string text;
        [SerializeField] private float realtime;

        public string SpeakerId => speakerId;
        public string SpeakerName => speakerName;
        public string Text => text;
        public float Realtime => realtime;

        public DialogueHistoryRecord(string speakerId, string speakerName, string text, float realtime)
        {
            this.speakerId = speakerId;
            this.speakerName = speakerName;
            this.text = text;
            this.realtime = realtime;
        }

        public string ToDisplayText()
        {
            return string.IsNullOrWhiteSpace(speakerName) ? text : $"{speakerName}: {text}";
        }
    }
}
