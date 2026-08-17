using UnityEngine;

namespace CityStateSim.Movement
{
    public sealed class PlayerLayeredAppearanceController : MonoBehaviour
    {
        private const string DefaultShirtResourceRoot = "player/shirts/farmer";

        [Header("Renderers")]
        [SerializeField] private SpriteRenderer bodyRenderer;
        [SerializeField] private SpriteRenderer legsRenderer;
        [SerializeField] private SpriteRenderer handsRenderer;
        [SerializeField] private SpriteRenderer shirtRenderer;
        [SerializeField] private SpriteRenderer hatRenderer;

        [Header("Shirt")]
        [SerializeField] private string shirtResourceRoot = DefaultShirtResourceRoot;
        [SerializeField] private bool loadShirtSpritesFromResources = true;
        [SerializeField] private Sprite shirtForward;
        [SerializeField] private Sprite shirtBackward;
        [SerializeField] private Sprite shirtRight;
        [SerializeField] private Sprite shirtLeft;

        [Header("Layer Offsets")]
        [SerializeField] private Vector3 shirtForwardOffset = new Vector3(0f, 0.5f, 0f);
        [SerializeField] private Vector3 shirtBackwardOffset = new Vector3(0f, 0.5f, 0f);
        [SerializeField] private Vector3 shirtRightOffset = new Vector3(0f, 0.5f, 0f);

        public bool HasShirt => shirtRenderer != null;

        private Vector3 initialHatLocalPosition;
        private bool hasInitialHatLocalPosition;

        private void Awake()
        {
            ResolveRenderers();
            CaptureInitialOffsets();
            LoadShirtSprites();
            ApplyDirection(Direction8.South);
        }

        private void OnValidate()
        {
            ResolveRenderers();
        }

        public void ApplyDirection(Direction8 direction)
        {
            ResolveRenderers();
            bool mirrorFromRight = IsLeftFacing(direction);
            Direction8 spriteDirection = ResolveSpriteDirection(direction);

            ApplyFlip(bodyRenderer, mirrorFromRight);
            ApplyFlip(legsRenderer, mirrorFromRight);
            ApplyFlip(handsRenderer, mirrorFromRight);
            ApplyFlip(hatRenderer, mirrorFromRight);

            if (shirtRenderer != null)
            {
                shirtRenderer.flipX = mirrorFromRight && shirtLeft == null;
                shirtRenderer.sprite = ResolveShirtSprite(spriteDirection, mirrorFromRight);
                shirtRenderer.transform.localPosition = ResolveShirtOffset(spriteDirection, mirrorFromRight);
                shirtRenderer.enabled = shirtRenderer.sprite != null;
            }

            if (hatRenderer != null)
            {
                EnsureInitialHatLocalPosition();
                Vector3 hatPosition = initialHatLocalPosition;
                if (mirrorFromRight)
                {
                    hatPosition.x = -hatPosition.x;
                }

                hatRenderer.transform.localPosition = hatPosition;
            }
        }

        private void ResolveRenderers()
        {
            bodyRenderer ??= FindChildRenderer("body");
            legsRenderer ??= FindChildRenderer("legs");
            handsRenderer ??= FindChildRenderer("hands");
            shirtRenderer ??= FindChildRenderer("shirt");
            hatRenderer ??= FindChildRenderer("hat");
        }

        private void CaptureInitialOffsets()
        {
            if (hatRenderer != null)
            {
                initialHatLocalPosition = hatRenderer.transform.localPosition;
                hasInitialHatLocalPosition = true;
            }
        }

        private void EnsureInitialHatLocalPosition()
        {
            if (!hasInitialHatLocalPosition && hatRenderer != null)
            {
                initialHatLocalPosition = hatRenderer.transform.localPosition;
                hasInitialHatLocalPosition = true;
            }
        }

        private SpriteRenderer FindChildRenderer(string childName)
        {
            if (string.IsNullOrWhiteSpace(childName))
            {
                return null;
            }

            Transform[] children = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child != null && string.Equals(child.name, childName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return child.GetComponent<SpriteRenderer>();
                }
            }

            return null;
        }

        private void LoadShirtSprites()
        {
            if (!loadShirtSpritesFromResources)
            {
                return;
            }

            string root = string.IsNullOrWhiteSpace(shirtResourceRoot)
                ? DefaultShirtResourceRoot
                : shirtResourceRoot.Trim().Trim('/');
            shirtForward ??= Resources.Load<Sprite>($"{root}/forward");
            shirtBackward ??= Resources.Load<Sprite>($"{root}/backward");
            shirtRight ??= Resources.Load<Sprite>($"{root}/right");
            shirtLeft ??= Resources.Load<Sprite>($"{root}/left");
        }

        private static void ApplyFlip(SpriteRenderer renderer, bool flipX)
        {
            if (renderer != null)
            {
                renderer.flipX = flipX;
            }
        }

        private static bool IsLeftFacing(Direction8 direction)
        {
            return direction == Direction8.West
                || direction == Direction8.NorthWest
                || direction == Direction8.SouthWest;
        }

        private static Direction8 ResolveSpriteDirection(Direction8 direction)
        {
            return direction switch
            {
                Direction8.North or Direction8.NorthEast or Direction8.NorthWest => Direction8.North,
                Direction8.East or Direction8.West => Direction8.East,
                Direction8.South or Direction8.SouthEast or Direction8.SouthWest => Direction8.South,
                _ => Direction8.South
            };
        }

        private Sprite ResolveShirtSprite(Direction8 spriteDirection, bool mirrorFromRight)
        {
            if (mirrorFromRight && shirtLeft != null)
            {
                return shirtLeft;
            }

            return spriteDirection switch
            {
                Direction8.North => shirtForward,
                Direction8.East => shirtRight,
                _ => shirtBackward
            };
        }

        private Vector3 ResolveShirtOffset(Direction8 spriteDirection, bool mirrorFromRight)
        {
            Vector3 offset = spriteDirection switch
            {
                Direction8.North => shirtForwardOffset,
                Direction8.East => shirtRightOffset,
                _ => shirtBackwardOffset
            };

            if (mirrorFromRight)
            {
                offset.x = -offset.x;
            }

            return offset;
        }
    }
}
