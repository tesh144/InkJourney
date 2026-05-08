using UnityEngine;
using UnityEngine.UI;

public class ChangeStyleButton : MonoBehaviour
{
    [Tooltip("Which style index this button represents")]
    public int styleIndex;

    [Tooltip("The child indicator shown when this style is active")]
    public GameObject onIndicator;

    [Header("Shop Item")]
    [Tooltip("If true, this button is a shop item — shows owned marker when unlocked instead of disabling")]
    public bool isShopItem     = false;
    public bool requiresQuill  = false;
    public int  mapStylePrice  = 100;
    public int  quillPrice     = 1;
    public GameObject ownedMarker;

    Button _button;
    bool   _lastActiveState;

    void Awake() => _button = GetComponent<Button>();

    void OnEnable()
    {
        MapLoader.onStyleChanged  += RefreshState;
        MapLoader.onStyleUnlocked += RefreshState;
        RefreshState();
    }

    void OnDisable()
    {
        MapLoader.onStyleChanged  -= RefreshState;
        MapLoader.onStyleUnlocked -= RefreshState;
    }

    void RefreshState()
    {
        if (MapLoader.instance == null) return;

        bool unlocked = MapLoader.instance.IsStyleUnlocked(styleIndex);

        if (isShopItem)
        {
            if (ownedMarker != null) ownedMarker.SetActive(unlocked);
            if (_button != null) _button.interactable = !unlocked;
        }
        else
        {
            gameObject.SetActive(unlocked);
            if (_button != null) _button.interactable = true;
        }

        if (onIndicator != null)
        {
            bool active = !isShopItem && MapLoader.instance.currentStyleIndex == styleIndex;
            if (active != _lastActiveState)
            {
                onIndicator.SetActive(active);
                _lastActiveState = active;
            }
        }
    }

    public void OnClick()
    {
        if (MapLoader.instance == null) return;
        if (isShopItem) Purchase();
        else MapLoader.instance.ChooseStyle(styleIndex);
    }

    public void Purchase()
    {
        if (requiresQuill)
        {
            if (GoldenQuillManager.instance == null || !GoldenQuillManager.instance.CanAfford(quillPrice))
            {
                MapLoader.instance?.ShowPurchaseFeedback(false);
                return;
            }
            GoldenQuillManager.instance.RemoveQuill(quillPrice);
        }
        else
        {
            if (InkManager.instance == null || InkManager.instance.CurrentInk < mapStylePrice)
            {
                MapLoader.instance?.ShowPurchaseFeedback(false);
                return;
            }
            InkManager.instance.RemoveInk(mapStylePrice);
        }

        MapLoader.instance.UnlockStyle(styleIndex);
        MapLoader.instance?.ShowPurchaseFeedback(true);
    }
}
