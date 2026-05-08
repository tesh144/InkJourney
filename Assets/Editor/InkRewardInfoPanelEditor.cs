using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(InkRewardInfoPanel))]
public class InkRewardInfoPanelEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(4);

        if (GUILayout.Button("Refresh Text", GUILayout.Height(28)))
            ((InkRewardInfoPanel)target).Refresh();
    }
}
