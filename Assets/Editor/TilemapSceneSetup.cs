using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CityStateSim.EditorTools
{
    public static class TilemapSceneSetup
    {
        [MenuItem("City State Sim/Map/Create Basic Tilemap Layers")]
        public static void CreateBasicTilemapLayers()
        {
            GameObject gridObject = GameObject.Find("Grid");
            if (gridObject == null)
            {
                gridObject = new GameObject("Grid");
                gridObject.AddComponent<Grid>();
                Undo.RegisterCreatedObjectUndo(gridObject, "Create Grid");
            }

            CreateTilemapChild(gridObject.transform, "Ground Tilemap", 0, false);
            CreateTilemapChild(gridObject.transform, "Decoration Tilemap", 10, false);
            CreateTilemapChild(gridObject.transform, "Collision Tilemap", 20, true);

            Selection.activeGameObject = gridObject;
        }

        private static void CreateTilemapChild(Transform parent, string name, int sortingOrder, bool collision)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                return;
            }

            GameObject tilemapObject = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(tilemapObject, $"Create {name}");
            tilemapObject.transform.SetParent(parent);
            tilemapObject.transform.localPosition = Vector3.zero;
            tilemapObject.transform.localRotation = Quaternion.identity;
            tilemapObject.transform.localScale = Vector3.one;

            tilemapObject.AddComponent<Tilemap>();
            TilemapRenderer renderer = tilemapObject.AddComponent<TilemapRenderer>();
            renderer.sortingOrder = sortingOrder;

            if (collision)
            {
                Rigidbody2D body = tilemapObject.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Static;

                TilemapCollider2D tilemapCollider = tilemapObject.AddComponent<TilemapCollider2D>();
                tilemapCollider.usedByComposite = true;

                CompositeCollider2D compositeCollider = tilemapObject.AddComponent<CompositeCollider2D>();
                compositeCollider.geometryType = CompositeCollider2D.GeometryType.Polygons;
            }
        }
    }
}
