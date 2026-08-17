using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace CityStateSim.UI
{
    [DefaultExecutionOrder(-10000)]
    public sealed class UiNavigationInputBlocker : MonoBehaviour
    {
        [Header("Policy")]
        [SerializeField] private bool clearInputSystemMoveAction = true;
        [SerializeField] private bool disableEventSystemNavigationEvents;
        [SerializeField] private bool clearCurrentSelectionOnApply = true;
        [SerializeField] private bool keepApplyingForFirstFrames = true;
        [SerializeField, Min(1)] private int firstFrameApplyCount = 5;

        [Header("Debug")]
        [SerializeField] private bool logApplied;

        private static UiNavigationInputBlocker autoInstance;
        private int remainingFrameApplies;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallAutomatically()
        {
            if (autoInstance != null)
            {
                return;
            }

            GameObject blockerObject = new GameObject("[UI Navigation Input Blocker]");
            DontDestroyOnLoad(blockerObject);
            autoInstance = blockerObject.AddComponent<UiNavigationInputBlocker>();
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ApplyToAllEventSystems(true, false, true, false);
        }

        private void Awake()
        {
            if (autoInstance == null)
            {
                autoInstance = this;
            }
        }

        private void OnEnable()
        {
            remainingFrameApplies = keepApplyingForFirstFrames ? Mathf.Max(1, firstFrameApplyCount) : 1;
            ApplyConfigured();
        }

        private void Update()
        {
            if (remainingFrameApplies <= 0)
            {
                return;
            }

            ApplyConfigured();
            remainingFrameApplies--;
        }

        public void ApplyConfigured()
        {
            ApplyToAllEventSystems(
                clearInputSystemMoveAction,
                disableEventSystemNavigationEvents,
                clearCurrentSelectionOnApply,
                logApplied);
        }

        public static void ApplyToAllEventSystems(
            bool clearMoveAction,
            bool disableNavigationEvents,
            bool clearSelection,
            bool log)
        {
            EventSystem[] eventSystems = FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
            for (int i = 0; i < eventSystems.Length; i++)
            {
                ApplyToEventSystem(eventSystems[i], clearMoveAction, disableNavigationEvents, clearSelection, log);
            }
        }

        private static void ApplyToEventSystem(
            EventSystem eventSystem,
            bool clearMoveAction,
            bool disableNavigationEvents,
            bool clearSelection,
            bool log)
        {
            if (eventSystem == null)
            {
                return;
            }

            if (disableNavigationEvents)
            {
                eventSystem.sendNavigationEvents = false;
            }

#if ENABLE_INPUT_SYSTEM
            if (clearMoveAction)
            {
                InputSystemUIInputModule inputModule = eventSystem.GetComponent<InputSystemUIInputModule>();
                if (inputModule != null && inputModule.move != null)
                {
                    inputModule.move.action?.Disable();
                    inputModule.move = null;
                    if (log)
                    {
                        Debug.Log($"[UI Navigation] Cleared Move action on {eventSystem.name}.", eventSystem);
                    }
                }
            }
#endif

            if (clearSelection && eventSystem.currentSelectedGameObject != null)
            {
                eventSystem.SetSelectedGameObject(null);
            }
        }
    }
}
