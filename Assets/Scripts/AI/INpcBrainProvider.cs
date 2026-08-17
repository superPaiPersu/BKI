using System;

namespace CityStateSim.AI
{
    public interface INpcBrainProvider
    {
        void RequestDecision(NpcAiRequest request, Action<NpcAiDecision> onSuccess, Action<string> onError);
    }
}
