using System;
using System.Collections.Generic;
using CityStateSim.Movement;
using CityStateSim.NPC;
using UnityEngine;
using UnityEngine.Events;

namespace CityStateSim.Dialogue
{
    public enum PlayerDialogueRequestResult
    {
        Accepted = 0,
        Rejected = 1,
        TimedOut = 2,
        Failed = 3
    }

    public enum PlayerDialogueRequestKind
    {
        Dialogue = 0,
        Follow = 1
    }

    public sealed class PlayerDialogueRequest
    {
        public PlayerDialogueRequest(
            int requestId,
            PlayerDialogueRequestKind kind,
            NpcRuntimeState npc,
            GameObject playerActor,
            string reason,
            string openingLine,
            float timeoutSeconds,
            Action<PlayerDialogueRequestResult> onFinished)
        {
            RequestId = requestId;
            Kind = kind;
            Npc = npc;
            PlayerActor = playerActor;
            Reason = reason ?? string.Empty;
            OpeningLine = openingLine ?? string.Empty;
            CreatedRealtime = Time.realtimeSinceStartup;
            TimeoutSeconds = Mathf.Max(0.1f, timeoutSeconds);
            ExpiresAtRealtime = CreatedRealtime + TimeoutSeconds;
            OnFinished = onFinished;
        }

        public int RequestId { get; }
        public PlayerDialogueRequestKind Kind { get; }
        public NpcRuntimeState Npc { get; }
        public GameObject PlayerActor { get; }
        public string Reason { get; }
        public string OpeningLine { get; }
        public float CreatedRealtime { get; }
        public float TimeoutSeconds { get; }
        public float ExpiresAtRealtime { get; }
        public Action<PlayerDialogueRequestResult> OnFinished { get; }
        public float RemainingSeconds => Mathf.Max(0f, ExpiresAtRealtime - Time.realtimeSinceStartup);
        public bool IsExpired => Time.realtimeSinceStartup >= ExpiresAtRealtime;

        public string DisplayText
        {
            get
            {
                if (Kind == PlayerDialogueRequestKind.Follow)
                {
                    return string.IsNullOrWhiteSpace(Reason) ? "wants to follow you." : Reason;
                }

                if (!string.IsNullOrWhiteSpace(OpeningLine))
                {
                    return OpeningLine;
                }

                return string.IsNullOrWhiteSpace(Reason) ? "wants to talk." : Reason;
            }
        }
    }

    public sealed class PlayerDialogueRequestSystem : MonoBehaviour
    {
        [Serializable]
        public sealed class RequestEvent : UnityEvent<PlayerDialogueRequest>
        {
        }

        [Serializable]
        public sealed class RequestResultEvent : UnityEvent<PlayerDialogueRequest, PlayerDialogueRequestResult>
        {
        }

        [Header("References")]
        [SerializeField] private DialogueController dialogueController;

        [Header("Policy")]
        [SerializeField, Min(0.1f)] private float timeoutSeconds = 8f;
        [SerializeField, Min(1)] private int maxPendingRequests = 3;
        [SerializeField] private bool pauseNpcWhilePending = true;
        [SerializeField] private bool facePlayerWhilePending = true;

        [Header("UI Events")]
        [SerializeField] private RequestEvent requestAdded;
        [SerializeField] private RequestResultEvent requestFinished;
        [SerializeField] private UnityEvent requestListChanged;

        private const string PendingRequestPauseReason = "player_dialogue_request";

        private readonly List<PlayerDialogueRequest> requests = new List<PlayerDialogueRequest>();
        private readonly Dictionary<NpcRuntimeState, NpcMovementAgent> pausedMovementsByNpc = new Dictionary<NpcRuntimeState, NpcMovementAgent>();
        private int nextRequestId = 1;

        public int RequestCount => requests.Count;
        public int MaxPendingRequests => maxPendingRequests;
        public bool HasPendingRequest => requests.Count > 0;
        public RequestEvent RequestAdded => requestAdded;
        public RequestResultEvent RequestFinished => requestFinished;
        public UnityEvent RequestListChanged => requestListChanged;

        private void Awake()
        {
            if (dialogueController == null)
            {
                dialogueController = FindFirstObjectByType<DialogueController>();
            }
        }

        private void Update()
        {
            for (int i = requests.Count - 1; i >= 0; i--)
            {
                if (requests[i].IsExpired)
                {
                    FinishRequest(requests[i], PlayerDialogueRequestResult.TimedOut);
                }
            }
        }

        private void OnDisable()
        {
            for (int i = requests.Count - 1; i >= 0; i--)
            {
                FinishRequest(requests[i], PlayerDialogueRequestResult.Rejected);
            }
        }

        public bool TryShowRequest(
            NpcRuntimeState npc,
            GameObject playerActor,
            string reason,
            string openingLine,
            Action<PlayerDialogueRequestResult> onFinished)
        {
            return TryShowRequest(
                PlayerDialogueRequestKind.Dialogue,
                npc,
                playerActor,
                reason,
                openingLine,
                onFinished);
        }

        public bool TryShowFollowRequest(
            NpcRuntimeState npc,
            GameObject playerActor,
            string reason,
            Action<PlayerDialogueRequestResult> onFinished)
        {
            return TryShowRequest(
                PlayerDialogueRequestKind.Follow,
                npc,
                playerActor,
                reason,
                string.Empty,
                onFinished);
        }

        private bool TryShowRequest(
            PlayerDialogueRequestKind kind,
            NpcRuntimeState npc,
            GameObject playerActor,
            string reason,
            string openingLine,
            Action<PlayerDialogueRequestResult> onFinished)
        {
            if (global::DayOverCheck.IsUserInputLocked)
            {
                onFinished?.Invoke(PlayerDialogueRequestResult.Rejected);
                return true;
            }

            if (npc == null || playerActor == null || HasPendingRequestFor(npc))
            {
                return false;
            }

            if (requests.Count >= maxPendingRequests)
            {
                onFinished?.Invoke(PlayerDialogueRequestResult.Rejected);
                return true;
            }

            if (dialogueController != null && dialogueController.IsConversationActive)
            {
                return false;
            }

            if (kind == PlayerDialogueRequestKind.Dialogue && dialogueController == null)
            {
                return false;
            }

            PlayerDialogueRequest request = new PlayerDialogueRequest(
                nextRequestId++,
                kind,
                npc,
                playerActor,
                reason,
                openingLine,
                timeoutSeconds,
                onFinished);

            PauseNpc(npc, playerActor);
            requests.Add(request);
            requestAdded?.Invoke(request);
            requestListChanged?.Invoke();
            return true;
        }

        public PlayerDialogueRequest GetRequest(int index)
        {
            return index >= 0 && index < requests.Count ? requests[index] : null;
        }

        public void AcceptRequest(PlayerDialogueRequest request)
        {
            TryAcceptRequest(request);
        }

        public bool TryAcceptRequest(PlayerDialogueRequest request)
        {
            if (request == null || !requests.Contains(request))
            {
                return false;
            }

            if (global::DayOverCheck.IsUserInputLocked)
            {
                FinishRequest(request, PlayerDialogueRequestResult.Rejected);
                return false;
            }

            if (request.Kind == PlayerDialogueRequestKind.Follow)
            {
                FinishRequest(request, PlayerDialogueRequestResult.Accepted);
                return true;
            }

            bool started = dialogueController != null
                && dialogueController.TryStartConversationWithOpeningLine(
                    request.Npc,
                    request.PlayerActor,
                    request.OpeningLine);

            FinishRequest(request, started ? PlayerDialogueRequestResult.Accepted : PlayerDialogueRequestResult.Failed);
            return started;
        }

        public void RejectRequest(PlayerDialogueRequest request)
        {
            if (request == null || !requests.Contains(request))
            {
                return;
            }

            FinishRequest(request, PlayerDialogueRequestResult.Rejected);
        }

        public void AcceptRequestById(int requestId)
        {
            AcceptRequest(FindRequestById(requestId));
        }

        public void RejectRequestById(int requestId)
        {
            RejectRequest(FindRequestById(requestId));
        }

        public bool HasPendingRequestFor(NpcRuntimeState npc)
        {
            return FindRequestFor(npc) != null;
        }

        private void PauseNpc(NpcRuntimeState npc, GameObject playerActor)
        {
            NpcMovementAgent movement = npc != null ? npc.GetComponent<NpcMovementAgent>() : null;
            if (pauseNpcWhilePending)
            {
                movement?.SetPause(PendingRequestPauseReason, true);
            }

            if (facePlayerWhilePending && movement != null && playerActor != null)
            {
                movement.Face(playerActor.transform.position);
            }

            if (npc != null && movement != null)
            {
                pausedMovementsByNpc[npc] = movement;
            }
        }

        private void FinishRequest(PlayerDialogueRequest request, PlayerDialogueRequestResult result)
        {
            if (request == null || !requests.Remove(request))
            {
                return;
            }

            UnpauseNpc(request.Npc);
            requestFinished?.Invoke(request, result);
            requestListChanged?.Invoke();
            request.OnFinished?.Invoke(result);
        }

        private void UnpauseNpc(NpcRuntimeState npc)
        {
            if (npc == null)
            {
                return;
            }

            if (pausedMovementsByNpc.TryGetValue(npc, out NpcMovementAgent movement))
            {
                movement?.SetPause(PendingRequestPauseReason, false);
                pausedMovementsByNpc.Remove(npc);
            }
        }

        private PlayerDialogueRequest FindRequestById(int requestId)
        {
            for (int i = 0; i < requests.Count; i++)
            {
                if (requests[i].RequestId == requestId)
                {
                    return requests[i];
                }
            }

            return null;
        }

        private PlayerDialogueRequest FindRequestFor(NpcRuntimeState npc)
        {
            if (npc == null)
            {
                return null;
            }

            for (int i = 0; i < requests.Count; i++)
            {
                if (requests[i].Npc == npc)
                {
                    return requests[i];
                }
            }

            return null;
        }
    }
}
