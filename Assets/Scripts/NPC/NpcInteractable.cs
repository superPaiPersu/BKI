using CityStateSim.Behavior;
using CityStateSim.Dialogue;
using CityStateSim.Interactions;
using UnityEngine;

namespace CityStateSim.NPC
{
    [RequireComponent(typeof(NpcRuntimeState))]
    public sealed class NpcInteractable : MonoBehaviour, IInteractable
    {
        [SerializeField] private string interactionLabel = "Talk";
        [SerializeField] private bool canInteract = true;
        [SerializeField] private bool faceInteractor = true;
        [SerializeField] private bool stopWhenInteracted = true;
        [SerializeField] private bool playerStartsConversation = true;
        [SerializeField] private bool requestDecisionOnInteract = true;
        [SerializeField] private string observedEventSummary = "Player started conversation: the player approached to talk.";

        [Header("Optional References")]
        [SerializeField] private NpcBehaviorController behaviorController;
        [SerializeField] private NpcActionExecutor actionExecutor;
        [SerializeField] private NpcMovementAgent movementAgent;
        [SerializeField] private DialogueController dialogueController;
        [SerializeField] private ConversationArbiter conversationArbiter;

        private const string PlayerDialoguePauseReason = "player_dialogue";
        private NpcRuntimeState runtimeState;

        public string InteractionLabel => interactionLabel;

        private void Awake()
        {
            runtimeState = GetComponent<NpcRuntimeState>();

            if (behaviorController == null)
            {
                behaviorController = GetComponent<NpcBehaviorController>();
            }

            if (actionExecutor == null)
            {
                actionExecutor = GetComponent<NpcActionExecutor>();
            }

            if (movementAgent == null)
            {
                movementAgent = GetComponent<NpcMovementAgent>();
            }

            if (dialogueController == null)
            {
                dialogueController = FindFirstObjectByType<DialogueController>();
            }

            if (conversationArbiter == null)
            {
                conversationArbiter = FindFirstObjectByType<ConversationArbiter>();
            }
        }

        public bool CanInteract(GameObject interactor)
        {
            return canInteract
                && runtimeState != null
                && runtimeState.PresenceMode == NpcPresenceMode.World
                && !global::DayOverCheck.IsUserInputLocked
                && (dialogueController == null || !dialogueController.IsConversationActive)
                && (dialogueController == null || !dialogueController.HasPendingReplyFor(runtimeState))
                && (conversationArbiter == null || !conversationArbiter.IsNpcInConversation(runtimeState))
                && (behaviorController == null || !behaviorController.RequestInFlight);
        }

        public void Interact(GameObject interactor)
        {
            StartDialogue(interactor);
        }

        public void StartDialogue(GameObject interactor)
        {
            if (!CanInteract(interactor))
            {
                return;
            }

            if (stopWhenInteracted)
            {
                movementAgent?.SetPause(PlayerDialoguePauseReason, true);
            }

            if (faceInteractor && interactor != null)
            {
                actionExecutor?.FaceActor(interactor);
                if (actionExecutor == null)
                {
                    movementAgent?.Face(interactor.transform.position);
                }
            }

            if (dialogueController == null || !dialogueController.TryStartConversation(runtimeState, interactor))
            {
                if (stopWhenInteracted)
                {
                    movementAgent?.SetPause(PlayerDialoguePauseReason, false);
                }

                return;
            }

            if (!playerStartsConversation && requestDecisionOnInteract && behaviorController != null)
            {
                dialogueController.TryRequestCurrentNpcReply(
                    "PlayerDialogue: The current conversation partner is player. " +
                    "The player just approached to talk. Reply to the player, not to any active task target. " +
                    observedEventSummary);
            }
        }
    }
}
