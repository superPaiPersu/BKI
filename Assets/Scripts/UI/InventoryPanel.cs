using System;
using System.Collections.Generic;
using CityStateSim.Economy;
using CityStateSim.Items;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CityStateSim.UI
{
    public sealed class InventoryPanel : MonoBehaviour
    {
        [Serializable]
        public sealed class InventoryDisplayEntry
        {
            public InventoryDisplayEntry(ItemDefinition item, int amount, int stackCount, int slotIndex)
            {
                Item = item;
                Amount = amount;
                StackCount = stackCount;
                SlotIndex = slotIndex;
            }

            public ItemDefinition Item { get; }
            public int Amount { get; }
            public int StackCount { get; }
            public int SlotIndex { get; }
            public int SellValuePerItem => Item != null ? Mathf.Max(0, Item.BaseSellPrice) : 0;
            public int TotalSellValue => SellValuePerItem * Amount;
        }

        [Header("References")]
        [SerializeField] private InventorySystem inventorySystem;
        [SerializeField] private PlayerEconomySystem economySystem;
        [SerializeField] private Transform itemContainer;
        [SerializeField] private InventoryItemView itemPrefab;

        [Header("Fixed Slot Grid")]
        [SerializeField] private bool useFixedSlotGrid = true;
        [SerializeField, Min(1)] private int slotColumns = 9;
        [SerializeField, Min(1)] private int slotRows = 4;
        [SerializeField] private Vector2 firstSlotCenter = new Vector2(32f, -32f);
        [SerializeField] private Vector2 slotStep = new Vector2(60f, -60f);
        [SerializeField] private Vector2 slotItemSize = new Vector2(52f, 52f);
        [SerializeField, Min(0f)] private float iconPadding = 6f;
        [SerializeField] private bool hideItemBackgroundInFixedSlots = true;
        [SerializeField] private bool hideItemTextInFixedSlots = true;
        [SerializeField] private bool disableContainerLayoutInFixedSlots = true;
        [SerializeField, Min(0f)] private float dropSnapMaxDistance = 28f;
        [SerializeField] private List<RectTransform> slotAnchors = new List<RectTransform>();

        [Header("Visibility")]
        [SerializeField] private CanvasGroup visibilityGroup;
        [SerializeField] private GameObject visualRoot;
        [SerializeField] private bool hideWhenEmpty;
        [SerializeField] private bool startOpen = true;

        [Header("Header")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text capacityText;
        [SerializeField] private TMP_Text moneyText;
        [SerializeField] private TMP_Text statusText;

        [Header("Hover Tooltip")]
        [SerializeField] private bool hoverTooltipsEnabled = true;
        [SerializeField, Min(0f)] private float hoverTooltipShowDelay = 0.18f;
        [SerializeField, Min(0f)] private float hoverTooltipHideDelay = 0.08f;
        [SerializeField] private Vector2 hoverTooltipOffset = new Vector2(18f, -18f);
        [SerializeField, Min(0f)] private float hoverTooltipClampPadding = 12f;
        [SerializeField, Min(120f)] private float hoverTooltipWidth = 280f;
        [SerializeField] private bool rightClickTogglesHoverTooltips = true;
        [SerializeField] private TMP_FontAsset hoverTooltipFont;
        [SerializeField, Min(0f)] private float dragGhostWidth = 52f;

        [Header("Quantity Selection")]
        [SerializeField, Min(0f)] private float ctrlQuantityRepeatInitialDelay = 0.35f;
        [SerializeField, Min(0.01f)] private float ctrlQuantityRepeatStartInterval = 0.16f;
        [SerializeField, Min(0.01f)] private float ctrlQuantityRepeatMinInterval = 0.035f;
        [SerializeField, Range(0.1f, 0.99f)] private float ctrlQuantityRepeatAcceleration = 0.82f;

        [Header("Test Items")]
        [SerializeField] private bool showTestItemButton = true;
        [SerializeField] private string testItemButtonLabel = "测试物品";
        [SerializeField] private Vector2 testItemButtonSize = new Vector2(120f, 32f);
        [SerializeField] private Vector2 testItemButtonOffset = new Vector2(-16f, -16f);
        [SerializeField] private List<ItemAmount> testItemsToGrant = new List<ItemAmount>();

        private readonly List<InventoryItemView> spawnedItems = new List<InventoryItemView>();
        private readonly List<InventoryItemView> sceneSlotViews = new List<InventoryItemView>();
        private readonly List<InventoryDisplayEntry> entries = new List<InventoryDisplayEntry>();
        private bool subscribedInventory;
        private bool subscribedEconomy;
        private bool isOpen;
        private bool usingSceneSlotViews;
        private bool spawnedItemsAreGenerated;
        private InventoryHoverTooltip hoverTooltip;
        private Canvas hoverTooltipCanvas;
        private InventoryDragGhost dragGhost;
        private Canvas dragGhostCanvas;
        private Button testItemButton;
        private TMP_Text testItemButtonText;
        private InventoryItemView hoveredView;
        private InventoryDisplayEntry hoveredEntry;
        private Vector2 hoveredScreenPosition;
        private float hoverEnteredRealtime;
        private float hoverExitedRealtime;
        private bool hoverExitPending;
        private bool hoverTooltipVisible;
        private InventoryItemView draggingView;
        private InventoryDisplayEntry draggingEntry;
        private int draggingAmount;
        private bool dragDropHandled;
        private InventoryItemView quantitySelectionView;
        private InventoryDisplayEntry quantitySelectionEntry;
        private int quantitySelectionAmount;
        private bool quantitySelectionHoldActive;
        private float nextQuantitySelectionRepeatTime;
        private float quantitySelectionRepeatInterval;
        private bool warnedMissingItemPrefab;
        private bool warnedMissingItemContainer;
        private bool warnedGridTooSmall;
        private string runtimePanelTitle;

        public InventorySystem BoundInventory => inventorySystem;
        public bool IsOpen => isOpen;

        private void Awake()
        {
            ResolveReferences();
            PrepareFixedGridContainer();
            EnsureTestItemButton();
        }

        private void OnEnable()
        {
            ResolveReferences();
            PrepareFixedGridContainer();
            EnsureTestItemButton();
            Subscribe();
            Refresh();
            SetOpen(startOpen);
        }

        private void Start()
        {
            Refresh();
        }

        private void Update()
        {
            bool needsRefresh = false;
            bool needsHeaderRefresh = false;

            if (inventorySystem == null)
            {
                inventorySystem = FindFirstObjectByType<InventorySystem>();
                needsRefresh = inventorySystem != null;
            }

            if (economySystem == null)
            {
                economySystem = FindFirstObjectByType<PlayerEconomySystem>();
                needsHeaderRefresh = economySystem != null;
            }

            if (needsRefresh || needsHeaderRefresh)
            {
                Subscribe();
                if (needsRefresh)
                {
                    Refresh();
                }
                else
                {
                    UpdateHeader();
                }
            }

            UpdateHoverTooltip();
            UpdateItemDragVisual();
            UpdateCtrlQuantitySelection();
            UpdateTestItemButtonState();
        }

        private void OnDisable()
        {
            Unsubscribe();
            HideHoverTooltip(true);
            CancelItemDrag(true);
            ClearCtrlQuantitySelection(true);
        }

        public void Open()
        {
            SetOpen(true);
        }

        public void Close()
        {
            SetOpen(false);
        }

        public void Toggle()
        {
            SetOpen(!isOpen);
        }

        public void Refresh()
        {
            RebuildSlots();
            UpdateHeader();
            RefreshVisibility();
            UpdateTestItemButtonState();
        }

        public void SetInventorySystem(InventorySystem value)
        {
            if (ReferenceEquals(inventorySystem, value))
            {
                Subscribe();
                Refresh();
                return;
            }

            if (subscribedInventory && inventorySystem != null)
            {
                inventorySystem.InventoryChanged -= HandleInventoryChanged;
                inventorySystem.InventoryOperationFailed -= HandleInventoryOperationFailed;
                subscribedInventory = false;
            }

            inventorySystem = value;
            Subscribe();
            Refresh();
        }

        public void SetPanelTitle(string title)
        {
            runtimePanelTitle = title;
            UpdateHeader();
        }

        public bool ContainsScreenPoint(Vector2 screenPosition)
        {
            RectTransform targetRect = visualRoot != null
                ? visualRoot.transform as RectTransform
                : transform as RectTransform;
            if (targetRect == null)
            {
                targetRect = itemContainer as RectTransform;
            }

            if (targetRect == null)
            {
                return false;
            }

            Canvas canvas = targetRect.GetComponentInParent<Canvas>(true);
            global::UnityEngine.Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            return RectTransformUtility.RectangleContainsScreenPoint(targetRect, screenPosition, uiCamera);
        }

        public bool TryGetSlotIndexAtScreenPosition(Vector2 screenPosition, out int slotIndex)
        {
            return TryResolveDropTargetSlot(screenPosition, out slotIndex);
        }

        public void ToggleHoverTooltips()
        {
            SetHoverTooltipsEnabled(!hoverTooltipsEnabled);
        }

        public bool GrantTestItems()
        {
            if (inventorySystem == null)
            {
                if (statusText != null)
                {
                    statusText.text = "没有找到背包系统。";
                }

                return false;
            }

            if (testItemsToGrant == null || testItemsToGrant.Count == 0)
            {
                if (statusText != null)
                {
                    statusText.text = "测试物品列表为空。";
                }

                return false;
            }

            bool success = inventorySystem.TryAddAll(testItemsToGrant);
            if (statusText != null)
            {
                statusText.text = success ? "已添加测试物品。" : "添加测试物品失败。";
            }

            if (success)
            {
                Refresh();
            }

            return success;
        }

        public void OnTestItemButtonClicked()
        {
            GrantTestItems();
        }

        public void OrganizeInventory()
        {
            if (inventorySystem == null)
            {
                if (statusText != null)
                {
                    statusText.text = "没有找到背包系统。";
                }

                return;
            }

            bool success = inventorySystem.TryOrganize();
            if (statusText != null)
            {
                statusText.text = success ? "背包已整理。" : "背包中没有可整理的物品。";
            }
        }

        public bool TryToggleHoverTooltipsFromItem()
        {
            if (!rightClickTogglesHoverTooltips)
            {
                return false;
            }

            ToggleHoverTooltips();
            return true;
        }

        public void SetHoverTooltipsEnabled(bool value)
        {
            if (hoverTooltipsEnabled == value)
            {
                return;
            }

            hoverTooltipsEnabled = value;
            if (!hoverTooltipsEnabled)
            {
                HideHoverTooltip(true);
            }
        }

        private void ResolveReferences()
        {
            if (inventorySystem == null)
            {
                inventorySystem = FindFirstObjectByType<InventorySystem>();
            }

            if (economySystem == null)
            {
                economySystem = FindFirstObjectByType<PlayerEconomySystem>();
            }

            if (itemContainer == null)
            {
                itemContainer = transform;
            }

            if (visibilityGroup == null)
            {
                visibilityGroup = GetComponent<CanvasGroup>();
            }
        }

        internal void NotifyItemPointerEnter(InventoryItemView view, Vector2 screenPosition)
        {
            if (!IsHoverTooltipEnabledForView(view))
            {
                return;
            }

            hoveredView = view;
            hoveredEntry = view != null ? view.Entry : null;
            hoveredScreenPosition = screenPosition;
            hoverEnteredRealtime = Time.unscaledTime;
            hoverExitPending = false;

            if (hoverTooltipVisible)
            {
                ShowHoverTooltipNow();
            }
        }

        internal void NotifyItemPointerExit(InventoryItemView view)
        {
            if (view == null || view != hoveredView)
            {
                return;
            }

            hoverExitPending = true;
            hoverExitedRealtime = Time.unscaledTime;
        }

        internal void NotifyItemPointerMove(InventoryItemView view, Vector2 screenPosition)
        {
            if (view == null || view != hoveredView)
            {
                return;
            }

            hoveredScreenPosition = screenPosition;
            if (hoverTooltipVisible)
            {
                UpdateHoverTooltipPosition();
            }
        }

        internal bool TryBeginCtrlQuantitySelection(InventoryItemView view, Vector2 screenPosition)
        {
            if (!IsCtrlHeld() || !CanStartItemDrag(view) || IsDraggingItem())
            {
                return false;
            }

            InventoryDisplayEntry entry = view.Entry;
            if (!IsSameCtrlQuantitySelection(view, entry))
            {
                quantitySelectionView = view;
                quantitySelectionEntry = entry;
                quantitySelectionAmount = 0;
            }

            HideHoverTooltip(true);
            quantitySelectionHoldActive = true;
            quantitySelectionRepeatInterval = Mathf.Max(ctrlQuantityRepeatMinInterval, ctrlQuantityRepeatStartInterval);
            nextQuantitySelectionRepeatTime = Time.unscaledTime + Mathf.Max(0f, ctrlQuantityRepeatInitialDelay);
            IncrementCtrlQuantitySelection(screenPosition);
            return true;
        }

        internal void EndCtrlQuantitySelectionHold(InventoryItemView view)
        {
            if (view == null || view != quantitySelectionView)
            {
                return;
            }

            quantitySelectionHoldActive = false;
        }

        internal void BeginItemDrag(InventoryItemView view, Vector2 screenPosition)
        {
            if (!CanStartItemDrag(view))
            {
                return;
            }

            HideHoverTooltip(true);
            CancelItemDrag(true);

            draggingView = view;
            draggingEntry = view.Entry;
            int stackAmount = draggingEntry != null ? draggingEntry.Amount : 0;
            int selectedAmount = ResolveCtrlQuantitySelectionForDrag(view, stackAmount);
            draggingAmount = selectedAmount > 0 ? selectedAmount : ResolveDraggedAmount(stackAmount);
            ClearCtrlQuantitySelection(false);
            dragDropHandled = false;

            EnsureDragGhost();
            if (dragGhost != null && draggingEntry != null && draggingEntry.Item != null)
            {
                dragGhost.Show(draggingEntry.Item, draggingAmount, screenPosition);
            }
        }

        internal void UpdateItemDrag(Vector2 screenPosition)
        {
            if (!IsDraggingItem())
            {
                return;
            }

            if (dragGhost != null)
            {
                dragGhost.SetScreenPosition(screenPosition);
            }
        }

        internal void EndItemDrag(Vector2 screenPosition)
        {
            if (!IsDraggingItem())
            {
                return;
            }

            if (!dragDropHandled)
            {
                if (TryDropDraggedItemAtScreenPosition(screenPosition))
                {
                    CancelItemDrag(true);
                    Refresh();
                    return;
                }

                CancelItemDrag(true);
            }
            else
            {
                CancelItemDrag(true);
            }
        }

        private bool TryDropDraggedItemAtScreenPosition(Vector2 screenPosition)
        {
            if (!IsDraggingItem() || draggingView == null || inventorySystem == null)
            {
                return false;
            }

            int sourceIndex = draggingView.SlotIndex;
            if (sourceIndex < 0)
            {
                return false;
            }

            StorageChestSession storageSession = FindFirstObjectByType<StorageChestSession>();
            if (storageSession != null
                && storageSession.TryTransferDraggedStack(this, sourceIndex, draggingAmount, screenPosition))
            {
                return true;
            }

            if (!TryResolveDropTargetSlot(screenPosition, out int targetIndex))
            {
                return false;
            }

            if (sourceIndex == targetIndex)
            {
                return true;
            }

            bool success = inventorySystem.TryDropStackOnSlot(sourceIndex, targetIndex, draggingAmount);
            if (success)
            {
                dragDropHandled = true;
            }

            return success;
        }

        private bool TryResolveDropTargetSlot(Vector2 screenPosition, out int targetIndex)
        {
            targetIndex = -1;
            RectTransform containerRect = itemContainer as RectTransform;
            if (containerRect == null)
            {
                return false;
            }

            Canvas canvas = itemContainer.GetComponentInParent<Canvas>(true);
            global::UnityEngine.Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            if (usingSceneSlotViews)
            {
                float bestArea = float.MaxValue;
                int nearestIndex = -1;
                float nearestDistance = float.MaxValue;
                int sceneSlotCount = inventorySystem != null
                    ? Mathf.Min(sceneSlotViews.Count, inventorySystem.MaxSlots)
                    : sceneSlotViews.Count;
                for (int i = 0; i < sceneSlotCount; i++)
                {
                    InventoryItemView view = sceneSlotViews[i];
                    RectTransform viewRect = view != null ? view.transform as RectTransform : null;
                    if (!TryGetScreenRect(viewRect, uiCamera, out Rect screenRect))
                    {
                        continue;
                    }

                    if (screenRect.Contains(screenPosition))
                    {
                        float area = screenRect.width * screenRect.height;
                        if (area < bestArea)
                        {
                            bestArea = area;
                            targetIndex = i;
                        }
                    }

                    float distance = GetDistanceToScreenRect(screenPosition, screenRect);
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearestIndex = i;
                    }
                }

                if (targetIndex >= 0)
                {
                    return true;
                }

                return TryAcceptNearestDropSlot(nearestIndex, nearestDistance, out targetIndex);
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(containerRect, screenPosition, uiCamera, out Vector2 localPoint))
            {
                return false;
            }

            int slotCount = useFixedSlotGrid
                ? GetVisibleSlotCount()
                : Mathf.Max(itemContainer.childCount, inventorySystem != null ? inventorySystem.MaxSlots : 0);
            if (inventorySystem != null)
            {
                slotCount = Mathf.Min(slotCount, inventorySystem.MaxSlots);
            }

            int nearestVirtualIndex = -1;
            float nearestVirtualDistance = float.MaxValue;
            for (int i = 0; i < slotCount; i++)
            {
                Vector2 slotCenter = GetSlotPosition(i);
                Vector2 halfSize = slotItemSize * 0.5f;
                Rect slotRect = new Rect(slotCenter - halfSize, slotItemSize);
                if (slotRect.Contains(localPoint))
                {
                    targetIndex = i;
                    return true;
                }

                if (!TryGetVirtualSlotScreenRect(containerRect, uiCamera, slotRect, out Rect screenRect))
                {
                    continue;
                }

                float distance = GetDistanceToScreenRect(screenPosition, screenRect);
                if (distance < nearestVirtualDistance)
                {
                    nearestVirtualDistance = distance;
                    nearestVirtualIndex = i;
                }
            }

            return TryAcceptNearestDropSlot(nearestVirtualIndex, nearestVirtualDistance, out targetIndex);
        }

        private bool TryAcceptNearestDropSlot(int nearestIndex, float nearestDistance, out int targetIndex)
        {
            targetIndex = -1;
            if (nearestIndex < 0 || nearestDistance > dropSnapMaxDistance)
            {
                return false;
            }

            targetIndex = nearestIndex;
            return true;
        }

        private static float GetDistanceToScreenRect(Vector2 screenPosition, Rect screenRect)
        {
            float dx = Mathf.Max(screenRect.xMin - screenPosition.x, 0f, screenPosition.x - screenRect.xMax);
            float dy = Mathf.Max(screenRect.yMin - screenPosition.y, 0f, screenPosition.y - screenRect.yMax);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private static bool TryGetVirtualSlotScreenRect(
            RectTransform containerRect,
            global::UnityEngine.Camera uiCamera,
            Rect localRect,
            out Rect screenRect)
        {
            screenRect = default;
            if (containerRect == null || localRect.width <= 0f || localRect.height <= 0f)
            {
                return false;
            }

            Vector3[] localCorners =
            {
                new Vector3(localRect.xMin, localRect.yMin, 0f),
                new Vector3(localRect.xMin, localRect.yMax, 0f),
                new Vector3(localRect.xMax, localRect.yMax, 0f),
                new Vector3(localRect.xMax, localRect.yMin, 0f)
            };

            Vector2 first = RectTransformUtility.WorldToScreenPoint(uiCamera, containerRect.TransformPoint(localCorners[0]));
            float minX = first.x;
            float maxX = first.x;
            float minY = first.y;
            float maxY = first.y;

            for (int i = 1; i < localCorners.Length; i++)
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(uiCamera, containerRect.TransformPoint(localCorners[i]));
                minX = Mathf.Min(minX, point.x);
                maxX = Mathf.Max(maxX, point.x);
                minY = Mathf.Min(minY, point.y);
                maxY = Mathf.Max(maxY, point.y);
            }

            screenRect = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return screenRect.width > 0f && screenRect.height > 0f;
        }

        private static bool TryGetScreenRect(RectTransform rectTransform, global::UnityEngine.Camera uiCamera, out Rect screenRect)
        {
            screenRect = default;
            if (rectTransform == null || rectTransform.rect.width <= 0f || rectTransform.rect.height <= 0f)
            {
                return false;
            }

            Vector3[] corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            Vector2 first = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[0]);
            float minX = first.x;
            float maxX = first.x;
            float minY = first.y;
            float maxY = first.y;

            for (int i = 1; i < corners.Length; i++)
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[i]);
                minX = Mathf.Min(minX, point.x);
                maxX = Mathf.Max(maxX, point.x);
                minY = Mathf.Min(minY, point.y);
                maxY = Mathf.Max(maxY, point.y);
            }

            screenRect = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return screenRect.width > 0f && screenRect.height > 0f;
        }

        internal void NotifyItemDragCancelledIfNeeded(InventoryItemView view)
        {
            if (view != null && view == draggingView)
            {
                CancelItemDrag(true);
            }
        }

        internal void NotifyItemQuantitySelectionCancelledIfNeeded(InventoryItemView view)
        {
            if (view != null && view == quantitySelectionView)
            {
                ClearCtrlQuantitySelection(true);
            }
        }

        private bool CanStartItemDrag(InventoryItemView view)
        {
            return view != null
                && view.Entry != null
                && view.Entry.Item != null
                && view.SlotIndex >= 0
                && inventorySystem != null;
        }

        private bool IsDraggingItem()
        {
            return draggingView != null && draggingEntry != null && draggingEntry.Item != null;
        }

        private int ResolveDraggedAmount(int stackAmount)
        {
            int amount = Mathf.Max(1, stackAmount);
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (ctrl)
            {
                return 1;
            }

            if (shift)
            {
                return Mathf.Max(1, Mathf.CeilToInt(amount / 2f));
            }

            return amount;
        }

        private int ResolveCtrlQuantitySelectionForDrag(InventoryItemView view, int stackAmount)
        {
            if (view == null || view != quantitySelectionView || quantitySelectionAmount <= 0)
            {
                return 0;
            }

            if (!TryGetCtrlQuantitySelectionEntry(out InventoryDisplayEntry entry) || entry != view.Entry)
            {
                return 0;
            }

            return Mathf.Clamp(quantitySelectionAmount, 1, Mathf.Max(1, stackAmount));
        }

        private void EnsureDragGhost()
        {
            if (dragGhost != null)
            {
                if (dragGhostCanvas == null)
                {
                    dragGhostCanvas = FindTooltipCanvas();
                }

                dragGhost.EnsureBuilt(dragGhostCanvas, dragGhostWidth);
                return;
            }

            dragGhostCanvas = FindTooltipCanvas();
            if (dragGhostCanvas == null)
            {
                return;
            }

            GameObject ghostObject = new GameObject("InventoryDragGhost", typeof(RectTransform), typeof(InventoryDragGhost));
            ghostObject.transform.SetParent(dragGhostCanvas.transform, false);
            ghostObject.transform.SetAsLastSibling();

            dragGhost = ghostObject.GetComponent<InventoryDragGhost>();
            if (dragGhost != null)
            {
                dragGhost.EnsureBuilt(dragGhostCanvas, dragGhostWidth);
            }
        }

        private void UpdateItemDragVisual()
        {
            if (!IsDraggingItem())
            {
                return;
            }

            if (dragGhost != null)
            {
                dragGhost.SetScreenPosition(Input.mousePosition);
            }
        }

        private void UpdateCtrlQuantitySelection()
        {
            if (!HasCtrlQuantitySelection())
            {
                return;
            }

            if (!TryGetCtrlQuantitySelectionEntry(out InventoryDisplayEntry entry))
            {
                ClearCtrlQuantitySelection(true);
                return;
            }

            quantitySelectionEntry = entry;
            quantitySelectionAmount = Mathf.Clamp(quantitySelectionAmount, 1, entry.Amount);

            if (IsDraggingItem())
            {
                quantitySelectionHoldActive = false;
                return;
            }

            if (dragGhost != null)
            {
                dragGhost.SetScreenPosition(Input.mousePosition);
            }

            if (!quantitySelectionHoldActive)
            {
                return;
            }

            if (!IsCtrlHeld() || !Input.GetMouseButton(0))
            {
                quantitySelectionHoldActive = false;
                return;
            }

            float now = Time.unscaledTime;
            if (now < nextQuantitySelectionRepeatTime)
            {
                return;
            }

            while (now >= nextQuantitySelectionRepeatTime && quantitySelectionAmount < entry.Amount)
            {
                IncrementCtrlQuantitySelection(Input.mousePosition);
                quantitySelectionRepeatInterval = Mathf.Max(
                    ctrlQuantityRepeatMinInterval,
                    quantitySelectionRepeatInterval * Mathf.Clamp(ctrlQuantityRepeatAcceleration, 0.1f, 0.99f));
                nextQuantitySelectionRepeatTime += quantitySelectionRepeatInterval;
            }

            if (quantitySelectionAmount >= entry.Amount)
            {
                quantitySelectionHoldActive = false;
            }
        }

        private bool IncrementCtrlQuantitySelection(Vector2 screenPosition)
        {
            if (!TryGetCtrlQuantitySelectionEntry(out InventoryDisplayEntry entry))
            {
                ClearCtrlQuantitySelection(true);
                return false;
            }

            quantitySelectionEntry = entry;
            quantitySelectionAmount = Mathf.Clamp(quantitySelectionAmount + 1, 1, entry.Amount);
            ShowCtrlQuantitySelection(entry, screenPosition);
            return true;
        }

        private void ShowCtrlQuantitySelection(InventoryDisplayEntry entry, Vector2 screenPosition)
        {
            if (entry == null || entry.Item == null)
            {
                return;
            }

            EnsureDragGhost();
            if (dragGhost != null)
            {
                dragGhost.Show(entry.Item, quantitySelectionAmount, screenPosition);
            }
        }

        private bool TryGetCtrlQuantitySelectionEntry(out InventoryDisplayEntry entry)
        {
            entry = null;
            if (quantitySelectionView == null)
            {
                return false;
            }

            entry = quantitySelectionView.Entry;
            if (entry == null || entry.Item == null || entry.Amount <= 0 || quantitySelectionView.SlotIndex < 0)
            {
                entry = null;
                return false;
            }

            if (quantitySelectionEntry != null
                && quantitySelectionEntry.Item != null
                && !ReferenceEquals(quantitySelectionEntry.Item, entry.Item))
            {
                entry = null;
                return false;
            }

            return true;
        }

        private bool IsSameCtrlQuantitySelection(InventoryItemView view, InventoryDisplayEntry entry)
        {
            return quantitySelectionView == view
                && quantitySelectionEntry != null
                && entry != null
                && quantitySelectionEntry.SlotIndex == entry.SlotIndex
                && ReferenceEquals(quantitySelectionEntry.Item, entry.Item);
        }

        private bool HasCtrlQuantitySelection()
        {
            return quantitySelectionView != null && quantitySelectionAmount > 0;
        }

        private static bool IsCtrlHeld()
        {
            return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        }

        private void ClearCtrlQuantitySelection(bool resetGhost)
        {
            quantitySelectionView = null;
            quantitySelectionEntry = null;
            quantitySelectionAmount = 0;
            quantitySelectionHoldActive = false;
            nextQuantitySelectionRepeatTime = 0f;
            quantitySelectionRepeatInterval = 0f;

            if (resetGhost && !IsDraggingItem() && dragGhost != null)
            {
                dragGhost.Hide();
            }
        }

        private void CancelItemDrag(bool resetGhost)
        {
            draggingView = null;
            draggingEntry = null;
            draggingAmount = 0;
            dragDropHandled = false;

            if (resetGhost && dragGhost != null)
            {
                dragGhost.Hide();
            }
        }

        private void Subscribe()
        {
            if (!subscribedInventory && inventorySystem != null)
            {
                inventorySystem.InventoryChanged += HandleInventoryChanged;
                inventorySystem.InventoryOperationFailed += HandleInventoryOperationFailed;
                subscribedInventory = true;
            }

            if (!subscribedEconomy && economySystem != null)
            {
                economySystem.MoneyAdded += HandleMoneyChanged;
                economySystem.MoneySpent += HandleMoneyChanged;
                subscribedEconomy = true;
            }
        }

        private void Unsubscribe()
        {
            if (subscribedInventory && inventorySystem != null)
            {
                inventorySystem.InventoryChanged -= HandleInventoryChanged;
                inventorySystem.InventoryOperationFailed -= HandleInventoryOperationFailed;
                subscribedInventory = false;
            }

            if (subscribedEconomy && economySystem != null)
            {
                economySystem.MoneyAdded -= HandleMoneyChanged;
                economySystem.MoneySpent -= HandleMoneyChanged;
                subscribedEconomy = false;
            }
        }

        private void HandleInventoryChanged()
        {
            Refresh();
        }

        private void HandleInventoryOperationFailed(string reason)
        {
            if (statusText != null)
            {
                statusText.text = string.IsNullOrWhiteSpace(reason) ? string.Empty : reason;
            }
        }

        private void HandleMoneyChanged(int value, string reason)
        {
            UpdateHeader();
            if (statusText != null && !string.IsNullOrWhiteSpace(reason))
            {
                statusText.text = reason;
            }
        }

        private void RebuildSlots()
        {
            HideHoverTooltip(true);
            entries.Clear();

            if (inventorySystem == null)
            {
                return;
            }

            IReadOnlyList<InventoryStack> stacks = inventorySystem.Stacks;
            int visibleSlotCount = GetVisibleSlotCount();

            if (TryCollectSceneSlotViews(sceneSlotViews))
            {
                if (!usingSceneSlotViews)
                {
                    ClearGeneratedItems();
                }

                usingSceneSlotViews = true;
                spawnedItems.Clear();
                spawnedItems.AddRange(sceneSlotViews);
                spawnedItemsAreGenerated = false;

                if (useFixedSlotGrid && !warnedGridTooSmall && stacks.Count > sceneSlotViews.Count)
                {
                    warnedGridTooSmall = true;
                    Debug.LogWarning($"[InventoryPanel] Fixed grid has {sceneSlotViews.Count} visible slots, but inventory has {stacks.Count} stacks.", this);
                }

                for (int i = 0; i < sceneSlotViews.Count; i++)
                {
                    InventoryItemView view = sceneSlotViews[i];
                    if (view == null)
                    {
                        continue;
                    }

                    view.SetSlotIndex(i);
                    InventoryStack stack = i < stacks.Count ? stacks[i] : null;
                    if (stack == null || stack.IsEmpty || stack.Item == null)
                    {
                        view.ClearBinding();
                        continue;
                    }

                    InventoryDisplayEntry displayEntry = new InventoryDisplayEntry(stack.Item, stack.Amount, 1, i);
                    entries.Add(displayEntry);
                    view.Bind(this, displayEntry);
                }
            }
            else
            {
                if (usingSceneSlotViews)
                {
                    spawnedItems.Clear();
                }

                usingSceneSlotViews = false;
                ClearGeneratedItems();

                if (useFixedSlotGrid && !warnedGridTooSmall && stacks.Count > visibleSlotCount)
                {
                    warnedGridTooSmall = true;
                    Debug.LogWarning($"[InventoryPanel] Fixed grid has {visibleSlotCount} visible slots, but inventory has {stacks.Count} stacks.", this);
                }

                for (int i = 0; i < stacks.Count && (!useFixedSlotGrid || i < visibleSlotCount); i++)
                {
                    InventoryStack stack = stacks[i];
                    if (stack == null || stack.IsEmpty || stack.Item == null)
                    {
                        continue;
                    }

                    InventoryDisplayEntry displayEntry = new InventoryDisplayEntry(stack.Item, stack.Amount, 1, i);
                    entries.Add(displayEntry);
                    CreateItemView(displayEntry);
                }
            }

        }

        private void CreateItemView(InventoryDisplayEntry entry)
        {
            if (entry == null || itemPrefab == null || itemContainer == null)
            {
                WarnMissingItemReferences();
                return;
            }

            InventoryItemView view = Instantiate(itemPrefab, itemContainer);
            if (useFixedSlotGrid)
            {
                PositionItemInFixedSlot(view, entry.SlotIndex);
                view.SetGridSlotLayout(slotItemSize, iconPadding, hideItemTextInFixedSlots, hideItemBackgroundInFixedSlots);
            }

            view.SetSlotIndex(entry.SlotIndex);
            view.Bind(this, entry);
            spawnedItems.Add(view);
            spawnedItemsAreGenerated = true;
            usingSceneSlotViews = false;
        }

        private bool TryCollectSceneSlotViews(List<InventoryItemView> output)
        {
            output.Clear();

            if (itemContainer == null || itemContainer.childCount <= 0)
            {
                return false;
            }

            bool foundAny = false;
            for (int i = 0; i < itemContainer.childCount; i++)
            {
                Transform slotRoot = itemContainer.GetChild(i);
                if (slotRoot == null)
                {
                    output.Add(null);
                    continue;
                }

                InventoryItemView view = slotRoot.GetComponentInChildren<InventoryItemView>(true);
                output.Add(view);
                if (view != null)
                {
                    foundAny = true;
                }
            }

            return foundAny;
        }

        private void PositionItemInFixedSlot(InventoryItemView view, int slotIndex)
        {
            RectTransform rect = view != null ? view.transform as RectTransform : null;
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = slotItemSize;
            rect.anchoredPosition = GetSlotPosition(slotIndex);
            rect.localScale = Vector3.one;
        }

        private Vector2 GetSlotPosition(int slotIndex)
        {
            if (slotAnchors != null && slotIndex >= 0 && slotIndex < slotAnchors.Count && slotAnchors[slotIndex] != null)
            {
                return slotAnchors[slotIndex].anchoredPosition;
            }

            int columns = Mathf.Max(1, slotColumns);
            int column = slotIndex % columns;
            int row = slotIndex / columns;
            return new Vector2(firstSlotCenter.x + slotStep.x * column, firstSlotCenter.y + slotStep.y * row);
        }

        private int GetVisibleSlotCount()
        {
            if (!useFixedSlotGrid)
            {
                return inventorySystem != null ? inventorySystem.MaxSlots : 0;
            }

            if (slotAnchors != null && slotAnchors.Count > 0)
            {
                return slotAnchors.Count;
            }

            return Mathf.Max(1, slotColumns) * Mathf.Max(1, slotRows);
        }

        private void WarnMissingItemReferences()
        {
            if (itemPrefab == null && !warnedMissingItemPrefab)
            {
                warnedMissingItemPrefab = true;
                Debug.LogWarning("[InventoryPanel] Item prefab is missing.", this);
            }

            if (itemContainer == null && !warnedMissingItemContainer)
            {
                warnedMissingItemContainer = true;
                Debug.LogWarning("[InventoryPanel] Item container is missing.", this);
            }
        }

        private void ClearItems()
        {
            ClearGeneratedItems();
        }

        private void ClearGeneratedItems()
        {
            if (!spawnedItemsAreGenerated)
            {
                spawnedItems.Clear();
                return;
            }

            for (int i = 0; i < spawnedItems.Count; i++)
            {
                InventoryItemView view = spawnedItems[i];
                if (view != null)
                {
                    Destroy(view.gameObject);
                }
            }

            spawnedItems.Clear();
            spawnedItemsAreGenerated = false;
        }

        private void UpdateHeader()
        {
            if (titleText != null && !string.IsNullOrWhiteSpace(runtimePanelTitle))
            {
                titleText.text = runtimePanelTitle;
            }

            if (inventorySystem != null && capacityText != null)
            {
                int usedSlots = CountUsedSlots(inventorySystem.Stacks);
                capacityText.text = $"容量 {usedSlots}/{inventorySystem.MaxSlots}";
            }

            if (economySystem != null && moneyText != null)
            {
                moneyText.text = $"金币 {economySystem.Money}";
            }
        }

        private void RefreshVisibility()
        {
            bool visible = isOpen && (!hideWhenEmpty || entries.Count > 0);
            if (visibilityGroup != null)
            {
                visibilityGroup.alpha = visible ? 1f : 0f;
                visibilityGroup.interactable = visible;
                visibilityGroup.blocksRaycasts = visible;
            }

            if (visualRoot != null && visualRoot != gameObject)
            {
                visualRoot.SetActive(visible);
            }
        }

        private void EnsureTestItemButton()
        {
            if (!showTestItemButton)
            {
                if (testItemButton != null)
                {
                    testItemButton.gameObject.SetActive(false);
                }

                return;
            }

            if (testItemButton != null)
            {
                return;
            }

            Transform parent = visualRoot != null ? visualRoot.transform : transform;
            if (parent == null)
            {
                return;
            }

            GameObject buttonObject = new GameObject("TestItemButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            buttonObject.transform.SetAsLastSibling();

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = testItemButtonSize;
            rect.anchoredPosition = testItemButtonOffset;

            Image background = buttonObject.GetComponent<Image>();
            // Do not depend on Unity's legacy UI/Skin/UISprite.psd built-in asset.
            // A 1x1 built-in white texture is enough for this runtime-created button.
            background.sprite = Sprite.Create(
                Texture2D.whiteTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                1f);
            background.type = Image.Type.Simple;
            background.color = new Color(0.18f, 0.28f, 0.42f, 0.92f);
            background.raycastTarget = true;

            testItemButton = buttonObject.GetComponent<Button>();
            testItemButton.transition = Selectable.Transition.ColorTint;
            testItemButton.targetGraphic = background;
            testItemButton.onClick.AddListener(OnTestItemButtonClicked);

            GameObject textObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(buttonObject.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 4f);
            textRect.offsetMax = new Vector2(-8f, -4f);

            testItemButtonText = textObject.GetComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null)
            {
                testItemButtonText.font = TMP_Settings.defaultFontAsset;
            }

            testItemButtonText.text = testItemButtonLabel;
            testItemButtonText.alignment = TextAlignmentOptions.Center;
            testItemButtonText.enableAutoSizing = true;
            testItemButtonText.fontSizeMin = 12f;
            testItemButtonText.fontSizeMax = 18f;
            testItemButtonText.color = Color.white;
            testItemButtonText.raycastTarget = false;

            UpdateTestItemButtonState();
        }

        private void UpdateTestItemButtonState()
        {
            if (testItemButtonText != null && !string.IsNullOrWhiteSpace(testItemButtonLabel))
            {
                testItemButtonText.text = testItemButtonLabel;
            }

            if (testItemButton != null)
            {
                bool canGrant = showTestItemButton && inventorySystem != null && HasValidTestItems();
                testItemButton.gameObject.SetActive(showTestItemButton);
                testItemButton.interactable = canGrant;
            }
        }

        private bool HasValidTestItems()
        {
            if (testItemsToGrant == null)
            {
                return false;
            }

            for (int i = 0; i < testItemsToGrant.Count; i++)
            {
                ItemAmount itemAmount = testItemsToGrant[i];
                if (itemAmount != null && itemAmount.IsValid)
                {
                    return true;
                }
            }

            return false;
        }

        private void SetOpen(bool value)
        {
            isOpen = value;
            RefreshVisibility();
        }

        private void PrepareFixedGridContainer()
        {
            if (!useFixedSlotGrid || !disableContainerLayoutInFixedSlots || itemContainer == null || TryCollectSceneSlotViews(sceneSlotViews))
            {
                return;
            }

            LayoutGroup[] layoutGroups = itemContainer.GetComponents<LayoutGroup>();
            for (int i = 0; i < layoutGroups.Length; i++)
            {
                if (layoutGroups[i] != null)
                {
                    layoutGroups[i].enabled = false;
                }
            }

            ContentSizeFitter contentSizeFitter = itemContainer.GetComponent<ContentSizeFitter>();
            if (contentSizeFitter != null)
            {
                contentSizeFitter.enabled = false;
            }

            RectTransform contentTransform = itemContainer as RectTransform;
            ScrollRect[] scrollRects = itemContainer.GetComponentsInParent<ScrollRect>(true);
            for (int i = 0; i < scrollRects.Length; i++)
            {
                ScrollRect scrollRect = scrollRects[i];
                if (scrollRect != null && scrollRect.content == contentTransform)
                {
                    scrollRect.enabled = false;
                }
            }
        }

        private void UpdateHoverTooltip()
        {
            if (IsDraggingItem())
            {
                if (hoverTooltipVisible)
                {
                    HideHoverTooltip(true);
                }

                return;
            }

            if (!hoverTooltipsEnabled)
            {
                if (hoverTooltipVisible)
                {
                    HideHoverTooltip(true);
                }

                return;
            }

            if (hoveredView == null || hoveredEntry == null || hoveredEntry.Item == null)
            {
                if (hoverTooltipVisible)
                {
                    HideHoverTooltip(true);
                }

                return;
            }

            if (hoverExitPending)
            {
                if (Time.unscaledTime - hoverExitedRealtime >= hoverTooltipHideDelay)
                {
                    HideHoverTooltip(true);
                }
                else if (hoverTooltipVisible)
                {
                    UpdateHoverTooltipPosition();
                }

                return;
            }

            if (!hoverTooltipVisible)
            {
                if (Time.unscaledTime - hoverEnteredRealtime >= hoverTooltipShowDelay)
                {
                    ShowHoverTooltipNow();
                }

                return;
            }

            UpdateHoverTooltipPosition();
        }

        private void ShowHoverTooltipNow()
        {
            if (!IsHoverTooltipEnabledForView(hoveredView))
            {
                return;
            }

            EnsureHoverTooltip();
            if (hoverTooltip == null || hoveredEntry == null || hoveredEntry.Item == null)
            {
                return;
            }

            hoverTooltip.Show(
                hoveredEntry.Item,
                hoveredEntry.Amount,
                hoveredScreenPosition,
                hoverTooltipOffset,
                hoverTooltipClampPadding);

            hoverTooltipVisible = true;
            UpdateHoverTooltipPosition();
        }

        private void HideHoverTooltip(bool clearHoverState)
        {
            if (hoverTooltip != null)
            {
                hoverTooltip.Hide();
            }

            hoverTooltipVisible = false;
            hoverExitPending = false;

            if (clearHoverState)
            {
                hoveredView = null;
                hoveredEntry = null;
            }
        }

        private void UpdateHoverTooltipPosition()
        {
            if (hoverTooltip == null || !hoverTooltipVisible)
            {
                return;
            }

            hoverTooltip.SetScreenPosition(hoveredScreenPosition, hoverTooltipOffset, hoverTooltipClampPadding);
        }

        private void EnsureHoverTooltip()
        {
            if (hoverTooltip != null)
            {
                if (hoverTooltipCanvas == null)
                {
                    hoverTooltipCanvas = FindTooltipCanvas();
                }

                hoverTooltip.SetFont(hoverTooltipFont);
                hoverTooltip.EnsureBuilt(hoverTooltipCanvas);
                return;
            }

            hoverTooltipCanvas = FindTooltipCanvas();
            if (hoverTooltipCanvas == null)
            {
                return;
            }

            GameObject tooltipObject = new GameObject(
                "InventoryHoverTooltip",
                typeof(RectTransform),
                typeof(InventoryHoverTooltip));
            tooltipObject.transform.SetParent(hoverTooltipCanvas.transform, false);
            tooltipObject.transform.SetAsLastSibling();

            hoverTooltip = tooltipObject.GetComponent<InventoryHoverTooltip>();
            if (hoverTooltip != null)
            {
                hoverTooltip.SetFont(hoverTooltipFont);
                hoverTooltip.EnsureBuilt(hoverTooltipCanvas);
                hoverTooltip.SetFixedWidth(hoverTooltipWidth);
            }
        }

        private Canvas FindTooltipCanvas()
        {
            if (itemContainer != null)
            {
                Canvas canvasFromContainer = itemContainer.GetComponentInParent<Canvas>(true);
                if (canvasFromContainer != null)
                {
                    return canvasFromContainer;
                }
            }

            if (visibilityGroup != null)
            {
                Canvas canvasFromVisibility = visibilityGroup.GetComponentInParent<Canvas>(true);
                if (canvasFromVisibility != null)
                {
                    return canvasFromVisibility;
                }
            }

            return FindFirstObjectByType<Canvas>();
        }

        private bool IsHoverTooltipEnabledForView(InventoryItemView view)
        {
            return hoverTooltipsEnabled
                && !IsDraggingItem()
                && view != null
                && view.Entry != null
                && view.Entry.Item != null;
        }

        private static int CountUsedSlots(IReadOnlyList<InventoryStack> stacks)
        {
            if (stacks == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < stacks.Count; i++)
            {
                InventoryStack stack = stacks[i];
                if (stack != null && !stack.IsEmpty && stack.Item != null)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
