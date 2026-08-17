using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace CityStateSim.AI
{
    public sealed class OpenAiNpcBrainProvider : NpcBrainProviderBehaviour
    {
        private const string ApiUrl = "https://cf.ai-pixel.online/v1/responses";

        [Header("OpenAI")]
        [SerializeField] private string model = AiModelDefaults.FastModel;
        [SerializeField] private string apiKey;
        [SerializeField, Min(1)] private int timeoutSeconds = 30;

        [Header("Debug")]
        [SerializeField] private bool logRawResponse;

        public override void RequestDecision(NpcAiRequest request, Action<NpcAiDecision> onSuccess, Action<string> onError)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                onError?.Invoke("Missing OpenAI API key. Fill apiKey on OpenAiNpcBrainProvider.");
                return;
            }

            RequestToken token = BeginTrackedRequest(request);
            StartCoroutine(SendRequest(token, request, apiKey.Trim(), onSuccess, onError));
        }

        private IEnumerator SendRequest(RequestToken token, NpcAiRequest request, string apiKey, Action<NpcAiDecision> onSuccess, Action<string> onError)
        {
            string body = BuildRequestBody(request);
            using UnityWebRequest webRequest = new UnityWebRequest(ApiUrl, UnityWebRequest.kHttpVerbPOST);
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
            webRequest.uploadHandler = new UploadHandlerRaw(bodyBytes);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.timeout = timeoutSeconds;
            webRequest.SetRequestHeader("Content-Type", "application/json");
            webRequest.SetRequestHeader("Authorization", $"Bearer {apiKey}");

            yield return webRequest.SendWebRequest();

            if (webRequest.result != UnityWebRequest.Result.Success)
            {
                string error = $"OpenAI request failed: {webRequest.responseCode} {webRequest.error} {webRequest.downloadHandler.text}";
                FinishTrackedRequest(token, false, null, error);
                onError?.Invoke(error);
                yield break;
            }

            string rawResponse = webRequest.downloadHandler.text;
            if (logRawResponse)
            {
                Debug.Log($"[OpenAI NPC Brain] {rawResponse}", this);
            }

            if (!TryParseDecision(rawResponse, out NpcAiDecision decision, out string parseError))
            {
                FinishTrackedRequest(token, false, null, parseError);
                onError?.Invoke(parseError);
                yield break;
            }

            decision.ClampHints();
            FinishTrackedRequest(token, true, decision, null);
            onSuccess?.Invoke(decision);
        }

        private string BuildRequestBody(NpcAiRequest request)
        {
            string systemPrompt = NpcAiPromptBuilder.EscapeJson(NpcAiPromptBuilder.SystemPrompt);
            string userPrompt = NpcAiPromptBuilder.EscapeJson(NpcAiPromptBuilder.BuildUserPrompt(request));
            string schema = NpcAiPromptBuilder.BuildDecisionSchema();
            string escapedModel = NpcAiPromptBuilder.EscapeJson(AiModelDefaults.ResolveRuntimeModel(model));

            return
                "{" +
                $"\"model\":\"{escapedModel}\"," +
                "\"input\":[" +
                "{\"role\":\"system\",\"content\":[{\"type\":\"input_text\",\"text\":\"" + systemPrompt + "\"}]}," +
                "{\"role\":\"user\",\"content\":[{\"type\":\"input_text\",\"text\":\"" + userPrompt + "\"}]}" +
                "]," +
                "\"text\":{" +
                "\"format\":{" +
                "\"type\":\"json_schema\"," +
                "\"name\":\"npc_ai_decision\"," +
                "\"strict\":true," +
                "\"schema\":" + schema +
                "}" +
                "}" +
                "}";
        }

        private static bool TryParseDecision(string rawResponse, out NpcAiDecision decision, out string error)
        {
            decision = null;
            error = string.Empty;

            OpenAiResponsesApiResponse response = JsonUtility.FromJson<OpenAiResponsesApiResponse>(rawResponse);
            if (response == null)
            {
                error = "OpenAI response could not be parsed.";
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
                error = "OpenAI response did not include output text.";
                return false;
            }

            decision = JsonUtility.FromJson<NpcAiDecision>(outputText);
            if (decision == null)
            {
                error = $"NPC decision JSON could not be parsed: {outputText}";
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
    }
}
