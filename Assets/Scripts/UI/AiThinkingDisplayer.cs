using System.Collections.Generic;
using CityStateSim.AI;
using CityStateSim.NPC;
using UnityEngine;

public sealed class AiThinkingDisplayer : MonoBehaviour
{
    [Header("Prefab / Pool")]
    [SerializeField] private AiThinkingIndicator indicatorPrefab;
    [SerializeField] private Transform poolRoot;
    [SerializeField, Min(0)] private int initialPoolSize = 4;

    [Header("Position")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 1.9f, 0f);
    [SerializeField] private bool useScreenSpacePosition = true;

    [Header("Debug")]
    [SerializeField] private bool logMissingNpc;
    [SerializeField] private bool logMissingPrefab = true;

    private readonly List<AiThinkingIndicator> pool = new List<AiThinkingIndicator>();
    private readonly Dictionary<string, NpcRuntimeState> npcById = new Dictionary<string, NpcRuntimeState>();
    private readonly Dictionary<string, AiThinkingIndicator> activeIndicatorsByNpcId = new Dictionary<string, AiThinkingIndicator>();
    private readonly Dictionary<string, int> activeRequestCountsByNpcId = new Dictionary<string, int>();
    private bool warnedMissingPrefab;

    private void Awake()
    {
        if (poolRoot == null)
        {
            poolRoot = transform;
        }

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        RefreshNpcCache();
        if (indicatorPrefab != null && indicatorPrefab.gameObject.scene.IsValid())
        {
            indicatorPrefab.Release();
        }

        WarmPool(initialPoolSize);
    }

    private void OnEnable()
    {
        NpcBrainProviderBehaviour.GlobalRequestStarted += HandleAiRequestStarted;
        NpcBrainProviderBehaviour.GlobalRequestFinished += HandleAiRequestFinished;
    }

    private void OnDisable()
    {
        NpcBrainProviderBehaviour.GlobalRequestStarted -= HandleAiRequestStarted;
        NpcBrainProviderBehaviour.GlobalRequestFinished -= HandleAiRequestFinished;
        HideAll();
    }

    public void RefreshNpcCache()
    {
        npcById.Clear();
        NpcRuntimeState[] npcs = FindObjectsByType<NpcRuntimeState>(FindObjectsSortMode.None);
        for (int i = 0; i < npcs.Length; i++)
        {
            NpcRuntimeState npc = npcs[i];
            if (npc == null || npc.Profile == null || string.IsNullOrWhiteSpace(npc.Profile.NpcId))
            {
                continue;
            }

            npcById[npc.Profile.NpcId] = npc;
        }
    }

    private void HandleAiRequestStarted(NpcBrainProviderBehaviour.RequestToken token)
    {
        string npcId = token != null && token.Request != null ? token.Request.npcId : string.Empty;
        if (string.IsNullOrWhiteSpace(npcId))
        {
            return;
        }

        activeRequestCountsByNpcId.TryGetValue(npcId, out int count);
        activeRequestCountsByNpcId[npcId] = count + 1;

        if (activeIndicatorsByNpcId.ContainsKey(npcId))
        {
            return;
        }

        NpcRuntimeState npc = FindNpc(npcId);
        if (npc == null)
        {
            if (logMissingNpc)
            {
                Debug.LogWarning($"[AiThinkingDisplayer] Could not find NPC for thinking indicator. npcId={npcId}", this);
            }

            return;
        }

        AiThinkingIndicator indicator = GetFreeIndicator();
        if (indicator == null)
        {
            return;
        }

        activeIndicatorsByNpcId[npcId] = indicator;
        indicator.Bind(npc.transform, worldOffset, targetCamera, useScreenSpacePosition);
    }

    private void HandleAiRequestFinished(NpcBrainProviderBehaviour.RequestFinishedEvent finishedEvent)
    {
        string npcId = finishedEvent != null && finishedEvent.Token != null && finishedEvent.Token.Request != null
            ? finishedEvent.Token.Request.npcId
            : string.Empty;
        if (string.IsNullOrWhiteSpace(npcId))
        {
            return;
        }

        if (!activeRequestCountsByNpcId.TryGetValue(npcId, out int count))
        {
            HideIndicator(npcId);
            return;
        }

        count--;
        if (count > 0)
        {
            activeRequestCountsByNpcId[npcId] = count;
            return;
        }

        activeRequestCountsByNpcId.Remove(npcId);
        HideIndicator(npcId);
    }

    private NpcRuntimeState FindNpc(string npcId)
    {
        if (npcById.TryGetValue(npcId, out NpcRuntimeState npc) && npc != null)
        {
            return npc;
        }

        RefreshNpcCache();
        npcById.TryGetValue(npcId, out npc);
        return npc;
    }

    private void WarmPool(int count)
    {
        for (int i = 0; i < count; i++)
        {
            CreateIndicator();
        }
    }

    private AiThinkingIndicator GetFreeIndicator()
    {
        for (int i = 0; i < pool.Count; i++)
        {
            AiThinkingIndicator indicator = pool[i];
            if (indicator != null && !indicator.IsInUse)
            {
                return indicator;
            }
        }

        return CreateIndicator();
    }

    private AiThinkingIndicator CreateIndicator()
    {
        if (indicatorPrefab == null)
        {
            if (logMissingPrefab && !warnedMissingPrefab)
            {
                warnedMissingPrefab = true;
                Debug.LogError("[AiThinkingDisplayer] indicatorPrefab is missing. Assign a prefab with AiThinkingIndicator.", this);
            }

            return null;
        }

        AiThinkingIndicator indicator = Instantiate(indicatorPrefab, poolRoot);
        indicator.Release();
        pool.Add(indicator);
        return indicator;
    }

    private void HideIndicator(string npcId)
    {
        if (!activeIndicatorsByNpcId.TryGetValue(npcId, out AiThinkingIndicator indicator))
        {
            return;
        }

        activeIndicatorsByNpcId.Remove(npcId);
        if (indicator != null)
        {
            indicator.Release();
        }
    }

    private void HideAll()
    {
        foreach (AiThinkingIndicator indicator in activeIndicatorsByNpcId.Values)
        {
            if (indicator != null)
            {
                indicator.Release();
            }
        }

        activeIndicatorsByNpcId.Clear();
        activeRequestCountsByNpcId.Clear();
    }
}
