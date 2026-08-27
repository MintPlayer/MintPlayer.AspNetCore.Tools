namespace MintPlayer.Timestamps;

/// <summary>
/// An entity that is deleted by marking rather than removing — a soft delete.
/// </summary>
/// <remarks>
/// The timestamp is set by whatever performs the delete, and the row stays in place. Because
/// <see cref="DateDelete"/> is not nullable, "not deleted" is represented by the default
/// <see cref="DateTime"/> rather than by <see langword="null"/>, so callers filtering live
/// entities compare against <c>default</c> instead of testing for null.
/// </remarks>
public interface IDeleteTimestamp
{
    /// <summary>
    /// When the entity was soft-deleted, or the default <see cref="DateTime"/> while it is still
    /// live. Set by whatever performs the delete.
    /// </summary>
    DateTime DateDelete { get; set; }
}
