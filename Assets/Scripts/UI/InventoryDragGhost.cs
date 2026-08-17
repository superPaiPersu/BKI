using CityStateSim.Items;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CityStateSim.UI
{
    public sealed class InventoryDragGhost : MonoBehaviour
    {
        [SerializeField] private RectTransform rootRect;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Image background;
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text countText;

        private Canvas rootCanvas;
        private RectTransform canvasRect;
        private bool built;
        private float fixedSize = 52f;

        public void EnsureBuilt(Canvas canvas, float size)
        {
            if (canvas == null)
            {
                return;
            }

            rootCanvas = canvas;
            canvasRect = rootCanvas.transform as RectTransform;
            fixedSize = Mathf.Max(32f, size);

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
            rootRect.sizeDelta = new Vector2(fixedSize, fixedSize);

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

            background.sprite = null;
            background.color = Color.clear;
            background.enabled = false;
            background.raycastTarget = false;

            if (icon == null)
            {
                GameObject iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                iconObject.transform.SetParent(transform, false);
                RectTransform iconRect = iconObject.GetComponent<RectTransform>();
                iconRect.anchorMin = new Vector2(0.5f, 0.5f);
                iconRect.anchorMax = new Vector2(0.5f, 0.5f);
                iconRect.pivot = new Vector2(0.5f, 0.5f);
                iconRect.anchoredPosition = Vector2.zero;
                iconRect.sizeDelta = new Vector2(fixedSize - 10f, fixedSize - 10f);
                icon = iconObject.GetComponent<Image>();
                icon.raycastTarget = false;
            }

            if (countText == null)
            {
                GameObject countObject = new GameObject("Count", typeof(RectTransform), typeof(TextMeshProUGUI));
                countObject.transform.SetParent(transform, false);
                RectTransform countRect = countObject.GetComponent<RectTransform>();
                countRect.anchorMin = new Vector2(1f, 0f);
                countRect.anchorMax = new Vector2(1f, 0f);
                countRect.pivot = new Vector2(1f, 0f);
                countRect.anchoredPosition = new Vector2(-3f, 2f);
                countRect.sizeDelta = new Vector2(32f, 20f);
                countText = countObject.GetComponent<TextMeshProUGUI>();
                countText.fontSize = 16f;
                countText.alignment = TextAlignmentOptions.BottomRight;
                countText.raycastTarget = false;
                countText.color = Color.white;
            }

            built = true;
            Hide();
        }

        public void Show(ItemDefinition item, int amount, Vector2 screenPosition)
        {
            if (!built || item == null)
            {
                return;
            }

            gameObject.SetActive(true);

            if (icon != null)
            {
                icon.sprite = item.Icon;
                icon.enabled = icon.sprite != null;
            }

            if (countText != null)
            {
                countText.text = amount > 1 ? amount.ToString() : string.Empty;
            }

            canvasGroup.alpha = 1f;
            SetScreenPosition(screenPosition);
        }

        public void SetScreenPosition(Vector2 screenPosition)
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

            Vector2 half = new Vector2(rootRect.rect.width * 0.5f, rootRect.rect.height * 0.5f);
            Vector2 canvasHalf = canvasRect.rect.size * 0.5f;
            float x = Mathf.Clamp(localPoint.x, -canvasHalf.x + half.x, canvasHalf.x - half.x);
            float y = Mathf.Clamp(localPoint.y, -canvasHalf.y + half.y, canvasHalf.y - half.y);
            rootRect.anchoredPosition = new Vector2(x, y);
        }

        public void Hide()
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
            }

            gameObject.SetActive(false);
        }
    }
}
