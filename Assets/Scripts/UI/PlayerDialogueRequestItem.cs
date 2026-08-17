using CityStateSim.Behavior;
using CityStateSim.Dialogue;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class PlayerDialogueRequestItem : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private Image progressFill;
    [SerializeField] private Slider progressSlider;
    [SerializeField] private FollowNpcPanel followNpcPanel;

    private PlayerDialogueRequestSystem requestSystem;
    private PlayerDialogueRequest request;

    private void Awake()
    {
        if (followNpcPanel == null)
        {
            followNpcPanel = FindFirstObjectByType<FollowNpcPanel>();
        }
    }

    public void Bind(PlayerDialogueRequestSystem system, PlayerDialogueRequest dialogueRequest)
    {
        requestSystem = system;
        request = dialogueRequest;
        Refresh();
        UpdateProgress();
    }

    private void Update()
    {
        UpdateProgress();
    }

    public void Accept()
    {
        if (requestSystem == null || request == null)
        {
            return;
        }

        if (request.Kind == PlayerDialogueRequestKind.Follow)
        {
            if (followNpcPanel == null)
            {
                followNpcPanel = FindFirstObjectByType<FollowNpcPanel>();
            }

            if (followNpcPanel == null)
            {
                Debug.LogWarning("[PlayerDialogueRequestItem] Follow request rejected because FollowNpcPanel was not found.", this);
                requestSystem.RejectRequest(request);
                return;
            }

            if (followNpcPanel.CheckFull())
            {
                Debug.Log("[PlayerDialogueRequestItem] Follow request rejected because the follow UI is full.", this);
                requestSystem.RejectRequest(request);
                return;
            }

            NpcActionExecutor npcExecutor = request.Npc != null ? request.Npc.GetComponent<NpcActionExecutor>() : null;
            if (npcExecutor == null)
            {
                Debug.LogWarning("[PlayerDialogueRequestItem] Follow request rejected because the NPC has no NpcActionExecutor.", this);
                requestSystem.RejectRequest(request);
                return;
            }

            if (!requestSystem.TryAcceptRequest(request))
            {
                return;
            }

            if (!followNpcPanel.TryAdd(npcExecutor))
            {
                npcExecutor.StopFollowingPlayer("follow UI could not add this follower after request acceptance.");
            }

            return;
        }

        requestSystem.AcceptRequest(request);
    }

    public void Reject()
    {
        requestSystem?.RejectRequest(request);
    }

    private void Refresh()
    {
        if (label == null)
        {
            return;
        }

        string npcName = request != null && request.Npc != null && request.Npc.Profile != null
            ? request.Npc.Profile.DisplayName
            : "\u6709\u4eba";
        string actionText = request != null && request.Kind == PlayerDialogueRequestKind.Follow
            ? "\u60f3\u8ddf\u7740\u4f60"
            : "\u60f3\u548c\u4f60\u804a\u804a";
        label.text = $"{npcName}{actionText}";
    }

    private void UpdateProgress()
    {
        float progress = 0f;
        if (request != null && request.TimeoutSeconds > 0f)
        {
            progress = Mathf.Clamp01(request.RemainingSeconds / request.TimeoutSeconds);
        }

        if (progressFill != null)
        {
            progressFill.fillAmount = progress;
        }

        if (progressSlider != null)
        {
            progressSlider.normalizedValue = progress;
        }
    }
}
