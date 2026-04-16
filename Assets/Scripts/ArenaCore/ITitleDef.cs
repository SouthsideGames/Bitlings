// Assets/Scripts/ArenaCore/ITitleDef.cs
// Shared interface for title data — used by both client and server.

/// <summary>
/// Read-only title definition consumed by arena logic.
/// Client: implemented by a wrapper around TitleSO.
/// Server: implemented by a JSON-deserialized catalog entry.
/// </summary>
public interface ITitleDef
{
    string TitleId { get; }
    string DisplayName { get; }
    int ArenaScore { get; }
}
