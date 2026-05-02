using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[System.Serializable]
public class StartupScenario
{
    public string scenarioId;
    public List<GameObject> enable  = new List<GameObject>();
    public List<GameObject> disable = new List<GameObject>();
}

public class StartupManager : MonoBehaviour
{
    public static string PendingScenario;
    public static StartupManager instance;

    [SerializeField] private string defaultScenarioId = "default";
    [SerializeField] private List<StartupScenario> scenarios = new List<StartupScenario>();

    private void Start()
    {
        instance = this;

        string target = !string.IsNullOrEmpty(PendingScenario) ? PendingScenario : defaultScenarioId;
        PendingScenario = null;

        Apply(target);
    }

    /// <summary>Applies PendingScenario if set. Call after Start() when a notification tap arrives.</summary>
    public void ApplyPending()
    {
        if (string.IsNullOrEmpty(PendingScenario)) return;
        string target = PendingScenario;
        PendingScenario = null;
        Apply(target);
    }

    private void Apply(string scenarioId)
    {
        StartupScenario scenario = scenarios.Find(s => s.scenarioId == scenarioId);

        if (scenario == null && scenarioId != defaultScenarioId)
            scenario = scenarios.Find(s => s.scenarioId == defaultScenarioId);

        if (scenario == null)
        {
            Debug.LogWarning($"[StartupManager] No scenario found for '{scenarioId}'");
            return;
        }

        foreach (var go in scenario.enable)
            if (go != null) go.SetActive(true);

        foreach (var go in scenario.disable)
            if (go != null) go.SetActive(false);
    }
}
