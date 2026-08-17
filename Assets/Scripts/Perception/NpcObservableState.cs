using System.Text;
using UnityEngine;

namespace CityStateSim.Perception
{
    public sealed class NpcObservableState : MonoBehaviour, IPerceivableDetailProvider
    {
        [Header("Body")]
        [SerializeField, Range(0, 100)] private int health = 80;
        [SerializeField, Range(0, 100)] private int energy = 70;
        [SerializeField, Range(0, 100)] private int cleanliness = 70;
        [SerializeField, Min(0f)] private float heightCm = 170f;
        [SerializeField, Range(0, 100)] private int bodyBuild = 50;
        [SerializeField, Range(0, 100)] private int charm = 50;

        [Header("Clothing")]
        [SerializeField] private string upperClothing = "plain top";
        [SerializeField] private string middleClothing = "plain trousers";
        [SerializeField] private string lowerClothing = "plain shoes";
        [SerializeField] private string clothingColorSummary = "neutral colors";
        [SerializeField, Min(0)] private int clothingEstimatedValue;

        [Header("Audible")]
        [SerializeField, Range(0, 100)] private int voiceVolume = 20;
        [SerializeField] private string audibleState;

        public int Health
        {
            get => health;
            set => health = Mathf.Clamp(value, 0, 100);
        }

        public int Energy
        {
            get => energy;
            set => energy = Mathf.Clamp(value, 0, 100);
        }

        public string BuildPerceptionDetail(PerceptionChannel channels)
        {
            StringBuilder builder = new StringBuilder();
            if ((channels & PerceptionChannel.Visual) != 0)
            {
                AppendPart(builder, $"health appears {DescribeLevel(health)} ({health}/100)");
                AppendPart(builder, $"energy appears {DescribeLevel(energy)} ({energy}/100)");
                AppendPart(builder, $"cleanliness {DescribeLevel(cleanliness)} ({cleanliness}/100)");
                AppendPart(builder, $"height about {heightCm:0} cm");
                AppendPart(builder, $"build {DescribeBuild(bodyBuild)}");
                AppendPart(builder, $"charm {DescribeLevel(charm)} ({charm}/100)");
                AppendPart(builder, $"clothes: upper={upperClothing}, middle={middleClothing}, lower={lowerClothing}, colors={clothingColorSummary}, estimatedValue={clothingEstimatedValue}");
            }

            if ((channels & PerceptionChannel.Audible) != 0)
            {
                AppendPart(builder, $"voice/noise level {voiceVolume}/100");
                AppendPart(builder, audibleState);
            }

            return builder.ToString();
        }

        private static string DescribeLevel(int value)
        {
            if (value >= 80)
            {
                return "high";
            }

            if (value >= 55)
            {
                return "normal";
            }

            if (value >= 30)
            {
                return "low";
            }

            return "critical";
        }

        private static string DescribeBuild(int value)
        {
            if (value >= 70)
            {
                return "heavy";
            }

            if (value <= 30)
            {
                return "slender";
            }

            return "average";
        }

        private static void AppendPart(StringBuilder builder, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.Append("; ");
            }

            builder.Append(text.Trim());
        }
    }
}
