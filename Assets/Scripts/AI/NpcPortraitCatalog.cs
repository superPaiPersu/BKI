using System.Collections.Generic;
using UnityEngine;

namespace CityStateSim.AI
{
    public static class NpcPortraitCatalog
    {
        public const string RootResourcesPath = "UI/Npc";
        public const string SharedResourcesPath = "UI/Npc/portraits";
        public const string LegacyResourcesPath = SharedResourcesPath;

        private static readonly Dictionary<string, string[]> cachedNamesByPath = new Dictionary<string, string[]>();

        public static string[] GetPortraitNames()
        {
            return GetPortraitNamesAtPath(SharedResourcesPath);
        }

        public static string[] GetPortraitNames(string npcId, string npcName)
        {
            List<string> names = new List<string>();
            string[] paths = BuildNpcPortraitPaths(npcId, npcName, includeShared: true);
            for (int i = 0; i < paths.Length; i++)
            {
                AddUnique(names, GetPortraitNamesAtPath(paths[i]));
            }

            return names.ToArray();
        }

        public static Sprite LoadPortrait(string portraitName)
        {
            if (string.IsNullOrWhiteSpace(portraitName))
            {
                return null;
            }

            return Resources.Load<Sprite>($"{SharedResourcesPath}/{portraitName.Trim()}");
        }

        public static Sprite LoadPortrait(string npcId, string npcName, string portraitName)
        {
            if (string.IsNullOrWhiteSpace(portraitName))
            {
                return null;
            }

            string trimmedPortraitName = portraitName.Trim();
            string[] paths = BuildNpcPortraitPaths(npcId, npcName, includeShared: true);
            for (int i = 0; i < paths.Length; i++)
            {
                Sprite sprite = Resources.Load<Sprite>($"{paths[i]}/{trimmedPortraitName}");
                if (sprite != null)
                {
                    return sprite;
                }
            }

            return null;
        }

        public static string GetFallbackPortraitName(string fallback = "neutral")
        {
            string[] names = GetPortraitNames();
            return GetFallbackPortraitName(names, fallback);
        }

        public static string GetFallbackPortraitName(string npcId, string npcName, string fallback = "neutral")
        {
            string[] names = GetPortraitNames(npcId, npcName);
            return GetFallbackPortraitName(names, fallback);
        }

        public static string DescribePortraitPath(string npcId, string npcName)
        {
            string primary = BuildNpcPortraitPath(NormalizeFolderName(npcId));
            string display = BuildNpcPortraitPath(NormalizeFolderName(npcName));
            if (!string.IsNullOrWhiteSpace(primary))
            {
                return primary;
            }

            if (!string.IsNullOrWhiteSpace(display))
            {
                return display;
            }

            return SharedResourcesPath;
        }

        private static string[] GetPortraitNamesAtPath(string resourcesPath)
        {
            if (string.IsNullOrWhiteSpace(resourcesPath))
            {
                return System.Array.Empty<string>();
            }

            if (cachedNamesByPath.TryGetValue(resourcesPath, out string[] cachedNames))
            {
                return cachedNames;
            }

            Sprite[] sprites = Resources.LoadAll<Sprite>(resourcesPath);
            List<string> names = new List<string>();
            for (int i = 0; i < sprites.Length; i++)
            {
                Sprite sprite = sprites[i];
                if (sprite != null && !string.IsNullOrWhiteSpace(sprite.name) && !names.Contains(sprite.name))
                {
                    names.Add(sprite.name);
                }
            }

            cachedNames = names.ToArray();
            cachedNamesByPath[resourcesPath] = cachedNames;
            return cachedNames;
        }

        private static string GetFallbackPortraitName(string[] names, string fallback)
        {
            if (names.Length == 0)
            {
                return fallback;
            }

            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], fallback, System.StringComparison.OrdinalIgnoreCase))
                {
                    return names[i];
                }
            }

            return names[0];
        }

        private static string[] BuildNpcPortraitPaths(string npcId, string npcName, bool includeShared)
        {
            List<string> paths = new List<string>();
            AddPathCandidate(paths, npcId);
            AddPathCandidate(paths, npcName);

            if (includeShared)
            {
                AddUnique(paths, SharedResourcesPath);
            }

            return paths.ToArray();
        }

        private static void AddPathCandidate(List<string> paths, string value)
        {
            string normalized = NormalizeFolderName(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            AddUnique(paths, BuildNpcPortraitPath(normalized));
            AddUnique(paths, BuildNpcPortraitPath(ToTitleCaseAscii(normalized)));
        }

        private static string BuildNpcPortraitPath(string folderName)
        {
            return string.IsNullOrWhiteSpace(folderName)
                ? string.Empty
                : $"{RootResourcesPath}/{folderName}/portraits";
        }

        private static string NormalizeFolderName(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string ToTitleCaseAscii(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            value = value.Trim();
            if (value.Length == 1)
            {
                return value.ToUpperInvariant();
            }

            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        private static void AddUnique(List<string> values, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], value, System.StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            values.Add(value);
        }

        private static void AddUnique(List<string> values, string[] newValues)
        {
            if (newValues == null)
            {
                return;
            }

            for (int i = 0; i < newValues.Length; i++)
            {
                AddUnique(values, newValues[i]);
            }
        }
    }
}
