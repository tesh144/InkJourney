using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(GPSManager))]
public class GPSManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        GPSManager gps = (GPSManager)target;

        DrawPropertiesExcluding(serializedObject, "mockLocationIndex");

        if (gps.mockLocations != null && gps.mockLocations.Length > 0)
        {
            string[] names = new string[gps.mockLocations.Length];
            for (int i = 0; i < gps.mockLocations.Length; i++)
                names[i] = gps.mockLocations[i].name ?? $"Location {i}";

            int newIndex = EditorGUILayout.Popup("Mock Location", gps.mockLocationIndex, names);
            if (newIndex != gps.mockLocationIndex)
            {
                Undo.RecordObject(gps, "Change Mock Location");
                gps.mockLocationIndex = newIndex;
                EditorUtility.SetDirty(gps);
            }
        }

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Editor Simulation Controls", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            EditorGUILayout.LabelField("Live Position", $"{gps.latitude:F6}, {gps.longitude:F6}");

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("North +5m")) gps.NudgeEditorMeters(5f, 0f);
            if (GUILayout.Button("South -5m")) gps.NudgeEditorMeters(-5f, 0f);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("West -5m")) gps.NudgeEditorMeters(0f, -5f);
            if (GUILayout.Button("East +5m")) gps.NudgeEditorMeters(0f, 5f);
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Set To Selected Mock Location"))
            {
                if (gps.mockLocations != null && gps.mockLocations.Length > 0)
                {
                    int idx = Mathf.Clamp(gps.mockLocationIndex, 0, gps.mockLocations.Length - 1);
                    gps.SetEditorLocation(gps.mockLocations[idx].latitude, gps.mockLocations[idx].longitude);
                }
            }
        }

        if (!Application.isPlaying)
            EditorGUILayout.HelpBox("Enter Play Mode to use simulation buttons.", MessageType.Info);
    }
}
