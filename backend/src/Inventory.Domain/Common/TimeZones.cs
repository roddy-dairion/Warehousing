namespace Inventory.Domain.Common;

/// <summary>
/// Finds a time zone by IANA name (Indian/Mauritius, Europe/Zurich) or Windows name (Mauritius Standard Time),
/// on any operating system.
/// </summary>
public static class TimeZones
{
    public static bool TryFind(string? id, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(id)) return false;
        if (TryLookup(id, out zone)) return true;
        // Convert between the IANA and Windows naming styles and try again.
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windowsId) && TryLookup(windowsId, out zone)) return true;
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var ianaId) && TryLookup(ianaId, out zone)) return true;
        return false;
    }

    /// <summary>The time zone, or UTC when the name is unknown (documents must still print).</summary>
    public static TimeZoneInfo FindOrUtc(string? id) => TryFind(id, out var zone) ? zone : TimeZoneInfo.Utc;

    private static bool TryLookup(string id, out TimeZoneInfo zone)
    {
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            zone = TimeZoneInfo.Utc;
            return false;
        }
    }
}
