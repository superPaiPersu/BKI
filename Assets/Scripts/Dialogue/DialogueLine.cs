using System;
using UnityEngine;

namespace CityStateSim.Dialogue
{
    [Serializable]
    public sealed class DialogueLine
    {
        [SerializeField] private string speakerId;
        [SerializeField] private string speakerName;
        [SerializeField, TextArea] private string text;

        public string SpeakerId => speakerId;
        public string SpeakerName => speakerName;
        public string Text => text;

        public DialogueLine(string speakerId, string speakerName, string text)
        {
            this.speakerId = speakerId;
            this.speakerName = speakerName;
            this.text = text;
        }
    }
}
