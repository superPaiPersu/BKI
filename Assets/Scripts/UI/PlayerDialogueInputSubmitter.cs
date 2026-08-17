using CityStateSim.Dialogue;
using TMPro;
using UnityEngine;

public sealed class PlayerDialogueInputSubmitter : MonoBehaviour
{
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private DialogueController dialogueController;
    [SerializeField] private bool clearAfterSubmit = true;
    [SerializeField] private bool refocusAfterSubmit = true;

    private void Awake()
    {
        if (inputField == null)
        {
            inputField = GetComponent<TMP_InputField>();
        }

        if (dialogueController == null)
        {
            dialogueController = FindFirstObjectByType<DialogueController>();
        }
    }

    private void OnEnable()
    {
        if (inputField != null)
        {
            inputField.onSubmit.AddListener(SubmitText);
        }
    }

    private void OnDisable()
    {
        if (inputField != null)
        {
            inputField.onSubmit.RemoveListener(SubmitText);
        }
    }

    public void SubmitCurrentText()
    {
        if (inputField == null)
        {
            return;
        }

        SubmitText(inputField.text);
    }

    public void SubmitText(string text)
    {
        if (global::DayOverCheck.IsUserInputLocked
            || dialogueController == null
            || string.IsNullOrWhiteSpace(text)
            || dialogueController.IsWaitingForConversationReply)
        {
            return;
        }

        dialogueController.SubmitPlayerLine(text.Trim());

        if (clearAfterSubmit && inputField != null)
        {
            inputField.SetTextWithoutNotify(string.Empty);
        }

        if (refocusAfterSubmit && inputField != null && inputField.gameObject.activeInHierarchy)
        {
            inputField.ActivateInputField();
        }
    }
}
