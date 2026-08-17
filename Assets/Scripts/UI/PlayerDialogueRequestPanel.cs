using System.Collections.Generic;
using CityStateSim.Dialogue;
using UnityEngine;

public sealed class PlayerDialogueRequestPanel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerDialogueRequestSystem requestSystem;
    [SerializeField] private Transform itemContainer;
    [SerializeField] private PlayerDialogueRequestItem itemPrefab;

    [Header("Visibility")]
    [SerializeField] private CanvasGroup visibilityGroup;
    [SerializeField] private GameObject visualRoot;
    [SerializeField] private bool hideWhenEmpty = true;

    private readonly Dictionary<int, PlayerDialogueRequestItem> itemsByRequestId = new Dictionary<int, PlayerDialogueRequestItem>();
    private bool subscribed;

    private void Awake()
    {
        if (requestSystem == null)
        {
            requestSystem = FindFirstObjectByType<PlayerDialogueRequestSystem>();
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

    private void OnEnable()
    {
        Subscribe();
        RebuildList();
    }

    private void Start()
    {
        RefreshVisibility();
    }

    private void Update()
    {
        if (requestSystem == null)
        {
            requestSystem = FindFirstObjectByType<PlayerDialogueRequestSystem>();
            Subscribe();
            RebuildList();
        }
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (subscribed || requestSystem == null)
        {
            return;
        }

        requestSystem.RequestAdded.AddListener(HandleRequestAdded);
        requestSystem.RequestFinished.AddListener(HandleRequestFinished);
        requestSystem.RequestListChanged.AddListener(RefreshVisibility);
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || requestSystem == null)
        {
            subscribed = false;
            return;
        }

        requestSystem.RequestAdded.RemoveListener(HandleRequestAdded);
        requestSystem.RequestFinished.RemoveListener(HandleRequestFinished);
        requestSystem.RequestListChanged.RemoveListener(RefreshVisibility);
        subscribed = false;
    }

    private void RebuildList()
    {
        ClearItems();
        if (requestSystem == null)
        {
            RefreshVisibility();
            return;
        }

        for (int i = 0; i < requestSystem.RequestCount; i++)
        {
            CreateItem(requestSystem.GetRequest(i));
        }

        RefreshVisibility();
    }

    private void HandleRequestAdded(PlayerDialogueRequest request)
    {
        CreateItem(request);
        RefreshVisibility();
    }

    private void HandleRequestFinished(PlayerDialogueRequest request, PlayerDialogueRequestResult result)
    {
        if (request == null)
        {
            RefreshVisibility();
            return;
        }

        if (itemsByRequestId.TryGetValue(request.RequestId, out PlayerDialogueRequestItem item))
        {
            itemsByRequestId.Remove(request.RequestId);
            if (item != null)
            {
                Destroy(item.gameObject);
            }
        }

        RefreshVisibility();
    }

    private void CreateItem(PlayerDialogueRequest request)
    {
        if (request == null || itemPrefab == null || itemContainer == null || itemsByRequestId.ContainsKey(request.RequestId))
        {
            return;
        }

        PlayerDialogueRequestItem item = Instantiate(itemPrefab, itemContainer);
        item.Bind(requestSystem, request);
        itemsByRequestId[request.RequestId] = item;
    }

    private void ClearItems()
    {
        foreach (PlayerDialogueRequestItem item in itemsByRequestId.Values)
        {
            if (item != null)
            {
                Destroy(item.gameObject);
            }
        }

        itemsByRequestId.Clear();
    }

    private void RefreshVisibility()
    {
        bool visible = !hideWhenEmpty || itemsByRequestId.Count > 0;
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
}
