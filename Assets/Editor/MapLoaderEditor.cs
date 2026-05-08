using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MapLoader))]
public class MapLoaderEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        MapLoader loader = (MapLoader)target;
        if (loader.mapStyles == null || loader.mapStyles.Count == 0) return;

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Style Purchases", EditorStyles.boldLabel);

        for (int i = 0; i < loader.mapStyles.Count; i++)
        {
            var style = loader.mapStyles[i];
            if (style == null || !style.isLocked) continue;

            bool unlocked = loader.IsStyleUnlocked(i);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"[{i}] {style.name}", GUILayout.ExpandWidth(true));

            GUI.enabled = unlocked;
            if (GUILayout.Button("Undo Purchase", GUILayout.Width(110)))
            {
                loader.LockStyle(i);
                EditorUtility.SetDirty(loader);
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
        }
    }
}
