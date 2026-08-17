using TMPro;
using System.Collections;
using UnityEngine;

public class MessageBox : MonoBehaviour
{
    public TMP_Text tmp;
    public float charactersPerSecond = 24f;
    public bool useUnscaledTime = false;
    public bool playTypewriterOnSetText = true;

    Transform followTarget;
    Camera targetCamera;
    Vector3 worldOffset;
    bool useScreenSpacePosition;
    RectTransform rectTransform;
    RectTransform parentRectTransform;
    Canvas parentCanvas;
    Coroutine typewriterCoroutine;
    string fullText = string.Empty;

    public bool IsInUse { get; private set; }

    void Awake()
    {
        rectTransform = transform as RectTransform;
        parentRectTransform = transform.parent as RectTransform;
        parentCanvas = GetComponentInParent<Canvas>();

        if (tmp == null)
        {
            tmp = GetComponentInChildren<TMP_Text>();
        }
    }

    public void SetText(string text)
    {
        fullText = text ?? string.Empty;

        if (typewriterCoroutine != null)
        {
            StopCoroutine(typewriterCoroutine);
            typewriterCoroutine = null;
        }

        if (tmp == null)
        {
            return;
        }

        if (!playTypewriterOnSetText || charactersPerSecond <= 0f)
        {
            tmp.text = fullText;
            return;
        }

        typewriterCoroutine = StartCoroutine(PlayTypewriter());
    }

    public void CompleteTypewriter()
    {
        if (typewriterCoroutine != null)
        {
            StopCoroutine(typewriterCoroutine);
            typewriterCoroutine = null;
        }

        if (tmp != null)
        {
            tmp.text = fullText;
        }
    }

    public void Bind(Transform target, Vector3 offset, Camera cameraForScreenSpace, bool screenSpacePosition)
    {
        IsInUse = true;
        followTarget = target;
        worldOffset = offset;
        targetCamera = cameraForScreenSpace;
        useScreenSpacePosition = screenSpacePosition;
        parentRectTransform = transform.parent as RectTransform;
        parentCanvas = GetComponentInParent<Canvas>();
        gameObject.SetActive(true);
        RefreshPosition();
    }

    public void Release()
    {
        CompleteTypewriter();
        IsInUse = false;
        followTarget = null;
        gameObject.SetActive(false);
    }

    IEnumerator PlayTypewriter()
    {
        tmp.text = string.Empty;
        if (string.IsNullOrEmpty(fullText))
        {
            typewriterCoroutine = null;
            yield break;
        }

        float secondsPerCharacter = 1f / charactersPerSecond;
        for (int i = 1; i <= fullText.Length; i++)
        {
            tmp.text = fullText.Substring(0, i);

            float elapsed = 0f;
            while (elapsed < secondsPerCharacter)
            {
                elapsed += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                yield return null;
            }
        }

        typewriterCoroutine = null;
    }

    void Update()
    {
        RefreshPosition();
    }

    void RefreshPosition()
    {
        if (followTarget == null)
        {
            return;
        }

        Vector3 worldPosition = followTarget.position + worldOffset;
        if (useScreenSpacePosition && targetCamera != null)
        {
            if (!UiScreenPositionUtility.TryWorldToScreenPoint(targetCamera, worldPosition, out Vector2 screenPoint))
            {
                return;
            }

            if (rectTransform != null && parentRectTransform != null)
            {
                if (UiScreenPositionUtility.TryScreenPointToLocalPoint(parentRectTransform, parentCanvas, screenPoint, out Vector2 localPoint))
                {
                    rectTransform.anchoredPosition = localPoint;
                }
            }
            else
            {
                transform.position = new Vector3(screenPoint.x, screenPoint.y, transform.position.z);
            }
        }
        else
        {
            transform.position = worldPosition;
        }
    }
}

public static class UiScreenPositionUtility
{
    private const float EdgePadding = 1f;

    public static bool TryGetMouseScreenPosition(Camera camera, out Vector2 screenPosition)
    {
        screenPosition = Input.mousePosition;
        if (camera == null)
        {
            return true;
        }

        return camera.pixelRect.Contains(screenPosition);
    }

    public static Camera GetCanvasCamera(Canvas canvas)
    {
        if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            return null;
        }

        return canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
    }

    public static bool TryWorldToScreenPoint(Camera camera, Vector3 worldPosition, out Vector2 screenPoint)
    {
        screenPoint = Vector2.zero;
        if (camera == null)
        {
            return false;
        }

        Rect pixelRect = camera.pixelRect;
        if (camera.orthographic)
        {
            Vector3 offset = worldPosition - camera.transform.position;
            float height = camera.orthographicSize * 2f;
            float width = height * camera.aspect;
            if (height <= 0f || width <= 0f || pixelRect.width <= 0f || pixelRect.height <= 0f)
            {
                return false;
            }

            float viewportX = Vector3.Dot(offset, camera.transform.right) / width + 0.5f;
            float viewportY = Vector3.Dot(offset, camera.transform.up) / height + 0.5f;
            screenPoint = new Vector2(
                pixelRect.xMin + viewportX * pixelRect.width,
                pixelRect.yMin + viewportY * pixelRect.height);
            return true;
        }

        Vector3 localPoint = camera.transform.InverseTransformPoint(worldPosition);
        if (localPoint.z <= camera.nearClipPlane)
        {
            return false;
        }

        Vector3 projected = camera.WorldToScreenPoint(worldPosition);
        screenPoint = new Vector2(projected.x, projected.y);
        return true;
    }

    public static bool TryScreenPointToWorldOnPlane(
        Camera camera,
        Vector2 screenPosition,
        float planeZ,
        out Vector3 worldPosition)
    {
        worldPosition = Vector3.zero;
        if (camera == null || !IsFinite(screenPosition.x) || !IsFinite(screenPosition.y))
        {
            return false;
        }

        Rect pixelRect = camera.pixelRect;
        if (pixelRect.width <= 0f || pixelRect.height <= 0f || !pixelRect.Contains(screenPosition))
        {
            return false;
        }

        if (camera.orthographic)
        {
            float height = camera.orthographicSize * 2f;
            float width = height * camera.aspect;
            if (height <= 0f || width <= 0f || pixelRect.width <= 0f || pixelRect.height <= 0f)
            {
                return false;
            }

            float centeredX = (screenPosition.x - pixelRect.xMin) / pixelRect.width - 0.5f;
            float centeredY = (screenPosition.y - pixelRect.yMin) / pixelRect.height - 0.5f;
            Vector3 rayOrigin =
                camera.transform.position
                + camera.transform.right * (centeredX * width)
                + camera.transform.up * (centeredY * height);
            Vector3 rayDirection = camera.transform.forward;
            if (Mathf.Abs(rayDirection.z) < 0.0001f)
            {
                return false;
            }

            float distance = (planeZ - rayOrigin.z) / rayDirection.z;
            worldPosition = rayOrigin + rayDirection * distance;
            return true;
        }

        // Normalize against the active camera rect before creating the ray. This
        // remains valid when the game window or camera viewport changes mid-frame.
        Vector2 viewportPoint = new Vector2(
            (screenPosition.x - pixelRect.xMin) / pixelRect.width,
            (screenPosition.y - pixelRect.yMin) / pixelRect.height);
        viewportPoint.x = Mathf.Clamp01(viewportPoint.x);
        viewportPoint.y = Mathf.Clamp01(viewportPoint.y);

        Ray ray = camera.ViewportPointToRay(new Vector3(viewportPoint.x, viewportPoint.y, 0f));
        Plane worldPlane = new Plane(Vector3.forward, new Vector3(0f, 0f, planeZ));
        if (!worldPlane.Raycast(ray, out float enter))
        {
            return false;
        }

        worldPosition = ray.GetPoint(enter);
        return true;
    }

    public static bool TryScreenPointToLocalPoint(
        RectTransform parentRect,
        Canvas parentCanvas,
        Vector2 screenPoint,
        out Vector2 localPoint)
    {
        localPoint = Vector2.zero;
        if (parentRect == null)
        {
            return false;
        }

        Camera uiCamera = GetCanvasCamera(parentCanvas);
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPoint, uiCamera, out localPoint);
    }

    public static Vector2 ClampToCameraPixelRect(Camera camera, Vector2 point)
    {
        if (camera == null)
        {
            return point;
        }

        return ClampToPixelRect(camera.pixelRect, point);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public static Vector2 ClampToPixelRect(Rect rect, Vector2 point)
    {
        if (rect.width <= EdgePadding * 2f || rect.height <= EdgePadding * 2f)
        {
            return point;
        }

        point.x = Mathf.Clamp(point.x, rect.xMin + EdgePadding, rect.xMax - EdgePadding);
        point.y = Mathf.Clamp(point.y, rect.yMin + EdgePadding, rect.yMax - EdgePadding);
        return point;
    }
}
