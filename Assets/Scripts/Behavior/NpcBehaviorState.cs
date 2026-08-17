using System;
using CityStateSim.AI;
using UnityEngine;

namespace CityStateSim.Behavior
{
    public sealed class NpcBehaviorState : MonoBehaviour
    {
        [SerializeField] private NpcBehaviorMode behaviorMode = NpcBehaviorMode.FollowSchedule;
        [SerializeField] private NpcIntentType currentIntent = NpcIntentType.ContinueCurrentAction;
        [SerializeField] private string emotion = "neutral";
        [SerializeField] private string tone = "neutral";
        [SerializeField] private string lastDialogue;
        [SerializeField] private string nextActionPreference;
        [SerializeField] private string targetLocationId;
        [SerializeField] private string targetActorId;

        public NpcBehaviorMode BehaviorMode => behaviorMode;
        public NpcIntentType CurrentIntent => currentIntent;
        public string Emotion => emotion;
        public string Tone => tone;
        public string LastDialogue => lastDialogue;
        public string NextActionPreference => nextActionPreference;
        public string TargetLocationId => targetLocationId;
        public string TargetActorId => targetActorId;

        public event Action<NpcBehaviorState, NpcAiDecision> DecisionApplied;

        public void SetEmotion(string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                emotion = value;
            }
        }

        public void ApplyDecision(NpcAiDecision decision)
        {
            if (decision == null)
            {
                return;
            }

            decision.ClampHints();
            if (!CanExecutableDecisionSpeak(decision.ParsedIntent))
            {
                decision.dialogue = string.Empty;
            }

            behaviorMode = decision.ParsedBehaviorMode;
            currentIntent = decision.ParsedIntent;
            emotion = decision.emotion;
            tone = decision.tone;
            lastDialogue = decision.dialogue;
            nextActionPreference = decision.nextActionPreference;
            targetLocationId = decision.targetLocationId;
            targetActorId = decision.targetActorId;

            DecisionApplied?.Invoke(this, decision);
        }

        private static bool CanExecutableDecisionSpeak(NpcIntentType intent)
        {
            return intent == NpcIntentType.TalkToPlayer
                || intent == NpcIntentType.TalkToNpc
                || intent == NpcIntentType.SelfTalk;
        }

        public void ApplyDialoguePreview(NpcAiDecision decision)
        {
            if (decision == null)
            {
                return;
            }

            decision.ClampHints();
            emotion = decision.emotion;
            tone = decision.tone;
            lastDialogue = decision.dialogue;
            nextActionPreference = decision.nextActionPreference;
        }
    }
}
