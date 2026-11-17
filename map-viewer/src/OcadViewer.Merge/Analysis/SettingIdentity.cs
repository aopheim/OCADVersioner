using OcadViewer.Ocd;

namespace OcadViewer.Merge.Analysis;

/// <summary>Which parameter strings take part in a merge, and which string is which.</summary>
public static class SettingIdentity
{
    /// <summary>
    /// Strings OCAD rewrites as the user looks around - zoom, selection, recent
    /// files, display mode, view options. They are kept from base: merging them
    /// would turn every pan and zoom into a conflict.
    /// </summary>
    public static readonly IReadOnlySet<int> ViewStateTypes = new HashSet<int> {
        SettingType.Zoom, SettingType.Selection, SettingType.RecentFiles, SettingType.Display, SettingType.View,
    };

    /// <summary>
    /// Whether <paramref name="setting"/> is merged. Strings tied to an object entry
    /// are not: entries are not identities, so such a string cannot be carried over
    /// safely, and base's is kept.
    /// </summary>
    public static bool IsMerged(OcadSetting setting)
        => setting.IsMapWide && !ViewStateTypes.Contains(setting.Type);

    /// <summary>
    /// A key per string, unique within one file: the type, plus the colour number
    /// for strings that have one, else the first field and how many strings of the
    /// same type and first field came before it.
    /// </summary>
    public static IReadOnlyDictionary<string, OcadSetting> Keyed(IEnumerable<OcadSetting> settings)
    {
        Dictionary<string, OcadSetting> keyed = [];
        Dictionary<string, int> seen = [];

        foreach (OcadSetting setting in settings.Where(IsMerged)) {
            string number = setting.Field('n');
            string stem = number is not null ? $"set:{setting.Type}:n{number}" : $"set:{setting.Type}:{setting.FirstField}";
            int occurrence = seen.TryGetValue(stem, out int n) ? n + 1 : 0;
            seen[stem] = occurrence;
            keyed[occurrence == 0 ? stem : $"{stem}#{occurrence}"] = setting;
        }
        return keyed;
    }
}
