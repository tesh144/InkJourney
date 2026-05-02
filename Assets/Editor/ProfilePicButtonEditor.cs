using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ProfilePicButton))]
public class ProfilePicButtonEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();

        if (GUILayout.Button("Apply Preview Image"))
            ApplyPreview((ProfilePicButton)target);

        if (GUILayout.Button("Apply All Preview Images"))
        {
            foreach (var btn in FindObjectsByType<ProfilePicButton>(FindObjectsSortMode.None))
                ApplyPreview(btn);
        }
    }

    private static void ApplyPreview(ProfilePicButton btn)
    {
        var manager = FindAnyObjectByType<UserProfileManager>();
        if (manager == null || manager.profilePics == null || manager.profilePics.Count == 0)
        {
            Debug.LogWarning("[ProfilePicButton] UserProfileManager not found in scene or has no sprites.");
            return;
        }

        var image = btn.previewImage;
        if (image == null) return;

        int id = Mathf.Clamp(btn.profilePicID, 0, manager.profilePics.Count - 1);
        Sprite sprite = manager.profilePics[id];

        Undo.RecordObject(image, "Apply Profile Pic Preview");
        image.sprite = sprite;
        image.enabled = sprite != null;
        EditorUtility.SetDirty(image);
    }
}
