using System.Text;
using UnityEngine;

namespace CityStateSim.Perception
{
    public sealed class BuildingObservableState : MonoBehaviour, IPerceivableDetailProvider
    {
        [SerializeField, Range(0, 100)] private int dirtiness;
        [SerializeField, Range(0, 100)] private int noisiness;
        [SerializeField] private string ownerActorId;
        [SerializeField, Min(0)] private int estimatedPrice;
        [SerializeField] private string currentUse;
        [SerializeField] private string visibleCondition;
        [SerializeField] private string audibleCondition;

        public string BuildPerceptionDetail(PerceptionChannel channels)
        {
            StringBuilder builder = new StringBuilder();
            if ((channels & PerceptionChannel.Visual) != 0)
            {
                AppendPart(builder, $"dirtiness {dirtiness}/100");
                AppendPart(builder, string.IsNullOrWhiteSpace(ownerActorId) ? "owner unknown" : $"ownerActorId={ownerActorId}");
                AppendPart(builder, $"estimatedPrice={estimatedPrice}");
                AppendPart(builder, currentUse);
                AppendPart(builder, visibleCondition);
            }

            if ((channels & PerceptionChannel.Audible) != 0)
            {
                AppendPart(builder, $"noisiness {noisiness}/100");
                AppendPart(builder, audibleCondition);
            }

            return builder.ToString();
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
