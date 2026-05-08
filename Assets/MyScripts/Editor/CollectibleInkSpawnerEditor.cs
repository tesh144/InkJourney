using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CollectibleInkSpawner))]
public class CollectibleInkSpawnerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        if (GUILayout.Button("Refresh Collectibles"))
            ((CollectibleInkSpawner)target).RefreshCollectibles();
    }
}
