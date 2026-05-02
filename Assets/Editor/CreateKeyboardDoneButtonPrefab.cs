#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEditor;

public static class CreateKeyboardDoneButtonPrefab
{
    [MenuItem("InkJourney/Create Keyboard Done Button Prefab")]
    static void Create()
    {
        // --- Root button object ---
        var root = new GameObject("KeyboardDoneButton");
        var rt = root.AddComponent<RectTransform>();
        rt.sizeDelta        = new Vector2(100f, 36f);
        rt.anchorMin        = new Vector2(0.5f, 0f);
        rt.anchorMax        = new Vector2(0.5f, 0f);
        rt.pivot            = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 8f); // resting pos; script overrides this

        var bg  = root.AddComponent<Image>();
        bg.color = new Color(0.15f, 0.15f, 0.15f, 0.85f);

        var btn = root.AddComponent<Button>();
        btn.targetGraphic = bg;

        var kdb = root.AddComponent<KeyboardDoneButton>();
        kdb.buttonRect = rt;
        // canvas ref must be set in Inspector after placing in scene

        // --- Label ---
        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(root.transform, false);
        var labelRT        = labelGO.AddComponent<RectTransform>();
        labelRT.anchorMin  = Vector2.zero;
        labelRT.anchorMax  = Vector2.one;
        labelRT.offsetMin  = Vector2.zero;
        labelRT.offsetMax  = Vector2.zero;

        var tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text      = "Done";
        tmp.fontSize  = 16f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = Color.white;

        // --- Save prefab ---
        const string savePath = "Assets/InkJourney_Assets/UI_Assets/KeyboardDoneButton.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, savePath);
        Object.DestroyImmediate(root);

        AssetDatabase.Refresh();
        Selection.activeObject = prefab;
        Debug.Log($"[InkJourney] Saved prefab → {savePath}");
    }
}
#endif
