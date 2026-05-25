using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public class UIStateManager : MonoBehaviour
{
    // ── Data ──────────────────────────────────────────────────────────────

    [Serializable]
    public class VisibilityRule
    {
        public GameObject target;
        public bool active;
    }

    [Serializable]
    public class SliderTrigger
    {
        [Tooltip("When the slider reaches this position…")]
        public MapBottomSlider.SliderState onSliderState;
        [Tooltip("…switch to this CoreState (index into UIStateManager.coreStates)")]
        public int targetCoreState;
        [Tooltip("…and this SubState (-1 = use that CoreState's default)")]
        public int targetSubState = -1;
    }

    [Serializable]
    public class SubState
    {
        public string name;
        [Tooltip("Lowest slider position the user can snap to in this sub state")]
        public MapBottomSlider.SliderState sliderMin = MapBottomSlider.SliderState.Hidden;
        [Tooltip("Highest slider position the user can snap to in this sub state")]
        public MapBottomSlider.SliderState sliderMax = MapBottomSlider.SliderState.Full;
        [Tooltip("Snap slider to this position when this sub state activates")]
        public MapBottomSlider.SliderState sliderOnEnter = MapBottomSlider.SliderState.Bottom;
        [Tooltip("Disable dragging and hide the drag handle in this sub state")]
        public bool disableDrag = false;
        [Tooltip("Snap the map to minimum zoom (fully zoomed out) when this sub state activates")]
        public bool snapToMinZoom = false;
        [Tooltip("Slider position triggers that switch to another state")]
        public SliderTrigger[] sliderTriggers;
        [Tooltip("GameObjects to show or hide when this sub state is active")]
        public VisibilityRule[] visibilityRules;
    }

    [Serializable]
    public class CoreState
    {
        public string name;
        [Tooltip("The main panel/window for this state — shown when active, hidden otherwise")]
        public GameObject mainWindow;
        [Tooltip("Index of the sub state to use when this CoreState is first entered")]
        public int defaultSubState = 0;
        public SubState[] subStates;
    }

    // ── Inspector ─────────────────────────────────────────────────────────

    [Header("Default Visibility")]
    [Tooltip("Applied before every SubState — defines the baseline. SubState rules override on top.")]
    public VisibilityRule[] defaultVisibilityRules;

    [Header("States")]
    public CoreState[] coreStates;
    [Tooltip("CoreState index to activate on Start")]
    public int initialCoreState = 0;

    [Header("Journey Integration")]
    [Tooltip("CoreState index that contains journey sub states")]
    public int journeyCoreState = 0;
    [Tooltip("SubState index to activate when a journey is selected")]
    public int journeyActiveSubState = 1;
    [Tooltip("SubState index to return to when a journey is deselected")]
    public int journeyDefaultSubState = 0;
    [Tooltip("SubState index for journey creation mode")]
    public int createJourneySubState = 2;

    // ── Public helpers ────────────────────────────────────────────────────

    public void ActivateCreateJourneySubState()
    {
        JourneyManager.instance?.BeginCreatingJourney();
        ActivateSubState(createJourneySubState);
    }

    public void ActivateEditJourneySubState()
    {
        // Journey editing already set up via BeginEditingJourney — just show the creation panel
        ActivateSubState(createJourneySubState);
    }

    // ── Runtime ───────────────────────────────────────────────────────────

    public static UIStateManager instance { get; private set; }

    public int CurrentCoreIndex { get; private set; } = -1;
    public int CurrentSubIndex  { get; private set; } = -1;

    public CoreState CurrentCore => CurrentCoreIndex >= 0 && CurrentCoreIndex < coreStates.Length
        ? coreStates[CurrentCoreIndex] : null;
    public SubState CurrentSub => CurrentCore != null && CurrentSubIndex >= 0 && CurrentSubIndex < CurrentCore.subStates.Length
        ? CurrentCore.subStates[CurrentSubIndex] : null;

    private readonly List<NavTabButton> _navButtons = new List<NavTabButton>();

    // ── Lifecycle ─────────────────────────────────────────────────────────

    private void Awake() => instance = this;

    private void Start()
    {
        ActivateCoreState(initialCoreState);
    }

    private void OnEnable()
    {
        MapBottomSlider.onStateChanged      += OnSliderStateChanged;
        JourneyManager.onJourneyActivated   += OnJourneyActivated;
        JourneyManager.onJourneyDeactivated += OnJourneyDeactivated;
    }

    private void OnDisable()
    {
        MapBottomSlider.onStateChanged      -= OnSliderStateChanged;
        JourneyManager.onJourneyActivated   -= OnJourneyActivated;
        JourneyManager.onJourneyDeactivated -= OnJourneyDeactivated;
    }

    // ── Public API ────────────────────────────────────────────────────────

    public void RegisterNavButton(NavTabButton btn) => _navButtons.Add(btn);
    public void UnregisterNavButton(NavTabButton btn) => _navButtons.Remove(btn);

    /// Activate a CoreState, optionally specifying which SubState to start in.
    public void ActivateCoreState(int coreIndex, int subIndex = -1)
    {
        if (coreIndex < 0 || coreIndex >= coreStates.Length) return;

        // Hide all main windows, show the new one
        for (int i = 0; i < coreStates.Length; i++)
            if (coreStates[i].mainWindow != null)
                coreStates[i].mainWindow.SetActive(i == coreIndex);

        CurrentCoreIndex = coreIndex;
        CurrentSubIndex  = -1;

        MapBottomSlider.instance?.ClearMaxState();

        int sub = subIndex >= 0 ? subIndex : coreStates[coreIndex].defaultSubState;
        ActivateSubState(sub);

        RefreshNavButtons();
    }

    /// Activate a SubState within the current CoreState.
    public void ActivateSubState(int subIndex)
    {
        if (CurrentCoreIndex == journeyCoreState)
        {
            if (JourneyManager.instance?.isCreatingJourney == true)
                subIndex = createJourneySubState;
            else if (JourneyManager.instance?.activeJourney != null)
                subIndex = journeyActiveSubState;
        }

        var core = CurrentCore;
        if (core == null || subIndex < 0 || subIndex >= core.subStates.Length) return;

        CurrentSubIndex = subIndex;
        var sub = core.subStates[subIndex];

        // Reset to baseline, then apply SubState overrides
        if (defaultVisibilityRules != null)
            foreach (var rule in defaultVisibilityRules)
                if (rule.target != null)
                    rule.target.SetActive(rule.active);

        if (sub.visibilityRules != null)
            foreach (var rule in sub.visibilityRules)
                if (rule.target != null)
                    rule.target.SetActive(rule.active);

        // Apply slider constraints and snap
        if (MapBottomSlider.instance != null)
        {
            MapBottomSlider.instance.SetDragEnabled(!sub.disableDrag);
            MapBottomSlider.instance.SetMaxState(sub.sliderMax);
            MapBottomSlider.instance.SetState(sub.sliderOnEnter);
        }

        if (sub.snapToMinZoom && MapInputController.instance != null)
            MapInputController.instance.SetTargetZoom(MapInputController.instance.minScale);
    }

    // ── Slider triggers ───────────────────────────────────────────────────

    private void OnSliderStateChanged(MapBottomSlider.SliderState state)
    {
        var sub = CurrentSub;
        if (sub?.sliderTriggers == null) return;

        foreach (var trigger in sub.sliderTriggers)
        {
            if (trigger.onSliderState != state) continue;

            int targetCore = trigger.targetCoreState;
            int targetSub  = trigger.targetSubState;

            if (targetCore == CurrentCoreIndex)
                ActivateSubState(targetSub >= 0 ? targetSub : CurrentCore.defaultSubState);
            else
                ActivateCoreState(targetCore, targetSub);

            break;
        }
    }

    // ── Journey events ────────────────────────────────────────────────────

    private void OnJourneyActivated()
    {
        if (CurrentCoreIndex != journeyCoreState)
            ActivateCoreState(journeyCoreState, journeyActiveSubState);
        else
            ActivateSubState(journeyActiveSubState);
    }

    private void OnJourneyDeactivated()
    {
        if (CurrentCoreIndex == journeyCoreState)
            ActivateSubState(journeyDefaultSubState);
    }

    // ── Nav buttons ───────────────────────────────────────────────────────

    private void RefreshNavButtons()
    {
        foreach (var btn in _navButtons)
            btn.UpdateSelected(CurrentCoreIndex);
    }

#if UNITY_EDITOR
    [Button("Log Current State")]
    private void LogCurrentState()
    {
        Debug.Log($"[UIStateManager] Core: {CurrentCore?.name ?? "none"} ({CurrentCoreIndex})  Sub: {CurrentSub?.name ?? "none"} ({CurrentSubIndex})");
    }
#endif
}
