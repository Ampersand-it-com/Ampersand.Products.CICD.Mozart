namespace Ampersand.Mozart.WEB.Services;

public static class Formatters
{
    public static string Bytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }

    public static string Duration(TimeSpan? span)
    {
        if (span is null)
        {
            return "—";
        }

        return span.Value.TotalHours >= 1
            ? $"{(int)span.Value.TotalHours}h {span.Value.Minutes:00}m {span.Value.Seconds:00}s"
            : span.Value.TotalMinutes >= 1
                ? $"{span.Value.Minutes}m {span.Value.Seconds:00}s"
                : $"{span.Value.TotalSeconds:0.0}s";
    }

    public static string Relative(DateTimeOffset? utc)
    {
        if (utc is null)
        {
            return "never";
        }

        var delta = DateTimeOffset.UtcNow - utc.Value;
        if (delta.TotalSeconds < 20)
        {
            return "just now";
        }

        if (delta.TotalMinutes < 1)
        {
            return $"{(int)delta.TotalSeconds}s ago";
        }

        if (delta.TotalHours < 1)
        {
            return $"{(int)delta.TotalMinutes}m ago";
        }

        if (delta.TotalDays < 1)
        {
            return $"{(int)delta.TotalHours}h ago";
        }

        return utc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }
}
