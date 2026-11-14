namespace TodoApp.Maui.Models;

// Kind separates the virtual groups from categories literally named "All categories".
public sealed record WorkspaceCategory(string? Name, int Count, bool IsAll = false)
{
    public string Label => $"{(IsAll ? "All categories" : Name ?? "Uncategorized")} ({Count})";
    public bool Matches(TaskRow row) => IsAll || StringComparer.OrdinalIgnoreCase.Equals(Name, row.CategoryName);
}
