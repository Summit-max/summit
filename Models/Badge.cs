namespace Wallbang.Models;

public class Badge
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Rarity { get; set; } = "Common";

    // runtime-only flag set when composing view for a user
    public bool IsUnlocked { get; set; }
    public DateTime? UnlockedAt { get; set; }
}
