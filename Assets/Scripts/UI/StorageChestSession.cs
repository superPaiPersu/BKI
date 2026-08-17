using CityStateSim.Items;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CityStateSim.UI
{
    public sealed class StorageChestSession : MonoBehaviour
    {
        private static int flyPanelToggleConsumedFrame = -1;
        private static KeyCode flyPanelToggleConsumedKey = KeyCode.None;

        [Header("Panels")]
        [SerializeField] private InventoryPanel playerInventoryPanel;
        [SerializeField] private InventoryPanel chestInventoryPanel;
        [SerializeField] private FlyPanel playerFlyPanel;
        [SerializeField] private FlyPanel chestFlyPanel;
        [SerializeField] private FlyPanel flyPanel;

        [Header("Storage Actions")]
        [SerializeField] private GameObject storageActionsRoot;
        [SerializeField] private CanvasGroup storageActionsGroup;
        [SerializeField] private Button takeAllButton;
        [SerializeField] private Button putAllButton;
        [SerializeField] private Button stackMatchingButton;
        [SerializeField] private Button organizeChestButton;
        [SerializeField] private TMP_Text statusText;

        [Header("Debug")]
        [SerializeField] private string lastOpenResult;
        [SerializeField] private float lastOpenTime;
        [SerializeField] private UnityEngine.Object lastOpenChest;
        [SerializeField] private UnityEngine.Object lastOpenInteractor;

        private StorageChest activeChest;
        private InventorySystem playerInventory;
        private GameObject interactor;
        private bool buttonsBound;
        private InventorySystem subscribedPlayerInventory;
        private InventorySystem subscribedChestInventory;

        public bool IsOpen => activeChest != null && playerInventory != null;
        public StorageChest ActiveChest => activeChest;
        public InventorySystem PlayerInventory => playerInventory;
        public InventorySystem ChestInventory => activeChest != null ? activeChest.Inventory : null;

        public static bool TryConsumeFlyPanelToggle(FlyPanel sourcePanel, KeyCode key)
        {
            if (sourcePanel == null || key == KeyCode.None || !Input.GetKeyDown(key))
            {
                return false;
            }

            if (flyPanelToggleConsumedFrame == Time.frameCount && flyPanelToggleConsumedKey == key)
            {
                return true;
            }

            StorageChestSession[] sessions = FindObjectsByType<StorageChestSession>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < sessions.Length; i++)
            {
                StorageChestSession session = sessions[i];
                if (session == null || !session.IsOpen || !session.ControlsFlyPanel(sourcePanel))
                {
                    continue;
                }

                flyPanelToggleConsumedFrame = Time.frameCount;
                flyPanelToggleConsumedKey = key;
                session.Close();
                return true;
            }

            return false;
        }

        private void Awake()
        {
            if (playerInventoryPanel == null)
            {
                playerInventoryPanel = FindFirstObjectByType<InventoryPanel>();
            }

            ResolveFlyPanels();

            BindButtons();
            SetActionsVisible(false);
        }

        private void OnEnable()
        {
            ResolveFlyPanels();
            BindButtons();
        }

        private void Update()
        {
            if (!IsOpen)
            {
                return;
            }

            if (interactor == null
                || global::DayOverCheck.IsUserInputLocked
                || !activeChest.CanInteract(interactor))
            {
                Close();
            }
        }

        private void OnDisable()
        {
            UnbindButtons();
        }

        public bool Open(StorageChest chest, GameObject newInteractor)
        {
            lastOpenTime = Time.unscaledTime;
            lastOpenChest = chest;
            lastOpenInteractor = newInteractor;

            if (chest == null)
            {
                lastOpenResult = "failed: chest is null";
                return false;
            }

            if (chest.Inventory == null)
            {
                lastOpenResult = "failed: chest inventory is null";
                return false;
            }

            if (newInteractor == null)
            {
                lastOpenResult = "failed: interactor is null";
                return false;
            }

            if (!chest.CanInteract(newInteractor))
            {
                lastOpenResult = "failed: chest.CanInteract returned false";
                return false;
            }

            if (playerInventoryPanel != null && ReferenceEquals(playerInventoryPanel, chestInventoryPanel))
            {
                lastOpenResult = "failed: player and chest panels are the same";
                Debug.LogWarning("[StorageChestSession] Player and chest panels must be different InventoryPanel instances.", this);
                return false;
            }

            InventorySystem newPlayerInventory = newInteractor.GetComponentInParent<InventorySystem>();
            if (newPlayerInventory == null)
            {
                newPlayerInventory = newInteractor.GetComponentInChildren<InventorySystem>(true);
            }
            if (newPlayerInventory == null)
            {
                lastOpenResult = "failed: interactor has no InventorySystem";
                Debug.LogWarning("[StorageChestSession] The interactor has no InventorySystem.", this);
                return false;
            }

            if (IsOpen)
            {
                Close();
            }

            activeChest = chest;
            playerInventory = newPlayerInventory;
            interactor = newInteractor;
            SubscribeInventories();

            ResolveFlyPanels();
            playerInventoryPanel?.SetPanelTitle("Inventory");
            playerInventoryPanel?.SetInventorySystem(playerInventory);
            playerInventoryPanel?.Open();

            chestInventoryPanel?.SetPanelTitle(chest.DisplayName);
            chestInventoryPanel?.SetInventorySystem(chest.Inventory);
            chestInventoryPanel?.Open();
            ShowFlyPanels();

            BindButtons();
            SetActionsVisible(true);
            RefreshActionState();
            SetStatus(string.Empty);
            lastOpenResult = "success: opened";
            return true;
        }

        public void Close()
        {
            if (!IsOpen && activeChest == null)
            {
                SetActionsVisible(false);
                return;
            }

            playerInventoryPanel?.Close();
            chestInventoryPanel?.Close();
            UnsubscribeInventories();
            activeChest = null;
            playerInventory = null;
            interactor = null;
            ResolveFlyPanels();
            SetActionsVisible(false);
            HideFlyPanels();
            SetStatus(string.Empty);
        }

        public void TakeAllFromChest()
        {
            if (!IsOpen)
            {
                return;
            }

            int moved = InventoryTransferService.TransferAll(activeChest.Inventory, playerInventory);
            SetStatus(moved > 0 ? $"Taken x{moved}." : "No chest items fit in the inventory.");
            RefreshActionState();
        }

        public void PutAllToChest()
        {
            if (!IsOpen)
            {
                return;
            }

            int moved = InventoryTransferService.TransferAll(playerInventory, activeChest.Inventory);
            SetStatus(moved > 0 ? $"Stored x{moved}." : "No items could be stored.");
            RefreshActionState();
        }

        public void StackMatchingItemsToChest()
        {
            if (!IsOpen)
            {
                return;
            }

            int moved = InventoryTransferService.TransferMatchingItemTypes(playerInventory, activeChest.Inventory);
            SetStatus(moved > 0 ? $"Stacked matching items x{moved}." : "No matching item types could be stacked.");
            RefreshActionState();
        }

        public void OrganizeChest()
        {
            if (!IsOpen)
            {
                return;
            }

            bool changed = activeChest.Inventory.TryOrganize();
            SetStatus(changed ? "Chest organized." : "Nothing to organize.");
            RefreshActionState();
        }

        public bool TryTransferDraggedStack(
            InventoryPanel sourcePanel,
            int sourceSlotIndex,
            int amount,
            Vector2 screenPosition)
        {
            if (!IsOpen || sourcePanel == null)
            {
                return false;
            }

            InventoryPanel destinationPanel;
            if (ReferenceEquals(sourcePanel, playerInventoryPanel))
            {
                destinationPanel = chestInventoryPanel;
            }
            else if (ReferenceEquals(sourcePanel, chestInventoryPanel))
            {
                destinationPanel = playerInventoryPanel;
            }
            else
            {
                return false;
            }

            if (destinationPanel == null
                || !destinationPanel.TryGetSlotIndexAtScreenPosition(screenPosition, out int destinationSlotIndex))
            {
                return false;
            }

            InventorySystem source = sourcePanel.BoundInventory;
            InventorySystem destination = destinationPanel.BoundInventory;
            if (source == null || destination == null || !source.TryGetStackAt(sourceSlotIndex, out InventoryStack stack))
            {
                return false;
            }

            int requested = amount > 0 ? amount : stack.Amount;
            bool moved = InventoryTransferService.TryTransferOrSwapBetweenSlots(
                source,
                sourceSlotIndex,
                destination,
                destinationSlotIndex,
                requested,
                out int movedAmount,
                out bool swapped);
            if (moved)
            {
                SetStatus(swapped ? "Swapped stacks." : $"Moved x{movedAmount}.");
                playerInventoryPanel?.Refresh();
                chestInventoryPanel?.Refresh();
                RefreshActionState();
            }

            return moved;
        }

        private void BindButtons()
        {
            if (buttonsBound)
            {
                return;
            }

            takeAllButton?.onClick.AddListener(TakeAllFromChest);
            putAllButton?.onClick.AddListener(PutAllToChest);
            stackMatchingButton?.onClick.AddListener(StackMatchingItemsToChest);
            organizeChestButton?.onClick.AddListener(OrganizeChest);
            buttonsBound = true;
        }

        private void UnbindButtons()
        {
            if (!buttonsBound)
            {
                return;
            }

            takeAllButton?.onClick.RemoveListener(TakeAllFromChest);
            putAllButton?.onClick.RemoveListener(PutAllToChest);
            stackMatchingButton?.onClick.RemoveListener(StackMatchingItemsToChest);
            organizeChestButton?.onClick.RemoveListener(OrganizeChest);
            buttonsBound = false;
        }

        private void SubscribeInventories()
        {
            UnsubscribeInventories();

            subscribedPlayerInventory = playerInventory;
            subscribedChestInventory = activeChest != null ? activeChest.Inventory : null;

            if (subscribedPlayerInventory != null)
            {
                subscribedPlayerInventory.InventoryChanged += HandleSessionInventoryChanged;
            }

            if (subscribedChestInventory != null && !ReferenceEquals(subscribedChestInventory, subscribedPlayerInventory))
            {
                subscribedChestInventory.InventoryChanged += HandleSessionInventoryChanged;
            }
        }

        private void UnsubscribeInventories()
        {
            if (subscribedPlayerInventory != null)
            {
                subscribedPlayerInventory.InventoryChanged -= HandleSessionInventoryChanged;
            }

            if (subscribedChestInventory != null && !ReferenceEquals(subscribedChestInventory, subscribedPlayerInventory))
            {
                subscribedChestInventory.InventoryChanged -= HandleSessionInventoryChanged;
            }

            subscribedPlayerInventory = null;
            subscribedChestInventory = null;
        }

        private void HandleSessionInventoryChanged()
        {
            if (!IsOpen)
            {
                return;
            }

            playerInventoryPanel?.Refresh();
            chestInventoryPanel?.Refresh();
            RefreshActionState();
        }

        private void SetActionsVisible(bool visible)
        {
            if (storageActionsGroup != null)
            {
                storageActionsGroup.alpha = visible ? 1f : 0f;
                storageActionsGroup.interactable = visible;
                storageActionsGroup.blocksRaycasts = visible;
            }

            if (storageActionsRoot != null && storageActionsRoot != gameObject)
            {
                storageActionsRoot.SetActive(visible);
            }
        }

        private void RefreshActionState()
        {
            if (!IsOpen)
            {
                return;
            }

            if (takeAllButton != null)
            {
                takeAllButton.interactable = activeChest.Inventory.Stacks.Count > 0;
            }

            if (putAllButton != null)
            {
                putAllButton.interactable = playerInventory.Stacks.Count > 0;
            }

            if (stackMatchingButton != null)
            {
                stackMatchingButton.interactable = InventoryTransferService.HasMatchingItemTypes(
                    playerInventory,
                    activeChest.Inventory);
            }
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message ?? string.Empty;
            }
        }

        private void ResolveFlyPanels()
        {
            if (playerFlyPanel == null && playerInventoryPanel != null)
            {
                playerFlyPanel = playerInventoryPanel.GetComponentInParent<FlyPanel>(true);
            }

            if (chestFlyPanel == null && chestInventoryPanel != null)
            {
                chestFlyPanel = chestInventoryPanel.GetComponentInParent<FlyPanel>(true);
            }

            if (flyPanel != null)
            {
                playerFlyPanel ??= flyPanel;
                chestFlyPanel ??= flyPanel;
            }
        }

        private void ShowFlyPanels()
        {
            ResolveFlyPanels();

            if (playerFlyPanel != null)
            {
                playerFlyPanel.Show();
            }

            if (chestFlyPanel != null && !ReferenceEquals(chestFlyPanel, playerFlyPanel))
            {
                chestFlyPanel.Show();
            }
        }

        private void HideFlyPanels()
        {
            ResolveFlyPanels();

            if (playerFlyPanel != null)
            {
                playerFlyPanel.Hide();
            }

            if (chestFlyPanel != null && !ReferenceEquals(chestFlyPanel, playerFlyPanel))
            {
                chestFlyPanel.Hide();
            }
        }

        private bool ControlsFlyPanel(FlyPanel panel)
        {
            if (panel == null)
            {
                return false;
            }

            ResolveFlyPanels();
            return ReferenceEquals(panel, playerFlyPanel)
                || ReferenceEquals(panel, chestFlyPanel)
                || ReferenceEquals(panel, flyPanel);
        }
    }
}
