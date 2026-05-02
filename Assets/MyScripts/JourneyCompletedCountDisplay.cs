using TMPro;
using UnityEngine;

public class JourneyCompletedCountDisplay : MonoBehaviour
{
    public TextMeshProUGUI countText;
    public string prefix = "✓ ";

    private void OnEnable()
    {
        JourneyManager.onJourneyActivated   += Refresh;
        JourneyManager.onJourneyDeactivated += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        JourneyManager.onJourneyActivated   -= Refresh;
        JourneyManager.onJourneyDeactivated -= Refresh;
    }

    public void Refresh()
    {
        if (countText == null) return;

        int total = 0;
        var journeys = GoogleSheetsFetcher.instance?.journeysList;

        if (journeys != null && JourneyManager.instance != null)
        {
            foreach (var journey in journeys)
            {
                if (journey == null || journey.Chapters == null || journey.Chapters.Count == 0) continue;
                if (JourneyManager.instance.GetProgressPercent(journey.ID) >= 1f)
                    total++;
            }
        }

        countText.text = prefix + total;
    }
}
