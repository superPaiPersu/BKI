using UnityEngine;

namespace CityStateSim.AI
{
    public abstract class NpcBrainProviderBehaviour : MonoBehaviour, INpcBrainProvider
    {
        private static int globalActiveRequestCount;

        public sealed class RequestToken
        {
            internal RequestToken(NpcBrainProviderBehaviour provider, NpcAiRequest request)
            {
                Provider = provider;
                Request = request;
                StartedRealtime = Time.realtimeSinceStartup;
                IsActive = true;
            }

            public NpcBrainProviderBehaviour Provider { get; }
            public NpcAiRequest Request { get; }
            public float StartedRealtime { get; }
            public bool IsActive { get; internal set; }
        }

        public sealed class RequestFinishedEvent
        {
            internal RequestFinishedEvent(RequestToken token, bool success, NpcAiDecision decision, string error)
            {
                Token = token;
                Success = success;
                Decision = decision;
                Error = error;
            }

            public RequestToken Token { get; }
            public bool Success { get; }
            public NpcAiDecision Decision { get; }
            public string Error { get; }
        }

        public static int GlobalActiveRequestCount => globalActiveRequestCount;

        public static event System.Action<RequestToken> GlobalRequestStarted;
        public static event System.Action<RequestFinishedEvent> GlobalRequestFinished;
        public static event System.Action<int> GlobalActiveRequestCountChanged;

        public event System.Action<RequestToken> RequestStarted;
        public event System.Action<RequestFinishedEvent> RequestFinished;

        public abstract void RequestDecision(NpcAiRequest request, System.Action<NpcAiDecision> onSuccess, System.Action<string> onError);

        public static NpcBrainProviderBehaviour FindPreferredProvider()
        {
            OpenAiNpcBrainProvider openAi = FindFirstObjectByType<OpenAiNpcBrainProvider>();
            if (openAi != null)
            {
                return openAi;
            }

            return FindFirstObjectByType<NpcBrainProviderBehaviour>();
        }

        protected RequestToken BeginTrackedRequest(NpcAiRequest request)
        {
            RequestToken token = new RequestToken(this, request);
            globalActiveRequestCount++;
            RequestStarted?.Invoke(token);
            GlobalRequestStarted?.Invoke(token);
            GlobalActiveRequestCountChanged?.Invoke(globalActiveRequestCount);
            return token;
        }

        protected void FinishTrackedRequest(RequestToken token, bool success, NpcAiDecision decision = null, string error = null)
        {
            if (token == null || !token.IsActive)
            {
                return;
            }

            token.IsActive = false;
            globalActiveRequestCount = Mathf.Max(0, globalActiveRequestCount - 1);

            RequestFinishedEvent finishedEvent = new RequestFinishedEvent(token, success, decision, error);
            RequestFinished?.Invoke(finishedEvent);
            GlobalRequestFinished?.Invoke(finishedEvent);
            GlobalActiveRequestCountChanged?.Invoke(globalActiveRequestCount);
        }
    }
}
