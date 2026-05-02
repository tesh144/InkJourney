using System.Globalization;
using UnityEngine;

public static class CompactCountFormatter
{
    public static string FormatLikes(int count)
    {
        int safe = Mathf.Max(0, count);
        if (safe < 1000)
            return safe.ToString(CultureInfo.InvariantCulture);

        float thousands = safe / 1000f;
        if (safe < 10000)
        {
            // Keep one decimal place for low-thousands (e.g. 1.2k, 9.8k)
            float truncated = Mathf.Floor(thousands * 10f) / 10f;
            return truncated.ToString("0.#", CultureInfo.InvariantCulture) + "k";
        }

        // For larger values, use whole thousands (e.g. 12k, 25k)
        return Mathf.FloorToInt(thousands).ToString(CultureInfo.InvariantCulture) + "k";
    }

    public static string FormatViews(int count)
    {
        return FormatLikes(count);
    }
}
