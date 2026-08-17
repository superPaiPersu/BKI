using CityStateSim.Items;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CityStateSim.UI
{
    public sealed class InventoryHoverTooltip : MonoBehaviour
    {
        [SerializeField] private RectTransform rootRect;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Image background;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text metaText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private TMP_Text footerText;
        [SerializeField] private TMP_FontAsset fontAsset;
        [SerializeField, Min(120f)] private float fixedWidth = 280f;

        private Canvas rootCanvas;
        private RectTransform canvasRect;
        private bool built;

        public void EnsureBuilt(Canvas canvas)
        {
            if (canvas == null)
            {
                return;
            }

            if (built && canvas == rootCanvas)
            {
                return;
            }

            rootCanvas = canvas;
            canvasRect = rootCanvas.transform as RectTransform;

            if (rootRect == null)
            {
                rootRect = transform as RectTransform;
            }

            if (rootRect == null)
            {
                rootRect = gameObject.AddComponent<RectTransform>();
            }

            rootRect.SetParent(rootCanvas.transform, false);
            rootRect.anchorMin = new Vector2(0.5f, 0.5f);
            rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0f, 1f);

            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
            }

            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            if (background == null)
            {
                background = GetComponent<Image>();
            }

            if (background == null)
            {
                background = gameObject.AddComponent<Image>();
            }

            background.color = new Color(0.11f, 0.11f, 0.11f, 0.95f);
            background.raycastTarget = false;

            VerticalLayoutGroup layoutGroup = GetComponent<VerticalLayoutGroup>();
            if (layoutGroup == null)
            {
                layoutGroup = gameObject.AddComponent<VerticalLayoutGroup>();
            }

            layoutGroup.childAlignment = TextAnchor.UpperLeft;
            layoutGroup.childControlWidth = true;
            layoutGroup.childControlHeight = true;
            layoutGroup.childForceExpandWidth = true;
            layoutGroup.childForceExpandHeight = false;
            layoutGroup.spacing = 4f;
            layoutGroup.padding = new RectOffset(12, 12, 10, 10);

            ContentSizeFitter fitter = GetComponent<ContentSizeFitter>();
            if (fitter == null)
            {
                fitter = gameObject.AddComponent<ContentSizeFitter>();
            }

            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            if (titleText == null)
            {
                titleText = CreateTextChild("Title", 18f, true, new Color(1f, 0.92f, 0.72f, 1f));
            }

            if (metaText == null)
            {
                metaText = CreateTextChild("Meta", 12f, false, new Color(1f, 1f, 1f, 0.8f));
            }

            if (descriptionText == null)
            {
                descriptionText = CreateTextChild("Description", 13f, false, Color.white);
            }

            if (footerText == null)
            {
                footerText = CreateTextChild("Footer", 11f, false, new Color(1f, 1f, 1f, 0.65f));
            }

            built = true;
            SetFixedWidth(fixedWidth);
        }

        public void SetFont(TMP_FontAsset font)
        {
            if (font == null)
            {
                return;
            }

            fontAsset = font;
            ApplyFont(titleText);
            ApplyFont(metaText);
            ApplyFont(descriptionText);
            ApplyFont(footerText);
        }

        public void SetFixedWidth(float width)
        {
            fixedWidth = Mathf.Max(120f, width);
            if (rootRect != null)
            {
                Vector2 sizeDelta = rootRect.sizeDelta;
                sizeDelta.x = fixedWidth;
                rootRect.sizeDelta = sizeDelta;
            }

            ApplyTextWidth(titleText);
            ApplyTextWidth(metaText);
            ApplyTextWidth(descriptionText);
            ApplyTextWidth(footerText);
        }

        public void Show(ItemDefinition item, int amount, Vector2 screenPosition, Vector2 offset, float clampPadding)
        {
            if (item == null)
            {
                Hide();
                return;
            }

            SetText(titleText, item.DisplayName);
            SetText(metaText, BuildMetaText(item, amount));
            SetText(descriptionText, string.IsNullOrWhiteSpace(item.Description) ? "暂无说明。" : item.Description);
            SetText(footerText, item.QuestItem
                ? "任务物品"
                : $"售价 {item.BaseSellPrice}  堆叠上限 {item.MaxStack}");

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }

            Canvas.ForceUpdateCanvases();
            if (rootRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rootRect);
            }

            SetScreenPosition(screenPosition, offset, clampPadding);
        }

        public void SetScreenPosition(Vector2 screenPosition, Vector2 offset, float clampPadding)
        {
            if (rootCanvas == null || rootRect == null || canvasRect == null)
            {
                return;
            }

            global::UnityEngine.Camera uiCamera = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, uiCamera, out Vector2 localPoint))
            {
                return;
            }

            Vector2 position = localPoint + offset;
            Vector2 size = rootRect.rect.size;
            Vector2 canvasSize = canvasRect.rect.size;
            Vector2 halfCanvas = canvasSize * 0.5f;

            float minX = -halfCanvas.x + clampPadding + size.x * rootRect.pivot.x;
            float maxX = halfCanvas.x - clampPadding - size.x * (1f - rootRect.pivot.x);
            float minY = -halfCanvas.y + clampPadding + size.y * rootRect.pivot.y;
            float maxY = halfCanvas.y - clampPadding - size.y * (1f - rootRect.pivot.y);

            position.x = Mathf.Clamp(position.x, minX, maxX);
            position.y = Mathf.Clamp(position.y, minY, maxY);
            rootRect.anchoredPosition = position;
        }

        public void Hide()
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
            }
        }

        private TMP_Text CreateTextChild(string childName, float fontSize, bool bold, Color color)
        {
            GameObject child = new GameObject(childName, typeof(RectTransform), typeof(TextMeshProUGUI));
            child.transform.SetParent(transform, false);

            RectTransform rect = child.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(fixedWidth - 24f, 0f);

            TextMeshProUGUI text = child.GetComponent<TextMeshProUGUI>();
            ApplyFont(text);

            text.fontSize = fontSize;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.color = color;
            text.alignment = TextAlignmentOptions.TopLeft;
            if (bold)
            {
                text.fontStyle = FontStyles.Bold;
            }

            LayoutElement layoutElement = child.AddComponent<LayoutElement>();
            layoutElement.minWidth = fixedWidth - 24f;
            layoutElement.preferredWidth = fixedWidth - 24f;
            layoutElement.flexibleWidth = 1f;
            layoutElement.preferredHeight = -1f;
            return text;
        }

        private void ApplyFont(TMP_Text text)
        {
            if (text == null)
            {
                return;
            }

            TMP_FontAsset selectedFont = fontAsset != null
                ? fontAsset
                : TMP_Settings.defaultFontAsset;
            if (selectedFont != null)
            {
                text.font = selectedFont;
            }
        }

        private void ApplyTextWidth(TMP_Text text)
        {
            if (text == null)
            {
                return;
            }

            float width = Mathf.Max(120f, fixedWidth - 24f);
            RectTransform rect = text.rectTransform;
            if (rect != null)
            {
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            }

            LayoutElement layoutElement = text.GetComponent<LayoutElement>();
            if (layoutElement != null)
            {
                layoutElement.minWidth = width;
                layoutElement.preferredWidth = width;
            }
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null)
            {
                text.text = value ?? string.Empty;
            }
        }

        private static string BuildMetaText(ItemDefinition item, int amount)
        {
            string category = item.Category.ToString();
            int count = Mathf.Max(1, amount);
            return item.MaxStack > 1 ? $"{category}  x{count}" : category;
        }
    }
}
