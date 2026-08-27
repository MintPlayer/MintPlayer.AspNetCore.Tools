namespace MintPlayer.Timestamps;

/// <summary>
/// Convenience combination of all three timestamps — created, last changed, soft-deleted — for
/// entities that carry the full set. Implementing it is equivalent to implementing
/// <see cref="IInsertTimestamp"/>, <see cref="IUpdateTimestamp"/> and
/// <see cref="IDeleteTimestamp"/> individually; it adds no members of its own.
/// </summary>
public interface ITimestamps : IInsertTimestamp, IUpdateTimestamp, IDeleteTimestamp
{
}
