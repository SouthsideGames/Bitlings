// Assets/Scripts/ArenaCore/ITitleCatalog.cs
// Shared interface for title catalog access.

/// <summary>
/// Provides read-only access to the title catalog.
/// Client: wraps TitleManager.I.
/// Server: wraps a JSON-loaded catalog.
/// </summary>
public interface ITitleCatalog
{
    ITitleDef GetTitleById(string titleId);
}
