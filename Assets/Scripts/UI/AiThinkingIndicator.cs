using UnityEngine;
using UnityEngine.UI;

public sealed class AiThinkingIndicator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Image targetImage;
    [SerializeField] private CanvasGroup visibilityGroup;
    [SerializeField] private GameObject visualRoot;

    [Header("Animation")]
    [SerializeField] private Sprite[] frames;
    [SerializeField, Min(0.01f)] private float frameDuration = 0.12f;
    [SerializeField] private bool useUnscaledTime = true;
    [SerializeField] private bool resetToFirstFrameWhenHidden = true;

    [Header("Position")]
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 1.9f, 0f);
    [SerializeField] private bool useScreenSpacePosition = true;

    [Header("Debug")]
    [SerializeField] private bool logMissingReferences;

    private Transform target;
    private Camera targetCamera;
    private RectTransform rectTransform;
    private RectTransform parentRect;
    private Canvas parentCanvas;
    private int frameIndex;
    private float nextFrameAt;
    private bool isInUse;
    private bool warnedMissingImage;

    public bool IsInUse => isInUse;

    private float Now => useUnscaledTime ? Time.unscaledTime : Time.time;

    private void Awake()
    {
        CacheRectReferences();
        if (targetImage == null)
        {
            targetImage = GetComponentInChildren<Image>(true);
        }

        if (visibilityGroup == null)
        {
            visibilityGroup = GetComponent<CanvasGroup>();
        }
    }

    private void Update()
    {
        if (!isInUse)
        {
            return;
        }

        RefreshPosition();
        AdvanceFrameIfNeeded();
    }

    public void Bind(Transform newTarget, Vector3 offset, Camera camera, bool screenSpacePosition)
    {
        gameObject.SetActive(true);
        CacheRectReferences();
        target = newTarget;
        worldOffset = offset;
        targetCamera = camera != null ? camera : Camera.main;
        useScreenSpacePosition = screenSpacePosition;
        isInUse = target != null;
        frameIndex = 0;
        nextFrameAt = Now;
        ApplyCurrentFrame();
        SetVisible(isInUse);
        RefreshPosition();
    }

    public void Release()
    {
        target = null;
        isInUse = false;

        if (resetToFirstFrameWhenHidden)
        {
            frameIndex = 0;
            ApplyCurrentFrame();
        }

        SetVisible(false);
        gameObject.SetActive(false);
    }

    public void SetFrames(Sprite[] newFrames)
    {
        frames = newFrames;
        frameIndex = 0;
        ApplyCurrentFrame();
    }

    private void AdvanceFrameIfNeeded()
    {
        if (frames == null || frames.Length == 0 || Now < nextFrameAt)
        {
            return;
        }

        frameIndex = (frameIndex + 1) % frames.Length;
        ApplyCurrentFrame();
        nextFrameAt = Now + frameDuration;
    }

    private void RefreshPosition()
    {
        if (target == null)
        {
            Release();
            return;
        }

        Vector3 worldPosition = target.position + worldOffset;
        if (!useScreenSpacePosition || rectTransform == null)
        {
            transform.position = worldPosition;
            return;
        }

        Camera camera = targetCamera != null ? targetCamera : Camera.main;
        if (camera == null)
        {
            return;
        }

        if (!UiScreenPositionUtility.TryWorldToScreenPoint(camera, worldPosition, out Vector2 screenPoint))
        {
            SetVisible(false);
            return;
        }

        screenPoint = UiScreenPositionUtility.ClampToCameraPixelRect(camera, screenPoint);
        SetVisible(true);
        if (parentRect == null)
        {
            rectTransform.position = new Vector3(screenPoint.x, screenPoint.y, rectTransform.position.z);
            return;
        }

        if (UiScreenPositionUtility.TryScreenPointToLocalPoint(parentRect, parentCanvas, screenPoint, out Vector2 localPoint))
        {
            rectTransform.anchoredPosition = localPoint;
        }
    }

    private void CacheRectReferences()
    {
        rectTransform = transform as RectTransform;
        parentRect = transform.parent as RectTransform;
        parentCanvas = GetComponentInParent<Canvas>();
    }

    private void SetVisible(bool visible)
    {
        if (visibilityGroup != null)
        {
            visibilityGroup.alpha = visible ? 1f : 0f;
            visibilityGroup.interactable = false;
            visibilityGroup.blocksRaycasts = false;
        }

        if (visualRoot != null)
        {
            visualRoot.SetActive(visible);
        }
    }

    private void ApplyCurrentFrame()
    {
        if (targetImage == null)
        {
            if (logMissingReferences && !warnedMissingImage)
            {
                warnedMissingImage = true;
                Debug.LogWarning("[AiThinkingIndicator] targetImage is missing. Assign the Image that should display the thinking frames.", this);
            }

            return;
        }

        if (frames == null || frames.Length == 0)
        {
            return;
        }

        frameIndex = Mathf.Clamp(frameIndex, 0, frames.Length - 1);
        targetImage.sprite = frames[frameIndex];
        targetImage.enabled = frames[frameIndex] != null;
    }
}
