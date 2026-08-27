namespace MintPlayer.Timestamps;

/// <summary>
/// An entity that records when it was created.
/// </summary>
/// <remarks>
/// The timestamp is written once, by whatever persists the entity, and is not expected to change
/// afterwards. Consumers may treat it as immutable even though the setter is public — the setter
/// exists so a persistence layer (or a deserializer) can stamp the value, not so callers can
/// rewrite history.
/// </remarks>
public interface IInsertTimestamp
{
    /// <summary>When the entity was created. Set once, at insert time, by the persistence layer.</summary>
    DateTime DateInsert { get; set; }
}
