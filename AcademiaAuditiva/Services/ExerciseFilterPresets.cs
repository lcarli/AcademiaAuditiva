using System.Globalization;
using AcademiaAuditiva.Models;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.Services;

/// <summary>
/// Filter presets pin some of an exercise's filter selects to fixed values,
/// e.g. a routine item that always practises intervals in D minor.
///
/// A preset is a flat map from a filter group name to one of that group's
/// option values (<c>{"keySelect":"D4","scaleTypeSelect":"minor"}</c>), the
/// same shape the exercise pages post to <c>RequestPlay</c>. It is stored as
/// JSON in <c>RoutineItem.FilterJson</c> and
/// <c>RoutineAssignmentOverride.OverrideFilterJson</c>, and travels to the
/// exercise page as query-string parameters, where it preselects the filters.
/// </summary>
public static class ExerciseFilterPresets
{
    /// <summary>Reads the filter groups declared in <c>Exercise.FiltersJson</c>.</summary>
    public static IReadOnlyList<FilterOptionGroup> Groups(string? exerciseFiltersJson)
    {
        if (string.IsNullOrWhiteSpace(exerciseFiltersJson)) return Array.Empty<FilterOptionGroup>();
        try
        {
            var groups = JsonConvert.DeserializeObject<List<FilterOptionGroup>>(exerciseFiltersJson);
            return groups?
                .Where(g => !string.IsNullOrEmpty(g?.Name) && g.Options is { Count: > 0 })
                .ToList() ?? new List<FilterOptionGroup>();
        }
        catch (JsonException)
        {
            return Array.Empty<FilterOptionGroup>();
        }
    }

    /// <summary>
    /// Parses a stored preset. Anything that is not a flat JSON object of
    /// string (or integer) values is ignored rather than thrown, because older
    /// rows were typed by hand.
    /// </summary>
    public static Dictionary<string, string> Parse(string? json)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) return result;

        JToken token;
        try
        {
            token = JToken.Parse(json);
        }
        catch (JsonException)
        {
            return result;
        }

        if (token is not JObject obj) return result;
        foreach (var prop in obj.Properties())
        {
            if (prop.Value is JValue { Type: JTokenType.String or JTokenType.Integer } value)
            {
                var text = value.ToString(CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(text)) result[prop.Name] = text;
            }
        }
        return result;
    }

    /// <summary>Serializes a preset with sorted keys, or null when it is empty.</summary>
    public static string? Serialize(IReadOnlyDictionary<string, string>? filters)
    {
        if (filters is null || filters.Count == 0) return null;
        return JsonConvert.SerializeObject(new SortedDictionary<string, string>(
            filters.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
            StringComparer.Ordinal));
    }

    /// <summary>
    /// Keeps only known group names whose value is one of the group's options;
    /// empty values ("let the student choose") are dropped.
    /// </summary>
    public static Dictionary<string, string> Sanitize(
        IEnumerable<KeyValuePair<string, string?>>? values,
        IReadOnlyList<FilterOptionGroup> groups)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (values is null) return result;

        foreach (var (key, value) in values)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value)) continue;
            var group = groups.FirstOrDefault(g => g.Name == key);
            if (group?.Options?.Any(o => o.Value == value) == true)
            {
                result[key] = value;
            }
        }
        return result;
    }

    /// <summary>
    /// The preset an answer is saved with (<c>ScoreSnapshot.FilterJson</c>): the values of the
    /// exercise's own filters the round was played with, so settings such as the instrument are
    /// left out. Null when there are none, or when they would not fit the column, so a long
    /// preset never costs the answer.
    /// </summary>
    public static string? ForAnswer(IReadOnlyDictionary<string, string>? played, string? exerciseFiltersJson)
    {
        var preset = Sanitize(
            played?.Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value)),
            Groups(exerciseFiltersJson));
        var json = Serialize(preset);
        return json is { Length: <= ScoreSnapshot.FilterJsonMaxLength } ? json : null;
    }

    /// <summary>Reads a preset from the exercise page's query string.</summary>
    public static Dictionary<string, string> FromQuery(IQueryCollection query, IReadOnlyList<FilterOptionGroup> groups)
        => Sanitize(query.Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value.ToString())), groups);

    /// <summary>Applies <paramref name="overrides"/> on top of <paramref name="baseFilters"/>; override keys win.</summary>
    public static Dictionary<string, string> Merge(
        IReadOnlyDictionary<string, string> baseFilters,
        IReadOnlyDictionary<string, string>? overrides)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in baseFilters) result[key] = value;
        if (overrides is not null)
        {
            foreach (var (key, value) in overrides) result[key] = value;
        }
        return result;
    }

    /// <summary>
    /// Pairs each preset value with its group and option, in the exercise's
    /// group order, so views can show localized "Key: D" summaries.
    /// </summary>
    public static IReadOnlyList<AppliedFilter> Describe(
        IReadOnlyList<FilterOptionGroup> groups,
        IReadOnlyDictionary<string, string> values)
    {
        var applied = new List<AppliedFilter>();
        foreach (var group in groups)
        {
            if (!values.TryGetValue(group.Name, out var value)) continue;
            var option = group.Options.FirstOrDefault(o => o.Value == value);
            if (option is not null) applied.Add(new AppliedFilter(group, option));
        }
        return applied;
    }
}

public sealed record AppliedFilter(FilterOptionGroup Group, FilterOption Option);
