using UnityEngine;

public static class StoryDateFormatter
{
    public static string FormatAgo(long createdTimestamp)
    {
        if (createdTimestamp <= 0) return "";

        var elapsed = System.DateTime.UtcNow
            - System.DateTimeOffset.FromUnixTimeSeconds(createdTimestamp).UtcDateTime;

        if (elapsed.TotalMinutes < 60)
        {
            int mins = Mathf.Max(1, Mathf.FloorToInt((float)elapsed.TotalMinutes));
            return $"Posted: {mins}mins ago";
        }
        if (elapsed.TotalHours < 24)
            return $"Posted: {Mathf.FloorToInt((float)elapsed.TotalHours)}hrs ago";
        if (elapsed.TotalDays < 30)
            return $"Posted: {Mathf.FloorToInt((float)elapsed.TotalDays)}d ago";
        if (elapsed.TotalDays < 365)
            return $"Posted: {Mathf.FloorToInt((float)elapsed.TotalDays / 30)}mo ago";
        int yrs = Mathf.FloorToInt((float)elapsed.TotalDays / 365);
        return $"Posted: {yrs}yrs ago";
    }

    public static string FormatActive(long expireTimestamp)
    {
        if (expireTimestamp <= 0) return "Forever";

        var remaining = System.DateTimeOffset.FromUnixTimeSeconds(expireTimestamp).UtcDateTime
            - System.DateTime.UtcNow;

        if (remaining.TotalSeconds <= 0) return "Expired";

        double days = remaining.TotalDays;

        if (days >= 36500) return "Active forever";
        if (days >= 365)
        {
            int yrs = Mathf.FloorToInt((float)days / 365);
            return $"Active for {yrs}yr{(yrs > 1 ? "s" : "")}";
        }
        if (days >= 30)
        {
            int mo = Mathf.FloorToInt((float)days / 30);
            return $"Active for {mo} month{(mo > 1 ? "s" : "")}";
        }
        if (days >= 7)
        {
            int wk = Mathf.FloorToInt((float)days / 7);
            return $"Active for {wk} week{(wk > 1 ? "s" : "")}";
        }
        if (days >= 1)
        {
            int d = Mathf.FloorToInt((float)days);
            return $"Active for {d} day{(d > 1 ? "s" : "")}";
        }

        int h = Mathf.Max(1, Mathf.FloorToInt((float)remaining.TotalHours));
        return $"Active for {h}h";
    }
}
