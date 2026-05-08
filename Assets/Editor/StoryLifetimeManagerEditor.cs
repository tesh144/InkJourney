using UnityEditor;
using UnityEngine;
using Firebase.Firestore;
using System.Collections.Generic;

[CustomEditor(typeof(StoryLifetimeManager))]
public class StoryLifetimeManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(4);

        if (GUILayout.Button("Upload to Firestore", GUILayout.Height(28)))
            UploadConfig((StoryLifetimeManager)target);
    }

    static void UploadConfig(StoryLifetimeManager mgr)
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Upload Config",
                "Enter Play Mode first so Firebase is initialised.", "OK");
            return;
        }

        var db = FirebaseFirestore.DefaultInstance;
        if (db == null)
        {
            Debug.LogError("[StoryLifetimeManager] Firestore not ready.");
            return;
        }

        var data = new Dictionary<string, object>
        {
            { "InitialLifetimeDays",   mgr.initialLifetimeDays   },
            { "CommentExtensionHours", mgr.commentExtensionHours },
            { "SaveExtensionHours",    mgr.saveExtensionHours    },
            { "ViewExtensionHours",    mgr.viewExtensionHours    },
            { "LikeExtensionHours",    mgr.likeExtensionHours    },
            { "BoostExtensionHours",   mgr.boostExtensionHours   },
            { "BoostDailyLimit",       mgr.boostDailyLimit       },
        };

        db.Collection("Config").Document("StoryLifetime")
          .SetAsync(data)
          .ContinueWith(task =>
          {
              if (task.IsFaulted)
                  Debug.LogError($"[StoryLifetimeManager] Upload failed: {task.Exception}");
              else
                  Debug.Log("[StoryLifetimeManager] Config uploaded to Firestore.");
          });
    }
}
