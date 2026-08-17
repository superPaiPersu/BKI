using CityStateSim.AI;
using CityStateSim.Behavior;
using CityStateSim.Dialogue;
using CityStateSim.NPC;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerDialogueBox : MonoBehaviour
{
    public const string RelationshipIconResourcesPath = "UI/RelationshipHearts";
    const string RelationshipIconFilePrefix = "relationship_heart_";

    public TMP_Text npcName;
    public TMP_Text npcMessage;
    public Image npcIcon;
    public Image relationshipValue;

    [Header("Visibility")]
    public CanvasGroup visibilityGroup;
    public GameObject visualRoot;

    public Sprite defaultIcon;
    public Sprite defaultRelationshipIcon;
    public bool hideOnStart = true;
    public bool autoListenDialogueController = true;
    public bool recordDisplayedDialogue = true;
    public bool recordPlayerLines = true;

    string nowPortraitName;
    NpcPlayerInfo currentNpcInfo;
    NpcBehaviorState currentBehaviorState;
    DialogueController dialogueController;
    DialogueHistorySystem dialogueHistorySystem;
    static readonly Sprite[] cachedRelationshipIcons = new Sprite[11];
    static readonly bool[] warnedMissingRelationshipIcons = new bool[11];
    bool warnedMissingVisibilityTarget;

    void Awake()
    {
        if (visibilityGroup == null)
        {
            visibilityGroup = GetComponent<CanvasGroup>();
        }

        if (autoListenDialogueController)
        {
            dialogueController = FindFirstObjectByType<DialogueController>();
        }

        dialogueHistorySystem = FindFirstObjectByType<DialogueHistorySystem>();
    }

    void OnEnable()
    {
        SubscribeDialogueController();
    }

    void Start()
    {
        if (hideOnStart)
        {
            HideVisual();
        }
    }

    void OnDisable()
    {
        if (dialogueController != null && dialogueController.IsConversationActive)
        {
            dialogueController.EndConversation();
        }

        UnsubscribeDialogueController();
        UnsubscribeCurrentBehaviorState();
    }

    public void ShowText(NpcPlayerInfo npcInfo, string message)
    {
        currentNpcInfo = npcInfo;
        BindBehaviorState(npcInfo != null ? npcInfo.GetComponent<NpcBehaviorState>() : null);

        SetVisible(true);
        RefreshNpcInfo();
        SetMessage(message);
        ChangeMoodIcon();
    }

    public void ShowText(NpcRuntimeState npc, string message)
    {
        if (npc == null)
        {
            return;
        }

        NpcPlayerInfo info = npc.GetComponent<NpcPlayerInfo>();
        if (info == null)
        {
            Debug.LogWarning($"[PlayerDialogueBox] {npc.name} has no NpcPlayerInfo component.", npc);
            return;
        }

        ShowText(info, message);
    }

    public void SetMessage(string message)
    {
        if (npcMessage != null)
        {
            npcMessage.text = message ?? string.Empty;
        }
    }

    public void ChangeMoodIcon()
    {
        string portraitName = GetCurrentPortraitName();
        nowPortraitName = portraitName;

        Sprite icon = defaultIcon;
        string npcId = currentNpcInfo != null ? currentNpcInfo.NpcId : string.Empty;
        string npcName = currentNpcInfo != null ? currentNpcInfo.DisplayName : string.Empty;
        Sprite loadedIcon = NpcPortraitCatalog.LoadPortrait(npcId, npcName, portraitName);
        if (loadedIcon == null)
        {
            string fallbackPortraitName = NpcPortraitCatalog.GetFallbackPortraitName(npcId, npcName, "neutral");
            if (!string.IsNullOrWhiteSpace(fallbackPortraitName)
                && !string.Equals(fallbackPortraitName, portraitName, System.StringComparison.OrdinalIgnoreCase))
            {
                loadedIcon = NpcPortraitCatalog.LoadPortrait(npcId, npcName, fallbackPortraitName);
                if (loadedIcon != null)
                {
                    portraitName = fallbackPortraitName;
                    nowPortraitName = fallbackPortraitName;
                }
            }
        }

        if (loadedIcon != null)
        {
            icon = loadedIcon;
        }
        else if (!string.IsNullOrWhiteSpace(portraitName))
        {
            Debug.LogWarning($"[PlayerDialogueBox] Portrait '{portraitName}' was not found in Resources/{NpcPortraitCatalog.DescribePortraitPath(npcId, npcName)} or Resources/{NpcPortraitCatalog.SharedResourcesPath}.", this);
        }

        if (npcIcon != null)
        {
            npcIcon.sprite = icon;
            npcIcon.enabled = icon != null;
        }
    }

    public void Hide()
    {
        if (dialogueController != null && dialogueController.IsConversationActive)
        {
            dialogueController.EndConversation();
            return;
        }

        HideVisual();
    }

    public void CloseConversation()
    {
        Hide();
    }

    void HideVisual()
    {
        SetVisible(false);
    }

    void RefreshNpcInfo()
    {
        if (currentNpcInfo == null)
        {
            return;
        }

        if (npcName != null)
        {
            npcName.text = currentNpcInfo.DisplayName;
        }

        if (relationshipValue != null)
        {
            int level = Mathf.Clamp(currentNpcInfo.RelationshipLevel, 1, 10);
            Sprite icon = LoadRelationshipIcon(level);

            relationshipValue.sprite = icon;
            relationshipValue.enabled = icon != null;
        }
    }

    string GetCurrentPortraitName()
    {
        string npcId = currentNpcInfo != null ? currentNpcInfo.NpcId : string.Empty;
        string npcName = currentNpcInfo != null ? currentNpcInfo.DisplayName : string.Empty;
        if (currentNpcInfo != null)
        {
            string mood = currentNpcInfo.CurrentMood;
            return string.IsNullOrWhiteSpace(mood)
                ? NpcPortraitCatalog.GetFallbackPortraitName(npcId, npcName, "neutral")
                : mood.Trim();
        }

        string emotion = currentBehaviorState != null ? currentBehaviorState.Emotion : string.Empty;
        return string.IsNullOrWhiteSpace(emotion)
            ? NpcPortraitCatalog.GetFallbackPortraitName(npcId, npcName, "neutral")
            : emotion.Trim();
    }

    Sprite LoadRelationshipIcon(int level)
    {
        if (cachedRelationshipIcons[level] != null)
        {
            return cachedRelationshipIcons[level];
        }

        string resourceName = $"{RelationshipIconResourcesPath}/{RelationshipIconFilePrefix}{level:00}";
        Sprite icon = Resources.Load<Sprite>(resourceName);
        if (icon != null)
        {
            cachedRelationshipIcons[level] = icon;
            return icon;
        }

        if (!warnedMissingRelationshipIcons[level])
        {
            warnedMissingRelationshipIcons[level] = true;
            Debug.LogWarning($"[PlayerDialogueBox] Relationship icon '{resourceName}' was not found in Resources.", this);
        }

        return defaultRelationshipIcon;
    }

    void SubscribeDialogueController()
    {
        if (!autoListenDialogueController || dialogueController == null)
        {
            return;
        }

        dialogueController.ConversationStarted += HandleConversationStarted;
        dialogueController.LineAdded += HandleDialogueLineAdded;
        dialogueController.ConversationEnded += HandleConversationEnded;
    }

    void UnsubscribeDialogueController()
    {
        if (dialogueController == null)
        {
            return;
        }

        dialogueController.ConversationStarted -= HandleConversationStarted;
        dialogueController.LineAdded -= HandleDialogueLineAdded;
        dialogueController.ConversationEnded -= HandleConversationEnded;
    }

    void HandleConversationStarted(NpcRuntimeState npc)
    {
        if (npc == null)
        {
            return;
        }

        ShowText(npc, string.Empty);
    }

    void HandleDialogueLineAdded(DialogueLine line)
    {
        if (line == null || currentNpcInfo == null)
        {
            return;
        }

        if (line.SpeakerId == currentNpcInfo.NpcId)
        {
            SetMessage(line.Text);
            RefreshNpcInfo();
            ChangeMoodIcon();
            RecordDisplayedLine(line);
            return;
        }

        if (recordPlayerLines && gameObject.activeInHierarchy)
        {
            RecordDisplayedLine(line);
        }
    }

    void HandleConversationEnded(NpcRuntimeState npc)
    {
        HideVisual();
        currentNpcInfo = null;
        UnsubscribeCurrentBehaviorState();
    }

    void BindBehaviorState(NpcBehaviorState state)
    {
        if (currentBehaviorState == state)
        {
            return;
        }

        UnsubscribeCurrentBehaviorState();
        currentBehaviorState = state;
        if (currentBehaviorState != null)
        {
            currentBehaviorState.DecisionApplied += HandleDecisionApplied;
        }
    }

    void UnsubscribeCurrentBehaviorState()
    {
        if (currentBehaviorState != null)
        {
            currentBehaviorState.DecisionApplied -= HandleDecisionApplied;
            currentBehaviorState = null;
        }
    }

    void HandleDecisionApplied(NpcBehaviorState state, CityStateSim.AI.NpcAiDecision decision)
    {
        RefreshNpcInfo();
        ChangeMoodIcon();
    }
    void RecordDisplayedLine(DialogueLine line)
    {
        if (recordDisplayedDialogue && dialogueHistorySystem != null && gameObject.activeInHierarchy)
        {
            dialogueHistorySystem.AddDisplayedLine(line);
        }
    }

    void SetVisible(bool visible)
    {
        bool changedAnyTarget = false;

        if (visibilityGroup != null)
        {
            visibilityGroup.alpha = visible ? 1f : 0f;
            visibilityGroup.interactable = visible;
            visibilityGroup.blocksRaycasts = visible;
            changedAnyTarget = true;
        }

        if (visualRoot != null)
        {
            if (visualRoot == gameObject)
            {
                Debug.LogWarning("[PlayerDialogueBox] visualRoot cannot be the same GameObject as PlayerDialogueBox, because disabling it would stop dialogue event listening.", this);
            }
            else
            {
                visualRoot.SetActive(visible);
                changedAnyTarget = true;
            }
        }

        if (!changedAnyTarget && !warnedMissingVisibilityTarget)
        {
            warnedMissingVisibilityTarget = true;
            Debug.LogWarning("[PlayerDialogueBox] Add a CanvasGroup to this object or assign visualRoot. Do not hide PlayerDialogueBox by disabling its own GameObject.", this);
        }
    }
}
