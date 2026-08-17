using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CityStateSim.EditorTools
{
    public static class TileAssetGenerator
    {
        private const string SourceRoot = "Assets/Resources/map";
        private const string OutputRoot = "Assets/Tiles/Generated";

        [MenuItem("City State Sim/Map/Generate Tiles From Resources Map")]
        public static void GenerateTilesFromResourcesMap()
        {
            if (!AssetDatabase.IsValidFolder(SourceRoot))
            {
                Debug.LogWarning($"Tile source folder not found: {SourceRoot}");
                return;
            }

            EnsureFolder("Assets", "Tiles");
            EnsureFolder("Assets/Tiles", "Generated");

            string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { SourceRoot });
            int created = 0;
            int updated = 0;
            int skipped = 0;

            for (int i = 0; i < textureGuids.Length; i++)
            {
                string texturePath = AssetDatabase.GUIDToAssetPath(textureGuids[i]);
                if (ShouldSkipTexture(texturePath))
                {
                    skipped++;
                    continue;
                }

                Object[] assets = AssetDatabase.LoadAllAssetRepresentationsAtPath(texturePath);
                if (assets == null || assets.Length == 0)
                {
                    Sprite singleSprite = AssetDatabase.LoadAssetAtPath<Sprite>(texturePath);
                    if (singleSprite == null)
                    {
                        skipped++;
                        continue;
                    }

                    CreateOrUpdateTile(texturePath, singleSprite, ref created, ref updated);
                    continue;
                }

                bool foundSprite = false;
                for (int j = 0; j < assets.Length; j++)
                {
                    if (assets[j] is Sprite sprite)
                    {
                        foundSprite = true;
                        CreateOrUpdateTile(texturePath, sprite, ref created, ref updated);
                    }
                }

                if (!foundSprite)
                {
                    skipped++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Generated tile assets. Created: {created}, Updated: {updated}, Skipped textures: {skipped}");
        }

        private static void CreateOrUpdateTile(string texturePath, Sprite sprite, ref int created, ref int updated)
        {
            string relativeFolder = Path.GetDirectoryName(texturePath)
                .Replace("\\", "/")
                .Replace(SourceRoot, string.Empty)
                .Trim('/');

            string outputFolder = string.IsNullOrEmpty(relativeFolder)
                ? OutputRoot
                : $"{OutputRoot}/{relativeFolder}";

            EnsureFolderPath(outputFolder);

            string safeName = MakeSafeFileName(sprite.name);
            string existingPath = FindExistingTilePath(outputFolder, safeName);

            if (!string.IsNullOrEmpty(existingPath))
            {
                Tile existingTile = AssetDatabase.LoadAssetAtPath<Tile>(existingPath);
                if (existingTile != null)
                {
                    existingTile.sprite = sprite;
                    EditorUtility.SetDirty(existingTile);
                    updated++;
                    return;
                }
            }

            Tile tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            string tilePath = AssetDatabase.GenerateUniqueAssetPath($"{outputFolder}/{safeName}.asset");
            AssetDatabase.CreateAsset(tile, tilePath);
            created++;
        }

        private static string FindExistingTilePath(string outputFolder, string safeName)
        {
            string directPath = $"{outputFolder}/{safeName}.asset";
            if (File.Exists(directPath))
            {
                return directPath;
            }

            string[] guids = AssetDatabase.FindAssets($"{safeName} t:Tile", new[] { outputFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (Path.GetFileNameWithoutExtension(path) == safeName)
                {
                    return path;
                }
            }

            return string.Empty;
        }

        private static bool ShouldSkipTexture(string texturePath)
        {
            string fileName = Path.GetFileName(texturePath).ToLowerInvariant();
            return fileName.Contains("preview") || fileName.Contains("sample");
        }

        private static string MakeSafeFileName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '_');
            }

            return value;
        }

        private static void EnsureFolderPath(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string current = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                EnsureFolder(current, parts[i]);
                current = $"{current}/{parts[i]}";
            }
        }

        private static void EnsureFolder(string parent, string child)
        {
            string fullPath = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(fullPath))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }
    }
}
