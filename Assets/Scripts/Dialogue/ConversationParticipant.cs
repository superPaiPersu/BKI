using CityStateSim.NPC;
using UnityEngine;

namespace CityStateSim.Dialogue
{
    public sealed class ConversationParticipant
    {
        private const string ConversationPauseReason = "npc_conversation";

        public ConversationParticipant(NpcRuntimeState npc)
        {
            Npc = npc;
            Movement = npc != null ? npc.GetComponent<NpcMovementAgent>() : null;
            CouldMoveBeforeConversation = Movement == null || Movement.CanMove;
        }

        public NpcRuntimeState Npc { get; }
        public NpcMovementAgent Movement { get; }
        public bool CouldMoveBeforeConversation { get; }

        public string ActorId
        {
            get
            {
                return Npc != null && Npc.Profile != null && !string.IsNullOrWhiteSpace(Npc.Profile.NpcId)
                    ? Npc.Profile.NpcId
                    : string.Empty;
            }
        }

        public string DisplayName => Npc != null && Npc.Profile != null ? Npc.Profile.DisplayName : "Unknown NPC";

        public void StopAndFace(Transform target)
        {
            if (Movement == null)
            {
                return;
            }

            Movement.SetPause(ConversationPauseReason, true);
            if (target != null)
            {
                Movement.Face(target.position);
            }
        }

        public void RestoreMovement()
        {
            if (Movement != null)
            {
                Movement.SetPause(ConversationPauseReason, false);
            }
        }
    }
}
