using System.Collections.Generic;
using UnityEngine;

namespace CityStateSim.NPC
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NpcRuntimeState))]
    public sealed class NpcWorldPresencePresenter : MonoBehaviour
    {
        [SerializeField] private bool cacheChildrenOnAwake = true;
        [SerializeField] private bool includeInactiveChildren = true;
        [SerializeField] private bool disableCollidersWhenInsideActivity = true;
        [SerializeField] private bool disableInteractablesWhenInsideActivity = true;
        [SerializeField] private bool hideActiveBubbleWhenInsideActivity = true;
        [SerializeField] private Renderer[] worldRenderers;
        [SerializeField] private Collider2D[] worldColliders;
        [SerializeField] private NpcInteractable[] interactables;

        private readonly Dictionary<Renderer, bool> rendererStatesBeforeHiding = new Dictionary<Renderer, bool>();
        private readonly Dictionary<Collider2D, bool> colliderStatesBeforeHiding = new Dictionary<Collider2D, bool>();
        private readonly Dictionary<NpcInteractable, bool> interactableStatesBeforeHiding = new Dictionary<NpcInteractable, bool>();
        private NpcRuntimeState runtimeState;
        private bool insideStateApplied;

        private void Awake()
        {
            runtimeState = GetComponent<NpcRuntimeState>();
            if (cacheChildrenOnAwake)
            {
                CacheWorldComponentsIfNeeded();
            }
        }

        private void OnEnable()
        {
            if (runtimeState == null)
            {
                runtimeState = GetComponent<NpcRuntimeState>();
            }

            if (runtimeState != null)
            {
                runtimeState.PresenceChanged += HandlePresenceChanged;
                ApplyPresence(runtimeState.PresenceMode);
            }
        }

        private void OnDisable()
        {
            if (runtimeState != null)
            {
                runtimeState.PresenceChanged -= HandlePresenceChanged;
            }
        }

        public void RebuildCache()
        {
            worldRenderers = GetComponentsInChildren<Renderer>(includeInactiveChildren);
            worldColliders = GetComponentsInChildren<Collider2D>(includeInactiveChildren);
            interactables = GetComponentsInChildren<NpcInteractable>(includeInactiveChildren);
            rendererStatesBeforeHiding.Clear();
            colliderStatesBeforeHiding.Clear();
            interactableStatesBeforeHiding.Clear();
            insideStateApplied = false;
            if (runtimeState != null)
            {
                ApplyPresence(runtimeState.PresenceMode);
            }
        }

        private void HandlePresenceChanged(NpcRuntimeState state)
        {
            ApplyPresence(state != null ? state.PresenceMode : NpcPresenceMode.World);
        }

        private void ApplyPresence(NpcPresenceMode mode)
        {
            CacheWorldComponentsIfNeeded();

            if (mode == NpcPresenceMode.InsideActivity)
            {
                if (!insideStateApplied)
                {
                    CaptureCurrentWorldState();
                    insideStateApplied = true;
                }

                SetWorldPresentationVisible(false);
                if (hideActiveBubbleWhenInsideActivity && runtimeState != null)
                {
                    global::MessageDisplayer messageDisplayer = FindFirstObjectByType<global::MessageDisplayer>();
                    messageDisplayer?.HideMessage(runtimeState);
                }

                return;
            }

            if (insideStateApplied
                || rendererStatesBeforeHiding.Count > 0
                || colliderStatesBeforeHiding.Count > 0
                || interactableStatesBeforeHiding.Count > 0)
            {
                RestoreWorldPresentation();
            }

            insideStateApplied = false;
        }

        private void CacheWorldComponentsIfNeeded()
        {
            if (!cacheChildrenOnAwake)
            {
                return;
            }

            if (worldRenderers == null || worldRenderers.Length == 0)
            {
                worldRenderers = GetComponentsInChildren<Renderer>(includeInactiveChildren);
            }

            if (worldColliders == null || worldColliders.Length == 0)
            {
                worldColliders = GetComponentsInChildren<Collider2D>(includeInactiveChildren);
            }

            if (interactables == null || interactables.Length == 0)
            {
                interactables = GetComponentsInChildren<NpcInteractable>(includeInactiveChildren);
            }
        }

        private void CaptureCurrentWorldState()
        {
            rendererStatesBeforeHiding.Clear();
            colliderStatesBeforeHiding.Clear();
            interactableStatesBeforeHiding.Clear();

            for (int i = 0; i < worldRenderers.Length; i++)
            {
                Renderer worldRenderer = worldRenderers[i];
                if (worldRenderer != null && !rendererStatesBeforeHiding.ContainsKey(worldRenderer))
                {
                    rendererStatesBeforeHiding.Add(worldRenderer, worldRenderer.enabled);
                }
            }

            for (int i = 0; i < worldColliders.Length; i++)
            {
                Collider2D worldCollider = worldColliders[i];
                if (worldCollider != null && !colliderStatesBeforeHiding.ContainsKey(worldCollider))
                {
                    colliderStatesBeforeHiding.Add(worldCollider, worldCollider.enabled);
                }
            }

            for (int i = 0; i < interactables.Length; i++)
            {
                NpcInteractable interactable = interactables[i];
                if (interactable != null && !interactableStatesBeforeHiding.ContainsKey(interactable))
                {
                    interactableStatesBeforeHiding.Add(interactable, interactable.enabled);
                }
            }
        }

        private void SetWorldPresentationVisible(bool visible)
        {
            for (int i = 0; i < worldRenderers.Length; i++)
            {
                Renderer worldRenderer = worldRenderers[i];
                if (worldRenderer != null)
                {
                    worldRenderer.enabled = visible;
                }
            }

            if (disableCollidersWhenInsideActivity)
            {
                for (int i = 0; i < worldColliders.Length; i++)
                {
                    Collider2D worldCollider = worldColliders[i];
                    if (worldCollider != null)
                    {
                        worldCollider.enabled = visible;
                    }
                }
            }

            if (disableInteractablesWhenInsideActivity)
            {
                for (int i = 0; i < interactables.Length; i++)
                {
                    NpcInteractable interactable = interactables[i];
                    if (interactable != null)
                    {
                        interactable.enabled = visible;
                    }
                }
            }
        }

        private void RestoreWorldPresentation()
        {
            for (int i = 0; i < worldRenderers.Length; i++)
            {
                Renderer worldRenderer = worldRenderers[i];
                if (worldRenderer == null)
                {
                    continue;
                }

                worldRenderer.enabled = rendererStatesBeforeHiding.TryGetValue(worldRenderer, out bool wasEnabled)
                    ? wasEnabled
                    : true;
            }

            if (disableCollidersWhenInsideActivity)
            {
                for (int i = 0; i < worldColliders.Length; i++)
                {
                    Collider2D worldCollider = worldColliders[i];
                    if (worldCollider == null)
                    {
                        continue;
                    }

                    worldCollider.enabled = colliderStatesBeforeHiding.TryGetValue(worldCollider, out bool wasEnabled)
                        ? wasEnabled
                        : true;
                }
            }

            if (disableInteractablesWhenInsideActivity)
            {
                for (int i = 0; i < interactables.Length; i++)
                {
                    NpcInteractable interactable = interactables[i];
                    if (interactable == null)
                    {
                        continue;
                    }

                    interactable.enabled = interactableStatesBeforeHiding.TryGetValue(interactable, out bool wasEnabled)
                        ? wasEnabled
                        : true;
                }
            }
        }
    }
}
