using System.Collections.Generic;
using CityStateSim.Interactions;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CityStateSim.UI
{
    public sealed class InteractableCursorController : MonoBehaviour
    {
        [Header("Detection")]
        [SerializeField] private global::UnityEngine.Camera worldCamera;
        [SerializeField] private Transform player;
        [SerializeField] private LayerMask interactableLayerMask = ~0;
        [SerializeField, Min(0f)] private float maxDistance = 2f;
        [SerializeField, Min(0f)] private float hideDelay = 0.15f;
        [SerializeField] private bool leftClickInteracts = true;
        [SerializeField] private bool ignoreClicksOverOtherUi = true;

        [Header("UI")]
        [SerializeField] private CanvasGroup visibilityGroup;
        [SerializeField] private GameObject visualRoot;
        [SerializeField] private RectTransform cursorRect;
        [SerializeField] private Vector2 screenOffset = new Vector2(18f, -18f);
        [SerializeField] private bool useDirectScreenPosition = true;
        [SerializeField] private bool clampToParentRect = true;

        private readonly List<RaycastResult> uiRaycastResults = new List<RaycastResult>();
        private IInteractable currentInteractable;
        private Canvas parentCanvas;
        private float hideAt = -1f;

        public IInteractable CurrentInteractable => currentInteractable;

        private void Awake()
        {
            if (worldCamera == null)
            {
                worldCamera = global::UnityEngine.Camera.main;
            }

            if (player == null)
            {
                CityStateSim.Movement.PlayerMovementController playerMovement =
                    FindFirstObjectByType<CityStateSim.Movement.PlayerMovementController>();
                player = playerMovement != null ? playerMovement.transform : null;
            }

            if (visibilityGroup == null)
            {
                visibilityGroup = GetComponent<CanvasGroup>();
            }

            if (cursorRect == null && visualRoot != null)
            {
                cursorRect = visualRoot.GetComponent<RectTransform>();
            }

            if (cursorRect == null)
            {
                cursorRect = transform as RectTransform;
            }

            if (cursorRect != null)
            {
                parentCanvas = cursorRect.GetComponentInParent<Canvas>();
            }
        }

        private void Start()
        {
            SetVisible(false);
        }

        private void Update()
        {
            if (global::DayOverCheck.IsUserInputLocked)
            {
                ClearCurrent();
                return;
            }

            IInteractable hovered = FindHoveredInteractable();
            if (hovered != null && CanShowCursor(hovered))
            {
                SetCurrent(hovered);
                hideAt = -1f;
            }
            else if (currentInteractable != null)
            {
                if (!CanShowCursor(currentInteractable))
                {
                    ClearCurrent();
                }
                else
                {
                    if (hideAt < 0f)
                    {
                        hideAt = Time.unscaledTime + hideDelay;
                    }

                    if (Time.unscaledTime >= hideAt)
                    {
                        ClearCurrent();
                    }
                }
            }

            RefreshCursorPosition();
            HandleDirectClick();
        }

        private void LateUpdate()
        {
            RefreshCursorPosition();
        }

        public void InteractWithCurrent()
        {
            if (global::DayOverCheck.IsUserInputLocked || currentInteractable == null || player == null)
            {
                return;
            }

            if (currentInteractable.CanInteract(player.gameObject))
            {
                currentInteractable.Interact(player.gameObject);
            }

            ClearCurrent();
        }

        public void HideCursor()
        {
            ClearCurrent();
        }

        private IInteractable FindHoveredInteractable()
        {
            if (worldCamera == null || !TryGetMouseWorldPosition(out Vector3 worldPosition))
            {
                return null;
            }

            Collider2D[] colliders = Physics2D.OverlapPointAll(worldPosition, interactableLayerMask);
            IInteractable closest = null;
            float closestSqrDistance = float.MaxValue;

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D collider = colliders[i];
                if (collider == null || collider.GetComponent<InteractableHoverHitbox>() == null)
                {
                    continue;
                }

                IInteractable interactable = ResolveInteractable(collider);
                if (interactable == null || interactable is not Component component)
                {
                    continue;
                }

                float sqrDistance = ((Vector2)component.transform.position - (Vector2)worldPosition).sqrMagnitude;
                if (sqrDistance < closestSqrDistance)
                {
                    closestSqrDistance = sqrDistance;
                    closest = interactable;
                }
            }

            return closest;
        }

        private IInteractable ResolveInteractable(Collider2D collider)
        {
            MonoBehaviour[] behaviours = collider.GetComponentsInParent<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IInteractable interactable)
                {
                    return interactable;
                }
            }

            return null;
        }

        private bool CanShowCursor(IInteractable interactable)
        {
            if (interactable == null || player == null || !interactable.CanInteract(player.gameObject))
            {
                return false;
            }

            if (maxDistance <= 0f || interactable is not Component component)
            {
                return true;
            }

            return ((Vector2)component.transform.position - (Vector2)player.position).sqrMagnitude
                <= maxDistance * maxDistance;
        }

        private bool TryGetMouseWorldPosition(out Vector3 worldPosition)
        {
            worldPosition = Vector3.zero;
            if (worldCamera == null || !UiScreenPositionUtility.TryGetMouseScreenPosition(worldCamera, out Vector2 screenPosition))
            {
                return false;
            }

            if (!UiScreenPositionUtility.TryScreenPointToWorldOnPlane(worldCamera, screenPosition, 0f, out worldPosition))
            {
                return false;
            }

            worldPosition.z = 0f;
            return true;
        }

        private void SetCurrent(IInteractable interactable)
        {
            if (ReferenceEquals(currentInteractable, interactable))
            {
                return;
            }

            currentInteractable = interactable;
            SetVisible(currentInteractable != null);
            RefreshCursorPosition();
        }

        private void ClearCurrent()
        {
            if (currentInteractable == null)
            {
                SetVisible(false);
                return;
            }

            currentInteractable = null;
            hideAt = -1f;
            SetVisible(false);
        }

        private void HandleDirectClick()
        {
            if (!leftClickInteracts || currentInteractable == null || player == null || !Input.GetMouseButtonDown(0))
            {
                return;
            }

            if (ignoreClicksOverOtherUi && IsPointerBlockedByOtherUi())
            {
                return;
            }

            InteractWithCurrent();
        }

        private bool IsPointerBlockedByOtherUi()
        {
            if (EventSystem.current == null)
            {
                return false;
            }

            PointerEventData eventData = new PointerEventData(EventSystem.current)
            {
                position = Input.mousePosition
            };

            uiRaycastResults.Clear();
            EventSystem.current.RaycastAll(eventData, uiRaycastResults);
            for (int i = 0; i < uiRaycastResults.Count; i++)
            {
                GameObject hitObject = uiRaycastResults[i].gameObject;
                if (hitObject == null)
                {
                    continue;
                }

                if (cursorRect != null && hitObject.transform.IsChildOf(cursorRect))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private void SetVisible(bool visible)
        {
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

        private void RefreshCursorPosition()
        {
            if (cursorRect == null || currentInteractable == null)
            {
                return;
            }

            if (!UiScreenPositionUtility.TryGetMouseScreenPosition(worldCamera, out Vector2 mousePosition))
            {
                return;
            }

            Vector2 screenPoint = mousePosition + screenOffset;
            if (clampToParentRect)
            {
                screenPoint = UiScreenPositionUtility.ClampToCameraPixelRect(worldCamera, screenPoint);
            }

            bool canUseDirectScreenPosition = useDirectScreenPosition
                && (parentCanvas == null || parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay);
            if (canUseDirectScreenPosition)
            {
                cursorRect.position = new Vector3(screenPoint.x, screenPoint.y, cursorRect.position.z);
                return;
            }

            RectTransform parentRect = cursorRect.parent as RectTransform;
            if (parentRect == null)
            {
                cursorRect.position = new Vector3(screenPoint.x, screenPoint.y, cursorRect.position.z);
                return;
            }

            if (UiScreenPositionUtility.TryScreenPointToLocalPoint(parentRect, parentCanvas, screenPoint, out Vector2 localPoint))
            {
                if (clampToParentRect)
                {
                    localPoint = ClampToRect(parentRect, localPoint);
                }

                cursorRect.anchoredPosition = localPoint;
            }
        }

        private static Vector2 ClampToRect(RectTransform rect, Vector2 point)
        {
            Rect localRect = rect.rect;
            point.x = Mathf.Clamp(point.x, localRect.xMin, localRect.xMax);
            point.y = Mathf.Clamp(point.y, localRect.yMin, localRect.yMax);
            return point;
        }
    }
}
