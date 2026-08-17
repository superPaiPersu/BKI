namespace CityStateSim.Dialogue
{
    public sealed class DialogueTurnResult
    {
        public string Text { get; set; }
        public string Emotion { get; set; }
        public string Tone { get; set; }
        public bool WantsToEnd { get; set; }
        public bool AcceptedInvitation { get; set; }
        public int RelationshipDeltaHint { get; set; }
        public string NextActionPreference { get; set; }
        public string NextSpeakerId { get; set; }
    }
}
