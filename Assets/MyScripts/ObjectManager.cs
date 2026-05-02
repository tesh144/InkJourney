using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObjectManager : MonoBehaviour
{
    public UI_StoryPanel storyPanel;
    public GameObject createStoryPanel;
    public GameObject loadingScreen;
    public GameObject warningScreen;

    [Tooltip("The write-new-post button GameObject — hidden when a story pointer is already within proximity")]
    public GameObject writePostButton;

    public static ObjectManager instance;
    private void Awake()
    {
        instance = this;
    }
}
