using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace CityStateSim.EditorTools
{
    [InitializeOnLoad]
    public static class PlayModeToolGuard
    {
        private const BindingFlags StaticBindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        static PlayModeToolGuard()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode)
            {
                EditorApplication.delayCall += SwitchAwayFromSceneEditingTool;
            }
        }

        private static void SwitchAwayFromSceneEditingTool()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying)
            {
                return;
            }

            Type activeToolType = GetActiveEditorToolType();
            if (!ShouldSwitchTool(activeToolType))
            {
                return;
            }

            Tools.current = Tool.Move;
            TrySetActiveMoveEditorTool();
            SceneView.RepaintAll();
        }

        private static bool ShouldSwitchTool(Type activeToolType)
        {
            if (IsTilemapTool(activeToolType))
            {
                return true;
            }

            string currentToolName = Tools.current.ToString();
            return currentToolName.Equals("Custom", StringComparison.OrdinalIgnoreCase)
                || currentToolName.Equals("None", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTilemapTool(Type toolType)
        {
            if (toolType == null)
            {
                return false;
            }

            string fullName = toolType.FullName ?? toolType.Name;
            return fullName.IndexOf("Tilemap", StringComparison.OrdinalIgnoreCase) >= 0
                || fullName.IndexOf("UnityEditor.Tilemaps", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Type GetActiveEditorToolType()
        {
            Type toolManagerType = FindLoadedType("UnityEditor.EditorTools.ToolManager");
            PropertyInfo activeToolTypeProperty = toolManagerType?.GetProperty("activeToolType", StaticBindingFlags);
            return activeToolTypeProperty?.GetValue(null) as Type;
        }

        private static void TrySetActiveMoveEditorTool()
        {
            Type toolManagerType = FindLoadedType("UnityEditor.EditorTools.ToolManager");
            Type moveToolType = FindLoadedType("UnityEditor.EditorTools.MoveTool");
            if (toolManagerType == null || moveToolType == null)
            {
                return;
            }

            foreach (MethodInfo method in toolManagerType.GetMethods(StaticBindingFlags))
            {
                if (method.Name != "SetActiveTool")
                {
                    continue;
                }

                try
                {
                    if (method.IsGenericMethodDefinition && method.GetParameters().Length == 0)
                    {
                        method.MakeGenericMethod(moveToolType).Invoke(null, Array.Empty<object>());
                        return;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 1 && parameters[0].ParameterType == typeof(Type))
                    {
                        method.Invoke(null, new object[] { moveToolType });
                        return;
                    }
                }
                catch
                {
                    // Tools.current above is enough for older editor versions.
                }
            }
        }

        private static Type FindLoadedType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }
    }
}
