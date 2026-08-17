using System.Collections.Generic;
using System.Text;
using CityStateSim.Behavior;
using CityStateSim.Dialogue;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class LogUIDisplayer : MonoBehaviour
{
    [Header("References")]
    public TMP_Text logText;
    [SerializeField] private DialogueHistorySystem dialogueHistorySystem;
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private RectTransform contentRect;

    [Header("Style")]
    public Color roleColor = new Color(1f, 0.82f, 0.35f);
    public Color textColor = Color.white;
    [SerializeField] private Color separatorColor = new Color(1f, 1f, 1f, 0.35f);
    [SerializeField] private bool useRichText;

    [Header("Policy")]
    [SerializeField, Min(1)] private int maxLogs = 80;
    [SerializeField] private bool subscribeDialogueHistory = true;
    [SerializeField] private bool includeNpcThinkingLogs;
    [SerializeField] private bool rebuildFromExistingHistoryOnStart = true;
    [SerializeField] private bool logMissingReferences = true;
    [SerializeField] private bool autoScrollToLatest = true;
    [SerializeField] private bool configureContentLayout = true;
    [SerializeField] private bool configureViewportLayout = true;
    [SerializeField, Min(0f)] private float bottomPadding = 32f;

    private readonly List<LogEntry> entries = new List<LogEntry>();
    private readonly StringBuilder builder = new StringBuilder();
    private CanvasGroup canvasGroup;
    private Coroutine scrollToLatestCoroutine;
    private bool initialized;
    private bool isOpen = true;
    private bool subscribedNpcThinkingLogs;
    private bool warnedMissingText;
    private bool warnedMissingHistory;

    private void Awake()
    {
        initialized = true;
        EnsureCanvasGroup();

        if (logText == null)
        {
            logText = GetComponentInChildren<TMP_Text>(true);
        }

        EnsureReferences();
        ConfigureScrollContentLayout();
    }

    private void OnEnable()
    {
        EnsureSubscribed();
        SyncNpcThinkingLogSubscription();
    }

    private void Start()
    {
        if (rebuildFromExistingHistoryOnStart)
        {
            RebuildFromDialogueHistory();
        }
        else
        {
            RefreshText();
        }
        // Close();
    }

    private void Update()
    {
        if (subscribeDialogueHistory && dialogueHistorySystem == null)
        {
            EnsureSubscribed();
        }
    }

    private void OnDisable()
    {
        if (dialogueHistorySystem != null)
        {
            dialogueHistorySystem.RecordAdded -= HandleDialogueHistoryRecordAdded;
        }

        UnsubscribeNpcThinkingLogs();
        CancelScrollToLatest();
    }

    public void AddLog(string role, string text)
    {
        if (string.IsNullOrWhiteSpace(role) && string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        entries.Add(new LogEntry(role, text));
        TrimOverflow();
        RefreshText();
    }

    public void Clear()
    {
        entries.Clear();
        RefreshText();
    }

    public void SetIncludeNpcThinkingLogs(bool value)
    {
        if (includeNpcThinkingLogs == value)
        {
            return;
        }

        includeNpcThinkingLogs = value;
        SyncNpcThinkingLogSubscription();
    }

    public void ToggleNpcThinkingLogs()
    {
        SetIncludeNpcThinkingLogs(!includeNpcThinkingLogs);
    }

    public void Close()
    {
        EnsureInitialized();
        SetOpen(false);
    }

    public void Open()
    {
        EnsureInitialized();
        SetOpen(true);
    }

    public void Toggle()
    {
        EnsureInitialized();
        if (isOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    private void RebuildFromDialogueHistory()
    {
        EnsureReferences();
        entries.Clear();
        if (dialogueHistorySystem != null)
        {
            IReadOnlyList<DialogueHistoryRecord> records = dialogueHistorySystem.Records;
            for (int i = 0; i < records.Count; i++)
            {
                DialogueHistoryRecord record = records[i];
                AddLog(record.SpeakerName, record.Text);
            }
        }

        RefreshText();
    }

    private void HandleDialogueHistoryRecordAdded(DialogueHistoryRecord record)
    {
        if (record != null)
        {
            AddLog(record.SpeakerName, record.Text);
        }
    }

    private void HandleNpcThinkingLogEmitted(string role, string text)
    {
        AddLog(role, text);
    }

    private void RefreshText()
    {
        EnsureReferences();
        if (logText == null)
        {
            if (logMissingReferences && !warnedMissingText)
            {
                warnedMissingText = true;
                Debug.LogWarning("[LogUIDisplayer] logText is missing. Assign a TMP_Text or place one under this object.", this);
            }

            return;
        }

        builder.Clear();
        for (int i = 0; i < entries.Count; i++)
        {
            LogEntry entry = entries[i];
            AppendEntry(entry);
            if (i < entries.Count - 1)
            {
                AppendSeparator();
            }
        }

        logText.richText = false;
        logText.text = builder.ToString();
        UpdateTextAndContentSize();
        ScheduleScrollToLatest();
    }

    private void AppendEntry(LogEntry entry)
    {
        string role = string.IsNullOrWhiteSpace(entry.Role) ? "Log" : entry.Role.Trim();
        string text = string.IsNullOrWhiteSpace(entry.Text) ? string.Empty : entry.Text.Trim();

        builder.Append(StripRichTextTags(role));
        builder.Append(":\n");
        builder.Append(StripRichTextTags(text));
        builder.AppendLine();
    }

    private void AppendSeparator()
    {
        builder.AppendLine("----------");
    }

    private void TrimOverflow()
    {
        while (entries.Count > maxLogs)
        {
            entries.RemoveAt(0);
        }
    }

    private void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        EnsureCanvasGroup();
    }

    private void SetOpen(bool value)
    {
        isOpen = value;
        EnsureCanvasGroup();
        if (canvasGroup != null)
        {
            canvasGroup.alpha = value ? 1f : 0f;
            canvasGroup.interactable = value;
            canvasGroup.blocksRaycasts = value;
            return;
        }
    }

    private void EnsureCanvasGroup()
    {
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
        }

        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
    }

    private void EnsureReferences()
    {
        if (logText == null)
        {
            logText = GetComponentInChildren<TMP_Text>(true);
        }

        if (scrollRect == null)
        {
            scrollRect = logText != null
                ? logText.GetComponentInParent<ScrollRect>()
                : GetComponentInChildren<ScrollRect>(true);
        }

        if (contentRect == null && scrollRect != null)
        {
            contentRect = scrollRect.content;
        }

        if (contentRect == null && logText != null)
        {
            contentRect = logText.rectTransform != null
                ? logText.rectTransform.parent as RectTransform
                : null;
        }

        if (dialogueHistorySystem == null)
        {
            dialogueHistorySystem = FindFirstObjectByType<DialogueHistorySystem>();
            if (dialogueHistorySystem == null && logMissingReferences && !warnedMissingHistory)
            {
                warnedMissingHistory = true;
                Debug.LogWarning("[LogUIDisplayer] DialogueHistorySystem was not found yet.", this);
            }
        }
    }

    private void ConfigureScrollContentLayout()
    {
        if (!configureContentLayout)
        {
            return;
        }

        EnsureReferences();
        if (contentRect == null)
        {
            return;
        }

        ConfigureViewportRect();
        ConfigureContentRect();

        VerticalLayoutGroup layoutGroup = contentRect.GetComponent<VerticalLayoutGroup>();
        if (layoutGroup != null)
        {
            layoutGroup.childControlWidth = true;
            layoutGroup.childControlHeight = true;
            layoutGroup.childForceExpandWidth = true;
            layoutGroup.childForceExpandHeight = false;
            layoutGroup.padding.bottom = Mathf.Max(layoutGroup.padding.bottom, Mathf.RoundToInt(bottomPadding));
        }
    }

    private void UpdateTextAndContentSize()
    {
        if (logText == null)
        {
            return;
        }

        RectTransform textRect = logText.rectTransform;
        if (textRect == null)
        {
            return;
        }

        ConfigureScrollContentLayout();
        ConfigureLogTextRect(textRect);
        logText.ForceMeshUpdate();

        RectOffset padding = GetContentPadding();
        float width = textRect.rect.width;
        if (scrollRect != null && scrollRect.viewport != null && scrollRect.viewport.rect.width > 1f)
        {
            width = scrollRect.viewport.rect.width - padding.horizontal;
        }

        if (width <= 1f && contentRect != null)
        {
            width = contentRect.rect.width - padding.horizontal;
        }

        if (width > 1f)
        {
            float preferredHeight = Mathf.Ceil(logText.GetPreferredValues(logText.text, width, Mathf.Infinity).y);
            LayoutElement layoutElement = logText.GetComponent<LayoutElement>();
            if (layoutElement == null)
            {
                layoutElement = logText.gameObject.AddComponent<LayoutElement>();
            }

            layoutElement.preferredWidth = width;
            layoutElement.minWidth = width;
            layoutElement.preferredHeight = preferredHeight;
            layoutElement.minHeight = preferredHeight;
            textRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            textRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, preferredHeight);
            UpdateContentHeight(preferredHeight, padding);
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(textRect);
        if (contentRect != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
        }
    }

    private void ConfigureViewportRect()
    {
        if (!configureViewportLayout || scrollRect == null || scrollRect.viewport == null)
        {
            return;
        }

        RectTransform viewport = scrollRect.viewport;
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = Vector2.zero;
        viewport.pivot = new Vector2(0f, 1f);
    }

    private void ConfigureContentRect()
    {
        if (contentRect == null)
        {
            return;
        }

        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0f, 1f);
        contentRect.anchoredPosition = new Vector2(0f, contentRect.anchoredPosition.y);
    }

    private void ConfigureLogTextRect(RectTransform textRect)
    {
        textRect.anchorMin = new Vector2(0f, 1f);
        textRect.anchorMax = new Vector2(1f, 1f);
        textRect.pivot = new Vector2(0f, 1f);
    }

    private RectOffset GetContentPadding()
    {
        if (contentRect == null)
        {
            return new RectOffset(0, 0, 0, Mathf.RoundToInt(bottomPadding));
        }

        VerticalLayoutGroup layoutGroup = contentRect.GetComponent<VerticalLayoutGroup>();
        if (layoutGroup == null)
        {
            return new RectOffset(0, 0, 0, Mathf.RoundToInt(bottomPadding));
        }

        RectOffset source = layoutGroup.padding;
        return new RectOffset(
            source.left,
            source.right,
            source.top,
            Mathf.Max(source.bottom, Mathf.RoundToInt(bottomPadding)));
    }

    private void UpdateContentHeight(float textPreferredHeight, RectOffset padding)
    {
        if (contentRect == null)
        {
            return;
        }

        float contentHeight = textPreferredHeight + padding.vertical;
        if (scrollRect != null && scrollRect.viewport != null)
        {
            contentHeight = Mathf.Max(contentHeight, scrollRect.viewport.rect.height);
        }

        contentRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Ceil(contentHeight));
    }

    private void ScheduleScrollToLatest()
    {
        if (!autoScrollToLatest || scrollRect == null)
        {
            return;
        }

        CancelScrollToLatest();
        if (isActiveAndEnabled)
        {
            scrollToLatestCoroutine = StartCoroutine(ScrollToLatestAfterLayout());
        }
        else
        {
            ScrollToLatestNow();
        }
    }

    private System.Collections.IEnumerator ScrollToLatestAfterLayout()
    {
        yield return null;
        UpdateTextAndContentSize();
        ScrollToLatestNow();
        yield return new WaitForEndOfFrame();
        UpdateTextAndContentSize();
        ScrollToLatestNow();
        scrollToLatestCoroutine = null;
    }

    private void ScrollToLatestNow()
    {
        if (scrollRect == null)
        {
            return;
        }

        Canvas.ForceUpdateCanvases();
        if (contentRect != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
        }

        scrollRect.StopMovement();
        scrollRect.velocity = Vector2.zero;
        scrollRect.verticalNormalizedPosition = 0f;
        scrollRect.normalizedPosition = new Vector2(scrollRect.normalizedPosition.x, 0f);

        if (contentRect != null && scrollRect.viewport != null)
        {
            float overflow = contentRect.rect.height - scrollRect.viewport.rect.height;
            Vector2 anchoredPosition = contentRect.anchoredPosition;
            anchoredPosition.y = overflow > 0f ? overflow : 0f;
            contentRect.anchoredPosition = anchoredPosition;
        }

        Canvas.ForceUpdateCanvases();
    }

    private void CancelScrollToLatest()
    {
        if (scrollToLatestCoroutine == null)
        {
            return;
        }

        StopCoroutine(scrollToLatestCoroutine);
        scrollToLatestCoroutine = null;
    }

    private void EnsureSubscribed()
    {
        EnsureReferences();
        if (!subscribeDialogueHistory || dialogueHistorySystem == null)
        {
            return;
        }

        dialogueHistorySystem.RecordAdded -= HandleDialogueHistoryRecordAdded;
        dialogueHistorySystem.RecordAdded += HandleDialogueHistoryRecordAdded;
    }

    private void SyncNpcThinkingLogSubscription()
    {
        if (includeNpcThinkingLogs && isActiveAndEnabled)
        {
            SubscribeNpcThinkingLogs();
        }
        else
        {
            UnsubscribeNpcThinkingLogs();
        }
    }

    private void SubscribeNpcThinkingLogs()
    {
        if (subscribedNpcThinkingLogs)
        {
            return;
        }

        NpcBehaviorDebugLogger.UiLogEmitted += HandleNpcThinkingLogEmitted;
        subscribedNpcThinkingLogs = true;
    }

    private void UnsubscribeNpcThinkingLogs()
    {
        if (!subscribedNpcThinkingLogs)
        {
            return;
        }

        NpcBehaviorDebugLogger.UiLogEmitted -= HandleNpcThinkingLogEmitted;
        subscribedNpcThinkingLogs = false;
    }

    private static string StripRichTextTags(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        const string ColorOpenPrefix = "<color";
        const string ColorCloseTag = "</color>";

        StringBuilder cleaned = new StringBuilder(value.Length);
        int index = 0;
        while (index < value.Length)
        {
            if (StartsWithIgnoreCase(value, index, ColorCloseTag))
            {
                index += ColorCloseTag.Length;
                continue;
            }

            if (StartsWithIgnoreCase(value, index, ColorOpenPrefix))
            {
                int end = value.IndexOf('>', index);
                if (end >= 0)
                {
                    index = end + 1;
                    continue;
                }
            }

            cleaned.Append(value[index]);
            index++;
        }

        return cleaned.ToString();
    }

    private static bool StartsWithIgnoreCase(string value, int startIndex, string prefix)
    {
        if (string.IsNullOrEmpty(value)
            || string.IsNullOrEmpty(prefix)
            || startIndex < 0
            || startIndex + prefix.Length > value.Length)
        {
            return false;
        }

        return string.Compare(value, startIndex, prefix, 0, prefix.Length, true) == 0;
    }

    private readonly struct LogEntry
    {
        public LogEntry(string role, string text)
        {
            Role = role;
            Text = text;
        }

        public string Role { get; }
        public string Text { get; }
    }
}
