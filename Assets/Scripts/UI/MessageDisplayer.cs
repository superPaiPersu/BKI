using System.Collections.Generic;
using CityStateSim.AI;
using CityStateSim.Behavior;
using CityStateSim.Dialogue;
using CityStateSim.NPC;
using UnityEngine;

public class MessageDisplayer : MonoBehaviour
{
    public GameObject messageboxPrefab;
    public Transform poolRoot;
    public Camera targetCamera;
    public Vector3 worldOffset = new Vector3(0f, 1.4f, 0f);
    public bool useScreenSpacePosition = true;
    public int initialPoolSize = 8;
    public bool recordDisplayedDialogue = true;
    public bool listenNpcBehaviorDialogue = true;
    public bool showNonDialogueDecisionTextAsBubble;
    public float npcDialogueMinVisibleSeconds = 4f;
    public float npcDialogueExtraVisibleSeconds = 0.75f;
    public float fallbackCharactersPerSecond = 24f;
    public float duplicateMessageSuppressionSeconds = 20f;
    public bool logDebug;

    List<MessageBox> boxPool;
    readonly Dictionary<NpcRuntimeState, MessageBox> activeBoxesByNpc = new Dictionary<NpcRuntimeState, MessageBox>();
    readonly Dictionary<NpcRuntimeState, Coroutine> hideCoroutinesByNpc = new Dictionary<NpcRuntimeState, Coroutine>();
    readonly Dictionary<NpcRuntimeState, string> lastShownTextByNpc = new Dictionary<NpcRuntimeState, string>();
    readonly Dictionary<NpcRuntimeState, float> lastShownRealtimeByNpc = new Dictionary<NpcRuntimeState, float>();
    readonly HashSet<NpcBehaviorState> subscribedBehaviorStates = new HashSet<NpcBehaviorState>();
    readonly Dictionary<string, float> recentHistoryKeys = new Dictionary<string, float>();
    DialogueController dialogueController;
    DialogueHistorySystem dialogueHistorySystem;

    void Awake()
    {
        if (poolRoot == null)
        {
            poolRoot = transform;
        }

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        boxPool = new List<MessageBox>();
        dialogueController = FindFirstObjectByType<DialogueController>();
        dialogueHistorySystem = FindFirstObjectByType<DialogueHistorySystem>();
        WarmPool(initialPoolSize);
        RefreshNpcBehaviorSubscriptions();
    }

    void OnEnable()
    {
        if (dialogueController != null)
        {
            dialogueController.LineAdded += HandleDialogueLineAdded;
            dialogueController.ConversationEnded += HandleConversationEnded;
        }

        RefreshNpcBehaviorSubscriptions();
    }

    void OnDisable()
    {
        if (dialogueController != null)
        {
            dialogueController.LineAdded -= HandleDialogueLineAdded;
            dialogueController.ConversationEnded -= HandleConversationEnded;
        }

        UnsubscribeNpcBehaviorStates();
    }

    public MessageBox AllocateNewMessageBox()
    {
        MessageBox box = CreateMessageBox();
        if (box != null)
        {
            boxPool.Add(box);
        }

        return box;
    }

    public void ShowMessage(NpcRuntimeState npc, string text)
    {
        if (npc == null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (!CanShowWorldBubble(npc))
        {
            HideMessage(npc);
            return;
        }

        if (ShouldSuppressDuplicateMessage(npc, text))
        {
            return;
        }

        MessageBox box = GetOrCreateActiveBox(npc);
        if (box == null)
        {
            return;
        }

        box.SetText(text);
        ScheduleAutoHide(npc, text, box);
        RecordDisplayedLine(npc, text);
    }

    public void ShowMessage(NpcRuntimeState npc, DialogueLine line)
    {
        if (npc == null || line == null || string.IsNullOrWhiteSpace(line.Text))
        {
            return;
        }

        if (!CanShowWorldBubble(npc))
        {
            HideMessage(npc);
            return;
        }

        if (ShouldSuppressDuplicateMessage(npc, line.Text))
        {
            return;
        }

        MessageBox box = GetOrCreateActiveBox(npc);
        if (box == null)
        {
            return;
        }

        box.SetText(line.Text);
        ScheduleAutoHide(npc, line.Text, box);
        RecordDisplayedLine(line);
    }

    public void ShowMessageWithoutRecording(NpcRuntimeState npc, string text)
    {
        ShowMessageWithoutHistory(npc, text);
    }

    public void ShowMessage(Transform target, string text)
    {
        if (target == null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        MessageBox box = GetFreeBox();
        if (box == null)
        {
            return;
        }

        box.Bind(target, worldOffset, targetCamera, useScreenSpacePosition);
        box.SetText(text);
    }

    public void ShowMessageByNpcId(string npcId, string text)
    {
        NpcRuntimeState npc = FindNpcById(npcId);
        ShowMessage(npc, text);
    }

    public void HideMessage(NpcRuntimeState npc)
    {
        if (npc == null || !activeBoxesByNpc.TryGetValue(npc, out MessageBox box))
        {
            return;
        }

        activeBoxesByNpc.Remove(npc);
        CancelAutoHide(npc);
        box.Release();
    }

    public void HideMessageByNpcId(string npcId)
    {
        HideMessage(FindNpcById(npcId));
    }

    public void HideAll()
    {
        foreach (MessageBox box in activeBoxesByNpc.Values)
        {
            if (box != null)
            {
                box.Release();
            }
        }

        activeBoxesByNpc.Clear();
        lastShownTextByNpc.Clear();
        lastShownRealtimeByNpc.Clear();
        CancelAllAutoHide();
    }

    public void RefreshNpcBehaviorSubscriptions()
    {
        if (!listenNpcBehaviorDialogue)
        {
            return;
        }

        NpcBehaviorState[] states = FindObjectsByType<NpcBehaviorState>(FindObjectsSortMode.None);
        for (int i = 0; i < states.Length; i++)
        {
            NpcBehaviorState state = states[i];
            if (state == null || subscribedBehaviorStates.Contains(state))
            {
                continue;
            }

            state.DecisionApplied += HandleNpcDecisionApplied;
            subscribedBehaviorStates.Add(state);
            if (logDebug)
            {
                Debug.Log($"[MessageDisplayer] Subscribed to {state.name}.", this);
            }
        }
    }

    void WarmPool(int count)
    {
        for (int i = 0; i < count; i++)
        {
            AllocateNewMessageBox();
        }
    }

    MessageBox GetOrCreateActiveBox(NpcRuntimeState npc)
    {
        if (activeBoxesByNpc.TryGetValue(npc, out MessageBox activeBox) && activeBox != null)
        {
            return activeBox;
        }

        MessageBox box = GetFreeBox();
        if (box == null)
        {
            return null;
        }

        box.Bind(npc.transform, worldOffset, targetCamera, useScreenSpacePosition);
        activeBoxesByNpc[npc] = box;
        return box;
    }

    MessageBox GetFreeBox()
    {
        for (int i = 0; i < boxPool.Count; i++)
        {
            MessageBox box = boxPool[i];
            if (box != null && !box.IsInUse)
            {
                return box;
            }
        }

        return AllocateNewMessageBox();
    }

    MessageBox CreateMessageBox()
    {
        if (messageboxPrefab == null)
        {
            Debug.LogError("[MessageDisplayer] Messagebox prefab is not assigned.", this);
            return null;
        }

        GameObject instance = Instantiate(messageboxPrefab, poolRoot);
        MessageBox box = instance.GetComponent<MessageBox>();
        if (box == null)
        {
            Debug.LogError("[MessageDisplayer] Messagebox prefab must have a MessageBox component on its root object.", this);
            Destroy(instance);
            return null;
        }

        box.Release();
        return box;
    }

    NpcRuntimeState FindNpcById(string npcId)
    {
        if (string.IsNullOrWhiteSpace(npcId))
        {
            return null;
        }

        NpcRuntimeState[] npcs = FindObjectsByType<NpcRuntimeState>(FindObjectsSortMode.None);
        for (int i = 0; i < npcs.Length; i++)
        {
            NpcRuntimeState npc = npcs[i];
            if (npc != null && npc.Profile != null && npc.Profile.NpcId == npcId)
            {
                return npc;
            }
        }

        return null;
    }

    void HandleDialogueLineAdded(DialogueLine line)
    {
        if (line == null || dialogueController == null)
        {
            return;
        }

        NpcRuntimeState currentNpc = dialogueController.CurrentNpc;
        if (currentNpc == null || currentNpc.Profile == null)
        {
            return;
        }

        if (line.SpeakerId == currentNpc.Profile.NpcId)
        {
            ShowMessage(currentNpc, line);
        }
    }

    void HandleConversationEnded(NpcRuntimeState npc)
    {
        HideMessage(npc);
    }

    void HandleNpcDecisionApplied(NpcBehaviorState state, NpcAiDecision decision)
    {
        if (state == null || decision == null)
        {
            return;
        }

        NpcRuntimeState npc = state.GetComponent<NpcRuntimeState>();
        if (npc == null)
        {
            return;
        }

        if (dialogueController != null && dialogueController.IsConversationWith(npc))
        {
            return;
        }

        if (showNonDialogueDecisionTextAsBubble
            && !IsDialogueHandledByDedicatedUi(decision)
            && !string.IsNullOrWhiteSpace(decision.dialogue))
        {
            ShowMessage(npc, decision.dialogue);
            return;
        }
    }

    static bool IsDialogueHandledByDedicatedUi(NpcAiDecision decision)
    {
        if (decision == null)
        {
            return false;
        }

        return decision.ParsedIntent == NpcIntentType.TalkToNpc
            || decision.ParsedIntent == NpcIntentType.TalkToPlayer
            || decision.ParsedIntent == NpcIntentType.SelfTalk;
    }

    void ShowMessageWithoutHistory(NpcRuntimeState npc, string text)
    {
        if (npc == null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (!CanShowWorldBubble(npc))
        {
            HideMessage(npc);
            return;
        }

        if (ShouldSuppressDuplicateMessage(npc, text))
        {
            return;
        }

        MessageBox box = GetOrCreateActiveBox(npc);
        if (box == null)
        {
            return;
        }

        if (logDebug)
        {
            Debug.Log($"[MessageDisplayer] Showing message for {npc.name}: {text}", this);
        }

        box.SetText(text);
        ScheduleAutoHide(npc, text, box);
    }

    bool ShouldSuppressDuplicateMessage(NpcRuntimeState npc, string text)
    {
        if (npc == null || string.IsNullOrWhiteSpace(text) || duplicateMessageSuppressionSeconds <= 0f)
        {
            return false;
        }

        float now = Time.realtimeSinceStartup;
        string normalizedText = text.Trim();
        if (lastShownTextByNpc.TryGetValue(npc, out string lastText)
            && string.Equals(lastText, normalizedText, System.StringComparison.Ordinal)
            && lastShownRealtimeByNpc.TryGetValue(npc, out float lastShownRealtime)
            && now - lastShownRealtime <= duplicateMessageSuppressionSeconds)
        {
            return true;
        }

        lastShownTextByNpc[npc] = normalizedText;
        lastShownRealtimeByNpc[npc] = now;
        return false;
    }

    void RecordDisplayedLine(NpcRuntimeState npc, string text)
    {
        if (!recordDisplayedDialogue || dialogueHistorySystem == null || npc == null || npc.Profile == null)
        {
            return;
        }

        AddHistoryIfNotRecent(npc.Profile.NpcId, npc.Profile.DisplayName, text);
    }

    void RecordDisplayedLine(DialogueLine line)
    {
        if (!recordDisplayedDialogue || dialogueHistorySystem == null || line == null)
        {
            return;
        }

        AddHistoryIfNotRecent(line.SpeakerId, line.SpeakerName, line.Text);
    }

    void AddHistoryIfNotRecent(string speakerId, string speakerName, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        string key = $"{speakerId}|{speakerName}|{text}";
        float now = Time.realtimeSinceStartup;
        if (recentHistoryKeys.TryGetValue(key, out float lastRealtime) && now - lastRealtime < 0.25f)
        {
            return;
        }

        recentHistoryKeys[key] = now;
        dialogueHistorySystem.AddDisplayedLine(speakerId, speakerName, text);
    }

    void UnsubscribeNpcBehaviorStates()
    {
        foreach (NpcBehaviorState state in subscribedBehaviorStates)
        {
            if (state != null)
            {
                state.DecisionApplied -= HandleNpcDecisionApplied;
            }
        }

        subscribedBehaviorStates.Clear();
    }

    void ScheduleAutoHide(NpcRuntimeState npc, string text, MessageBox box)
    {
        if (npc == null)
        {
            return;
        }

        CancelAutoHide(npc);
        hideCoroutinesByNpc[npc] = StartCoroutine(HideAfterDelay(npc, CalculateVisibleSeconds(text, box)));
    }

    float CalculateVisibleSeconds(string text, MessageBox box)
    {
        float cps = box != null && box.charactersPerSecond > 0f
            ? box.charactersPerSecond
            : Mathf.Max(1f, fallbackCharactersPerSecond);
        int length = string.IsNullOrEmpty(text) ? 0 : text.Length;
        float typewriterSeconds = length / cps;
        return Mathf.Max(npcDialogueMinVisibleSeconds, typewriterSeconds + npcDialogueExtraVisibleSeconds);
    }

    void CancelAutoHide(NpcRuntimeState npc)
    {
        if (npc == null || !hideCoroutinesByNpc.TryGetValue(npc, out Coroutine coroutine))
        {
            return;
        }

        if (coroutine != null)
        {
            StopCoroutine(coroutine);
        }

        hideCoroutinesByNpc.Remove(npc);
    }

    void CancelAllAutoHide()
    {
        foreach (Coroutine coroutine in hideCoroutinesByNpc.Values)
        {
            if (coroutine != null)
            {
                StopCoroutine(coroutine);
            }
        }

        hideCoroutinesByNpc.Clear();
    }

    static bool CanShowWorldBubble(NpcRuntimeState npc)
    {
        return npc != null && npc.PresenceMode == NpcPresenceMode.World;
    }

    System.Collections.IEnumerator HideAfterDelay(NpcRuntimeState npc, float delay)
    {
        yield return new WaitForSeconds(delay);
        hideCoroutinesByNpc.Remove(npc);
        HideMessage(npc);
    }
}
