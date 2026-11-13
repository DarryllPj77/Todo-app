using System.Collections;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TodoApp.Domain.Entities;

[JsonConverter(typeof(TagSetJsonConverter))]
public sealed class TagSet : IReadOnlyList<string>, IEquatable<TagSet>
{
    public const int MaxCount = 10;
    public const int MaxNameLength = 32;
    private readonly string[] names;
    public static TagSet Empty { get; } = new([]);
    private TagSet(string[] names) => this.names = names;
    public int Count => names.Length;
    public string this[int index] => names[index];
    public static TagSet FromNames(IEnumerable<string>? values)
    {
        if (values is null) return Empty;
        var normalized = new SortedSet<string>(StringComparer.Ordinal);
        var count = 0;
        foreach (var value in values)
        {
            if (++count > MaxCount) throw new ArgumentException("Submit at most 10 tags per task or tag rule.");
            var name = value?.Trim().Normalize(NormalizationForm.FormC).ToLowerInvariant();
            if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength || name.Contains(',') || name.Any(char.IsControl))
                throw new ArgumentException("Each tag needs 1-32 characters, without commas or control characters.");
            normalized.Add(name);
        }
        return normalized.Count == 0 ? Empty : new(normalized.ToArray());
    }
    public static TagSet Parse(string? csv) => FromNames((csv ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    public override string ToString() => string.Join(", ", names);
    public bool Equals(TagSet? other) => other is not null && names.SequenceEqual(other.names, StringComparer.Ordinal);
    public override bool Equals(object? obj) => obj is TagSet other && Equals(other);
    public override int GetHashCode() { var hash = new HashCode(); foreach (var name in names) hash.Add(name, StringComparer.Ordinal); return hash.ToHashCode(); }
    public IEnumerator<string> GetEnumerator() => ((IEnumerable<string>)names).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class TagSetJsonConverter : JsonConverter<TagSet>
{
    public override TagSet Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() > TagSet.MaxCount)
            throw new JsonException("Tags must be an array of at most 10 names.");
        var names = new List<string>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) throw new JsonException("Every tag must be a string.");
            names.Add(item.GetString()!);
        }
        try { return TagSet.FromNames(names); }
        catch (ArgumentException error) { throw new JsonException(error.Message); }
    }
    public override void Write(Utf8JsonWriter writer, TagSet value, JsonSerializerOptions options)
    { writer.WriteStartArray(); foreach (var name in value) writer.WriteStringValue(name); writer.WriteEndArray(); }
}
