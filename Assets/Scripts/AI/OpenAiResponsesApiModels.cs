using System;

namespace CityStateSim.AI
{
    public static class AiModelDefaults
    {
        public const string FastModel = "gpt-5.6-terra";
        public const string LegacyFastModel = "gpt-5.4-mini";
        public const string LegacySlowModel = "gpt-5.5";
        public const string UnsupportedFastModel = "gpt-4o-mini";

        public static string ResolveRuntimeModel(string configuredModel)
        {
            if (string.IsNullOrWhiteSpace(configuredModel))
            {
                return FastModel;
            }

            string model = configuredModel.Trim();
            return string.Equals(model, LegacyFastModel, StringComparison.OrdinalIgnoreCase)
                || string.Equals(model, LegacySlowModel, StringComparison.OrdinalIgnoreCase)
                || string.Equals(model, UnsupportedFastModel, StringComparison.OrdinalIgnoreCase)
                ? FastModel
                : model;
        }
    }

    [Serializable]
    public sealed class OpenAiResponsesApiResponse
    {
        public OpenAiOutputItem[] output;
        public OpenAiResponseError error;
    }

    [Serializable]
    public sealed class OpenAiOutputItem
    {
        public string type;
        public OpenAiOutputContent[] content;
    }

    [Serializable]
    public sealed class OpenAiOutputContent
    {
        public string type;
        public string text;
    }

    [Serializable]
    public sealed class OpenAiResponseError
    {
        public string message;
        public string type;
        public string code;
    }
}
