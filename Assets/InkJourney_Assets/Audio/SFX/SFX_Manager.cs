using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine.Events;
using UnityEngine;

[System.Serializable]
public class SFX
{
    public string name;
    public List<AudioClip> clips;
}
public class SFX_Manager : MonoBehaviour
{
    public List<SFX> sfx;
    public AudioSource source;
    public AudioSource musicSource;
    [ReadOnly] public static SFX_Manager instance;
    // Start is called before the first frame update

    private void Awake()
    {
        instance = this;
        if(PlayerPrefs.GetInt("audio") >= 1) eventOnDisableSFX.Invoke();
        else eventOnEnableSFX.Invoke();

        if (PlayerPrefs.GetInt("music") >= 1) eventOnDisableMusic.Invoke();
        else eventOnEnableMusic.Invoke();
    }
    public void PlaySFX_name_index(string name , int index)
    {
        foreach(SFX s in sfx)
        {
            if (s.name == name)
            source.PlayOneShot(s.clips[index]);
        }
    }

    public UnityEvent eventOnDisableSFX;
    public UnityEvent eventOnEnableSFX;

    public void DisableSFX()
    {
        PlayerPrefs.SetInt("audio", 1);
        eventOnDisableSFX.Invoke();
    }
    public void EnableSFX()
    {
        PlayerPrefs.SetInt("audio", 0);
        eventOnEnableSFX.Invoke();
    }

    [Space]
    public UnityEvent eventOnDisableMusic;
    public UnityEvent eventOnEnableMusic;
    public void DisableMusic()
    {
        PlayerPrefs.SetInt("music", 1);
        eventOnDisableMusic.Invoke();
    }
    public void EnableMusic()
    {
        PlayerPrefs.SetInt("music", 0);
        eventOnEnableMusic.Invoke();
    }

    public void PlaySFX_name_random(string name)
    {
        foreach (SFX s in sfx)
        {
            if (s.name == name)
            {
                int index = Random.Range(0, (s.clips.Count - 1));
                source.PlayOneShot(s.clips[index]);
            }
        }
    }
}
