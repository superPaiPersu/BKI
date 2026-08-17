using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using CityStateSim.AI;
using CityStateSim.Core;
using CityStateSim.NPC;
using CityStateSim.Relationships;
using UnityEngine;
using UnityEngine.Networking;

namespace CityStateSim.Memory
{
    public sealed class AiMemoryConsolidator : MonoBehaviour, IDayEndSettlementTask
    {
        private const string ApiUrl = "https://cf.ai-pixel.online/v1/responses";

        [Header("References")]
        [SerializeField] private GameClock clock;
        [SerializeField] private MemorySystem memorySystem;
        [SerializeField] private RelationshipSystem relationshipSystem;

        [Header("OpenAI Compatible API")]
        [SerializeField] private string model = AiModelDefaults.FastModel;
        [SerializeField] private string apiKey;
        [SerializeField, Min(1)] private int timeoutSeconds = 45;

        [Header("Policy")]
        [SerializeField] private bool consolidateOnDayEnding = true;
        [SerializeField, Min(1)] private int maxMemoryLines = 30;
        [SerializeField] private bool useRuleFallback = true;
        [SerializeField] private bool logConsolidation = true;
        [SerializeField] private bool logRawResponse;

        private int activeConsolidationJobs;

        public bool IsDayEndSettlementRunning => activeConsolidationJobs > 0;

        private void Awake()
        {
            if (clock == null)
            {
                clock = FindFirstObjectByType<GameClock>();
            }

            if (memorySystem == null)
            {
                memorySystem = FindFirstObjectByType<MemorySystem>();
            }

            if (relationshipSystem == null)
            {
                relationshipSystem = FindFirstObjectByType<RelationshipSystem>();
            }
        }

        private void OnEnable()
        {
            if (clock != null)
            {
                clock.DayEnding += HandleDayEnding;
            }
        }

        private void OnDisable()
        {
            if (clock != null)
            {
                clock.DayEnding -= HandleDayEnding;
            }
        }

        public void ConsolidateYesterday()
        {
            if (clock != null)
            {
                ConsolidateDate(clock.CurrentDate);
            }
        }

        public void ConsolidateDate(GameDate date)
        {
            if (memorySystem == null)
            {
                return;
            }

            NpcRuntimeState[] npcs = FindObjectsByType<NpcRuntimeState>(FindObjectsSortMode.None);
            int jobCount = 0;
            for (int i = 0; i < npcs.Length; i++)
            {
                NpcProfile profile = npcs[i] != null ? npcs[i].Profile : null;
                if (profile != null)
                {
                    jobCount++;
                }
            }

            if (jobCount <= 0)
            {
                return;
            }

            activeConsolidationJobs += jobCount;
            for (int i = 0; i < npcs.Length; i++)
            {
                NpcProfile profile = npcs[i] != null ? npcs[i].Profile : null;
                if (profile != null)
                {
                    StartCoroutine(ConsolidateNpcTracked(profile, date));
                }
            }
        }

        private IEnumerator ConsolidateNpcTracked(NpcProfile profile, GameDate date)
        {
            yield return ConsolidateNpc(profile, date);
            activeConsolidationJobs = Mathf.Max(0, activeConsolidationJobs - 1);
        }

        private IEnumerator ConsolidateNpc(NpcProfile profile, GameDate date)
        {
            string memorySummary = BuildConsolidationInput(profile.NpcId, date);
            if (string.IsNullOrWhiteSpace(memorySummary))
            {
                ConsolidateMemoryAndFacts(profile.NpcId, date);
                yield break;
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                ApplyFallback(profile, date, "missing API key");
                yield break;
            }

            string body = BuildRequestBody(profile, date, memorySummary);
            using UnityWebRequest webRequest = new UnityWebRequest(ApiUrl, UnityWebRequest.kHttpVerbPOST);
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
            webRequest.uploadHandler = new UploadHandlerRaw(bodyBytes);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.timeout = timeoutSeconds;
            webRequest.SetRequestHeader("Content-Type", "application/json");
            webRequest.SetRequestHeader("Authorization", $"Bearer {apiKey.Trim()}");

            yield return webRequest.SendWebRequest();

            if (webRequest.result != UnityWebRequest.Result.Success)
            {
                ApplyFallback(profile, date, $"request failed: {webRequest.responseCode} {webRequest.error}");
                yield break;
            }

            string rawResponse = webRequest.downloadHandler.text;
            if (logRawResponse)
            {
                Debug.Log($"[Memory Consolidation] Raw response for {profile.DisplayName}: {rawResponse}", this);
            }

            if (!TryParse(rawResponse, out NpcMemoryConsolidationResponse result, out string error))
            {
                ApplyFallback(profile, date, error);
                yield break;
            }

            ApplyResult(profile, date, result);
        }

        private string BuildRequestBody(NpcProfile profile, GameDate date, string memorySummary)
        {
            string systemPrompt = NpcAiPromptBuilder.EscapeJson(
                "You are an NPC memory consolidation module for a life simulation game. " +
                "Return only valid JSON matching the schema. " +
                "The NPC does not keep every ordinary line of dialogue. Convert ordinary experiences into impressions and relationship deltas. " +
                "Keep long-term memories only for promises, appointments, debts, secrets, threats, trauma, unresolved conflicts, major discoveries, and facts with future consequences. " +
                "Discard greetings, small talk, repeated casual replies, and non-consequential observations unless they reveal an important pattern. " +
                "Impression changes are not the same as affection. For example, poor clothing may reduce cleanliness impression, but affection depends on the NPC's values.");

            string userPrompt = NpcAiPromptBuilder.EscapeJson(
                $"NPC id: {profile.NpcId}\n" +
                $"NPC name: {profile.DisplayName}\n" +
                $"Role: {profile.Role}\n" +
                $"Personality: {profile.PersonalitySummary}\n" +
                $"Value profile: {(profile.ValueProfile != null ? profile.ValueProfile.ToSummary() : "unknown")}\n" +
                $"Date to consolidate: {date}\n\n" +
                "Consolidation input:\n" +
                memorySummary +
                "\n\nRules:\n" +
                "- keepMemoryKeywords should contain short distinctive phrases from memories that should become long-term.\n" +
                "- discardMemoryKeywords should contain short distinctive phrases from memories that can be discarded.\n" +
                "- Use impressionChanges for ordinary observations and interactions that should shape how this NPC sees another actor.\n" +
                "- Use relationshipChanges only when trust/affinity/suspicion should actually change.\n" +
                "- Keep deltas small, usually -2 to 2.");

            return
                "{" +
                $"\"model\":\"{NpcAiPromptBuilder.EscapeJson(AiModelDefaults.ResolveRuntimeModel(model))}\"," +
                "\"input\":[" +
                "{\"role\":\"system\",\"content\":[{\"type\":\"input_text\",\"text\":\"" + systemPrompt + "\"}]}," +
                "{\"role\":\"user\",\"content\":[{\"type\":\"input_text\",\"text\":\"" + userPrompt + "\"}]}" +
                "]," +
                "\"text\":{" +
                "\"format\":{" +
                "\"type\":\"json_schema\"," +
                "\"name\":\"npc_memory_consolidation\"," +
                "\"strict\":true," +
                "\"schema\":" + BuildSchema() +
                "}" +
                "}" +
                "}";
        }

        private static string BuildSchema()
        {
            return
                "{" +
                "\"type\":\"object\"," +
                "\"additionalProperties\":false," +
                "\"properties\":{" +
                "\"summary\":{\"type\":\"string\"}," +
                "\"keepMemoryKeywords\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}," +
                "\"discardMemoryKeywords\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}," +
                "\"impressionChanges\":{\"type\":\"array\",\"items\":{" +
                "\"type\":\"object\",\"additionalProperties\":false,\"properties\":{" +
                "\"targetActorId\":{\"type\":\"string\"}," +
                "\"cleanlinessDelta\":{\"type\":\"integer\",\"minimum\":-5,\"maximum\":5}," +
                "\"reliabilityDelta\":{\"type\":\"integer\",\"minimum\":-5,\"maximum\":5}," +
                "\"warmthDelta\":{\"type\":\"integer\",\"minimum\":-5,\"maximum\":5}," +
                "\"competenceDelta\":{\"type\":\"integer\",\"minimum\":-5,\"maximum\":5}," +
                "\"charmDelta\":{\"type\":\"integer\",\"minimum\":-5,\"maximum\":5}," +
                "\"concernDelta\":{\"type\":\"integer\",\"minimum\":-5,\"maximum\":5}," +
                "\"reason\":{\"type\":\"string\"}" +
                "},\"required\":[\"targetActorId\",\"cleanlinessDelta\",\"reliabilityDelta\",\"warmthDelta\",\"competenceDelta\",\"charmDelta\",\"concernDelta\",\"reason\"]" +
                "}}," +
                "\"relationshipChanges\":{\"type\":\"array\",\"items\":{" +
                "\"type\":\"object\",\"additionalProperties\":false,\"properties\":{" +
                "\"targetActorId\":{\"type\":\"string\"}," +
                "\"trustDelta\":{\"type\":\"integer\",\"minimum\":-5,\"maximum\":5}," +
                "\"affinityDelta\":{\"type\":\"integer\",\"minimum\":-5,\"maximum\":5}," +
                "\"suspicionDelta\":{\"type\":\"integer\",\"minimum\":-5,\"maximum\":5}," +
                "\"reason\":{\"type\":\"string\"}" +
                "},\"required\":[\"targetActorId\",\"trustDelta\",\"affinityDelta\",\"suspicionDelta\",\"reason\"]" +
                "}}" +
                "}," +
                "\"required\":[\"summary\",\"keepMemoryKeywords\",\"discardMemoryKeywords\",\"impressionChanges\",\"relationshipChanges\"]" +
                "}";
        }

        private void ApplyResult(NpcProfile profile, GameDate date, NpcMemoryConsolidationResponse result)
        {
            ApplyMemoryKeywords(profile.NpcId, date, result.keepMemoryKeywords, true);
            ApplyMemoryKeywords(profile.NpcId, date, result.discardMemoryKeywords, false);

            if (relationshipSystem != null && result.impressionChanges != null)
            {
                for (int i = 0; i < result.impressionChanges.Length; i++)
                {
                    NpcImpressionDeltaAiEntry change = result.impressionChanges[i];
                    if (change == null || string.IsNullOrWhiteSpace(change.targetActorId))
                    {
                        continue;
                    }

                    relationshipSystem.ApplyImpressionDelta(
                        profile.NpcId,
                        change.targetActorId,
                        change.cleanlinessDelta,
                        change.reliabilityDelta,
                        change.warmthDelta,
                        change.competenceDelta,
                        change.charmDelta,
                        change.concernDelta,
                        change.reason);
                }
            }

            if (relationshipSystem != null && result.relationshipChanges != null)
            {
                for (int i = 0; i < result.relationshipChanges.Length; i++)
                {
                    NpcRelationshipDeltaAiEntry change = result.relationshipChanges[i];
                    if (change == null || string.IsNullOrWhiteSpace(change.targetActorId))
                    {
                        continue;
                    }

                    relationshipSystem.ApplyDelta(profile.NpcId, change.targetActorId, change.trustDelta, change.affinityDelta, change.suspicionDelta);
                }
            }

            memorySystem.AddMemory(profile.NpcId, $"Daily memory consolidation: {result.summary}", "memory_consolidation", 8);
            ConsolidateMemoryAndFacts(profile.NpcId, date);

            if (logConsolidation)
            {
                Debug.Log($"[Memory Consolidation] {profile.DisplayName}: {result.summary}", this);
            }
        }

        private void ApplyMemoryKeywords(string npcId, GameDate date, string[] keywords, bool keep)
        {
            if (keywords == null || keywords.Length == 0)
            {
                return;
            }

            IReadOnlyList<MemoryRecord> records = memorySystem.GetMemoriesForDate(npcId, date);
            for (int i = 0; i < records.Count; i++)
            {
                MemoryRecord record = records[i];
                for (int j = 0; j < keywords.Length; j++)
                {
                    if (ContainsKeyword(record, keywords[j]))
                    {
                        if (keep)
                        {
                            memorySystem.MarkLongTerm(npcId, record, "ai_kept");
                        }
                        else
                        {
                            memorySystem.MarkDiscarded(npcId, record);
                        }
                    }
                }
            }
        }

        private void ApplyFallback(NpcProfile profile, GameDate date, string reason)
        {
            if (useRuleFallback)
            {
                ConsolidateMemoryAndFacts(profile.NpcId, date);
            }

            if (logConsolidation)
            {
                Debug.LogWarning($"[Memory Consolidation] {profile.DisplayName}: fallback used ({reason}).", this);
            }
        }

        private static bool ContainsKeyword(MemoryRecord record, string keyword)
        {
            return record != null
                && !string.IsNullOrWhiteSpace(keyword)
                && record.Summary.IndexOf(keyword.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string BuildConsolidationInput(string npcId, GameDate date)
        {
            string memorySummary = memorySystem.BuildSummaryForDate(npcId, date, maxMemoryLines);
            string factSummary = memorySystem.BuildFactSummaryForDate(npcId, date, maxMemoryLines);
            string playerDialogueTranscript = memorySystem.BuildPlayerDialogueTranscriptForDate(npcId, date);
            StringBuilder builder = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(memorySummary))
            {
                builder.AppendLine("Today's memories:");
                builder.AppendLine(memorySummary);
            }

            if (!string.IsNullOrWhiteSpace(playerDialogueTranscript))
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.AppendLine("Same-day raw player dialogue transcript:");
                builder.AppendLine(playerDialogueTranscript);
            }

            if (!string.IsNullOrWhiteSpace(factSummary))
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.AppendLine("Evidence fact records:");
                builder.AppendLine(factSummary);
            }

            return builder.ToString().Trim();
        }

        private void ConsolidateMemoryAndFacts(string npcId, GameDate date)
        {
            memorySystem.ConsolidateDate(npcId, date);
            memorySystem.ConsolidateFactsBeforeDate(npcId, date);
        }

        private static bool TryParse(string rawResponse, out NpcMemoryConsolidationResponse result, out string error)
        {
            result = null;
            error = string.Empty;
            OpenAiResponsesApiResponse response = JsonUtility.FromJson<OpenAiResponsesApiResponse>(rawResponse);
            if (response == null)
            {
                error = "memory consolidation response could not be parsed";
                return false;
            }

            if (response.error != null && !string.IsNullOrWhiteSpace(response.error.message))
            {
                error = response.error.message;
                return false;
            }

            string outputText = ExtractOutputText(response);
            if (string.IsNullOrWhiteSpace(outputText))
            {
                error = "memory consolidation response did not include output text";
                return false;
            }

            result = JsonUtility.FromJson<NpcMemoryConsolidationResponse>(outputText);
            if (result == null)
            {
                error = $"memory consolidation JSON could not be parsed: {outputText}";
                return false;
            }

            return true;
        }

        private static string ExtractOutputText(OpenAiResponsesApiResponse response)
        {
            if (response.output == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < response.output.Length; i++)
            {
                OpenAiOutputContent[] content = response.output[i].content;
                if (content == null)
                {
                    continue;
                }

                for (int j = 0; j < content.Length; j++)
                {
                    if (!string.IsNullOrWhiteSpace(content[j].text))
                    {
                        return content[j].text;
                    }
                }
            }

            return string.Empty;
        }

        private void HandleDayEnding(GameDate date)
        {
            if (consolidateOnDayEnding)
            {
                ConsolidateDate(date);
            }
        }
    }
}
