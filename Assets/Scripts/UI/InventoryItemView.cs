using CityStateSim.Items;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CityStateSim.UI
{
    public sealed class InventoryItemView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Header("References")]
        [SerializeField] private Button button;
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text countText;
        [SerializeField] private TMP_Text categoryText;

        private InventoryPanel owner;
        private InventoryPanel.InventoryDisplayEntry entry;
        private Image cachedImage;
        private int slotIndex = -1;

        public ItemDefinition Item => entry != null ? entry.Item : null;
        public InventoryPanel.InventoryDisplayEntry Entry => entry;
        public int SlotIndex => slotIndex;

        public void SetSlotIndex(int value)
        {
            slotIndex = value;
        }

        private void Awake()
        {
            CacheAndCreateRuntimeReferences();
        }

        private void OnDisable()
        {
            if (owner != null)
            {
                owner.NotifyItemPointerExit(this);
                owner.NotifyItemDragCancelledIfNeeded(this);
                owner.NotifyItemQuantitySelectionCancelledIfNeeded(this);
            }
        }

        public void Bind(InventoryPanel panel, InventoryPanel.InventoryDisplayEntry displayEntry)
        {
            owner = panel;
            entry = displayEntry;
            CacheAndCreateRuntimeReferences();
            if (entry == null || entry.Item == null)
            {
                ClearView();
                return;
            }

            gameObject.SetActive(true);
            Refresh();
        }

        public void ClearBinding()
        {
            entry = null;
            ClearView();
        }

        public void SetGridSlotLayout(Vector2 slotSize, float iconPadding, bool hideText, bool hideBackground)
        {
            RectTransform root = transform as RectTransform;
            if (root != null)
            {
                root.sizeDelta = slotSize;
            }

            Image image = GetPrimaryImage();
            if (image != null)
            {
                image.enabled = true;
                image.raycastTarget = true;
            }

            LayoutElement layoutElement = GetComponent<LayoutElement>();
            if (layoutElement != null)
            {
                layoutElement.ignoreLayout = true;
            }

            ConfigureIcon(slotSize, iconPadding);
            ConfigureCountText(slotSize);
            SetTextActive(nameText, !hideText);
            SetTextActive(categoryText, !hideText);
            if (button != null && button.targetGraphic == null)
            {
                button.targetGraphic = image;
            }
        }

        private void Refresh()
        {
            if (entry == null || entry.Item == null)
            {
                ClearView();
                return;
            }

            gameObject.SetActive(true);
            ItemDefinition item = entry.Item;
            if (icon != null)
            {
                icon.sprite = item.Icon;
                icon.enabled = icon.sprite != null;
            }

            if (nameText != null)
            {
                nameText.text = item.DisplayName;
            }

            if (countText != null)
            {
                countText.text = entry.Amount > 1 ? entry.Amount.ToString() : string.Empty;
            }

            if (categoryText != null)
            {
                categoryText.text = $"{item.Category}";
            }

            if (button != null)
            {
                button.interactable = true;
            }

        }

        private void ClearView()
        {
            if (icon != null)
            {
                icon.sprite = null;
                icon.enabled = false;
            }

            if (nameText != null)
            {
                nameText.text = string.Empty;
            }

            if (countText != null)
            {
                countText.text = string.Empty;
            }

            if (categoryText != null)
            {
                categoryText.text = string.Empty;
            }

            if (button != null)
            {
                button.interactable = false;
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (owner != null)
            {
                owner.NotifyItemPointerEnter(this, eventData != null ? eventData.position : Input.mousePosition);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (owner != null)
            {
                owner.NotifyItemPointerExit(this);
            }
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            if (owner != null)
            {
                owner.NotifyItemPointerMove(this, eventData != null ? eventData.position : Input.mousePosition);
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (owner == null || entry == null || entry.Item == null)
            {
                return;
            }

            if (eventData != null && eventData.button == PointerEventData.InputButton.Right)
            {
                owner.TryToggleHoverTooltipsFromItem();
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (owner == null || entry == null || entry.Item == null)
            {
                return;
            }

            if (eventData == null || eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            owner.TryBeginCtrlQuantitySelection(this, eventData.position);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (owner == null)
            {
                return;
            }

            if (eventData == null || eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            owner.EndCtrlQuantitySelectionHold(this);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (owner == null || entry == null || entry.Item == null)
            {
                return;
            }

            if (eventData != null && eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            owner.BeginItemDrag(this, eventData != null ? eventData.position : (Vector2)Input.mousePosition);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (owner != null)
            {
                owner.UpdateItemDrag(eventData != null ? eventData.position : (Vector2)Input.mousePosition);
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (owner != null)
            {
                owner.EndItemDrag(eventData != null ? eventData.position : (Vector2)Input.mousePosition);
            }
        }

        private void CacheAndCreateRuntimeReferences()
        {
            cachedImage = GetComponent<Image>();

            if (icon == null)
            {
                icon = cachedImage;
            }

            if (button == null)
            {
                button = GetComponent<Button>();
            }

            if (button == null)
            {
                button = gameObject.AddComponent<Button>();
            }

            button.transition = Selectable.Transition.None;
            button.targetGraphic = GetPrimaryImage();
            button.interactable = entry != null && entry.Item != null;

            if (countText == null)
            {
                countText = GetComponentInChildren<TMP_Text>(true);
            }

            if (countText == null)
            {
                countText = CreateRuntimeCountText();
            }

        }

        private Image GetPrimaryImage()
        {
            if (icon != null)
            {
                return icon;
            }

            if (cachedImage == null)
            {
                cachedImage = GetComponent<Image>();
            }

            return cachedImage;
        }

        private TMP_Text CreateRuntimeCountText()
        {
            GameObject countRoot = new GameObject("Count", typeof(RectTransform), typeof(TextMeshProUGUI));
            countRoot.transform.SetParent(transform, false);

            RectTransform rect = countRoot.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-4f, 3f);
            rect.sizeDelta = new Vector2(48f, 20f);

            TextMeshProUGUI text = countRoot.GetComponent<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.BottomRight;
            text.fontSize = 18f;
            text.enableAutoSizing = true;
            text.fontSizeMin = 10f;
            text.fontSizeMax = 18f;
            text.raycastTarget = false;
            text.color = Color.white;
            return text;
        }

        private void ConfigureIcon(Vector2 slotSize, float iconPadding)
        {
            Image primaryImage = GetPrimaryImage();
            RectTransform iconRect = primaryImage != null ? primaryImage.rectTransform : null;
            if (iconRect == null)
            {
                return;
            }

            float padding = Mathf.Max(0f, iconPadding);
            float size = Mathf.Max(1f, Mathf.Min(slotSize.x, slotSize.y) - padding * 2f);
            iconRect.anchorMin = new Vector2(0.5f, 0.5f);
            iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.anchoredPosition = Vector2.zero;
            iconRect.sizeDelta = new Vector2(size, size);
        }

        private void ConfigureCountText(Vector2 slotSize)
        {
            RectTransform countRect = countText != null ? countText.rectTransform : null;
            if (countRect == null)
            {
                return;
            }

            countRect.anchorMin = new Vector2(1f, 0f);
            countRect.anchorMax = new Vector2(1f, 0f);
            countRect.pivot = new Vector2(1f, 0f);
            countRect.anchoredPosition = new Vector2(-4f, 3f);
            countRect.sizeDelta = new Vector2(Mathf.Max(24f, slotSize.x), 20f);
            countText.alignment = TextAlignmentOptions.BottomRight;
            countText.fontSize = Mathf.Min(countText.fontSize, 18f);
            countText.enableAutoSizing = true;
            countText.fontSizeMin = 10f;
            countText.fontSizeMax = 18f;
        }

        private static void SetTextActive(TMP_Text text, bool active)
        {
            if (text != null)
            {
                text.gameObject.SetActive(active);
            }
        }
    }
}
