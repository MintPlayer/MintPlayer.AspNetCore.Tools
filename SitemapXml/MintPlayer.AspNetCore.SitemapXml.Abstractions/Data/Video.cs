using System.Xml.Serialization;

namespace MintPlayer.AspNetCore.SitemapXml.Abstractions.Data;

/// <summary>
/// One <c>&lt;video:video&gt;</c> entry of Google's video sitemap extension, describing a single
/// video hosted on the containing <see cref="Url"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every member of this type is optional as far as this library is concerned: nothing is
/// validated, and any member left unset is omitted from the XML entirely — no empty element, no
/// <c>xsi:nil</c>. That is what the <c>ShouldSerialize*</c> methods below are for
/// (see <see cref="ShouldSerializeDuration"/> for the mechanism). The consumer of the sitemap
/// decides what it actually requires; Google, for instance, wants at least a title, a
/// description, a thumbnail, and either <see cref="ContentLocation"/> or
/// <see cref="PlayerLocation"/>, and will drop an entry that is missing them.
/// </para>
/// <para>
/// Members backed by nullable value types need the guard for a second reason: without it
/// <see cref="XmlSerializer"/> would write the underlying default (<c>0</c>, <c>false</c>, the
/// zero date), which a crawler reads as a real value rather than as "unknown".
/// </para>
/// </remarks>
[XmlType("video", Namespace = "http://www.google.com/schemas/sitemap-video/1.1")]
public class Video
{
    #region ThumbnailLocation
    /// <summary>
    /// URL of a thumbnail image for the video. Google requires one per video entry and expects an
    /// image of at least 160x90 pixels in a widely supported format.
    /// </summary>
    [XmlElement("thumbnail_loc", Namespace = "http://www.google.com/schemas/sitemap-video/1.1")]
    public string? ThumbnailLocation { get; set; }

    /// <inheritdoc cref="ShouldSerializeDuration"/>
    public bool ShouldSerializeThumbnailLocation() => ThumbnailLocation != null;
    #endregion
    #region Title
    /// <summary>
    /// Title of the video, as plain text. Should match the title shown on the page. Omitted when
    /// <see langword="null"/>.
    /// </summary>
    [XmlElement("title", Namespace = "http://www.google.com/schemas/sitemap-video/1.1")]
    public string? Title { get; set; }
    #endregion
    #region Description
    /// <summary>
    /// Description of the video, as plain text — no markup. Google truncates at 2,048 characters.
    /// Omitted when <see langword="null"/>.
    /// </summary>
    [XmlElement("description", Namespace = "http://www.google.com/schemas/sitemap-video/1.1")]
    public string? Description { get; set; }
    #endregion
    #region ContentLocation
    /// <summary>
    /// URL of the raw media file itself (mp4, webm, …), not of a page embedding it. Either this or
    /// <see cref="PlayerLocation"/> should be given, and they must differ from the containing
    /// <see cref="Url.Loc"/>. Omitted when <see langword="null"/>.
    /// </summary>
    [XmlElement("content_loc", Namespace = "http://www.google.com/schemas/sitemap-video/1.1")]
    public string? ContentLocation { get; set; }
    #endregion
    #region PlayerLocation
    /// <summary>
    /// URL of a player for the video — the embed URL, not the page it is embedded in. The
    /// alternative to <see cref="ContentLocation"/>. Omitted when <see langword="null"/>.
    /// </summary>
    [XmlElement("player_loc", Namespace = "http://www.google.com/schemas/sitemap-video/1.1")]
    public string? PlayerLocation { get; set; }
    #endregion
    #region Duration
    /// <summary>
    /// Length of the video in seconds. Google accepts 1 to 28,800 (eight hours).
    /// <see langword="null"/> omits the element.
    /// </summary>
    [XmlElement("duration", Namespace = "http://www.google.com/schemas/sitemap-video/1.1")]
    public int? Duration { get; set; }

    /// <summary>
    /// Tells <see cref="XmlSerializer"/> to leave this optional element out when the member is
    /// unset, instead of writing an empty element or the underlying type's default. Every optional
    /// member of <see cref="Video"/> has one of these; they are the mechanism by which an unset
    /// member disappears from the XML rather than being published as a value.
    /// </summary>
    /// <returns><see langword="true"/> when the member has a value.</returns>
    public bool ShouldSerializeDuration() => Duration != null;
    #endregion
    #region ExpirationDate
    /// <summary>
    /// Date after which the video is no longer available, in W3C format. Set it only for content
    /// that really expires — a crawler will stop showing the video once the date passes.
    /// <see langword="null"/> omits the element and means the video does not expire.
    /// </summary>
    [XmlElement("expiration_date", Namespace = "http://www.google.com/schemas/sitemap-video/1.1")]
    public DateTime? ExpirationDate { get; set; }

    /// <inheritdoc cref="ShouldSerializeDuration"/>
    public bool ShouldSerializeExpirationDate() => ExpirationDate != null;
    #endregion
    #region Rating
    /// <summary>
    /// Average viewer rating of the video, from 0.0 to 5.0. <see langword="null"/> omits the
    /// element, which is what you want when the video has not been rated — a written
    /// <c>0</c> reads as the worst possible rating, not as "no rating".
    /// </summary>
    [XmlElement("rating", Namespace = "http://www.google.com/schemas/sitemap-video/1.1")]
    public double? Rating { get; set; }

    /// <inheritdoc cref="ShouldSerializeDuration"/>
    public bool ShouldSerializeRating() => Rating != null;
    #endregion
    #region ViewCount
    /// <summary>
    /// Number of times the video has been watched. <see langword="null"/> omits the element and
    /// means the count is unknown, which is distinct from a count of zero.
    /// </summary>
    [XmlElement("view_count", Namespace = "http://www.google.com/schemas/sitemap-video/1.1")]
    public int? ViewCount { get; set; }

    /// <inheritdoc cref="ShouldSerializeDuration"/>
    public bool ShouldSerializeViewCount() => ViewCount != null;
    #endregion
    #region PublicationDate
    /// <summary>
    /// Date the video was first published, in W3C format. <see langword="null"/> omits the
    /// element.
    /// </summary>
    [XmlElement("publication_date", Namespace = "http://www.google.com/schemas/sitemap-video/1.1")]
    public DateTime? PublicationDate { get; set; }

    /// <inheritdoc cref="ShouldSerializeDuration"/>
    public bool ShouldSerializePublicationDate() => PublicationDate != null;
    #endregion
    #region FamilyFriendly
    /// <summary>
    /// <see langword="false"/> marks the video as unsuitable for minors, so it is withheld when
    /// SafeSearch is on. <see langword="null"/> omits the element, which a crawler treats the same
    /// as <see langword="true"/> — this is why the guard matters: an unset
    /// <see cref="bool"/>? written as its default would claim the opposite.
    /// </summary>
    [XmlElement("family_friendly", Namespace = "http://www.google.com/schemas/sitemap-video/1.1")]
    public bool? FamilyFriendly { get; set; }

    /// <inheritdoc cref="ShouldSerializeDuration"/>
    public bool ShouldSerializeFamilyFriendly() => FamilyFriendly != null;
    #endregion
    #region Live
    /// <summary>
    /// <see langword="true"/> while the video is a live stream. <see langword="null"/> omits the
    /// element and means the video is ordinary on-demand content.
    /// </summary>
    [XmlElement("live", Namespace = "http://www.google.com/schemas/sitemap-video/1.1")]
    public bool? Live { get; set; }

    /// <inheritdoc cref="ShouldSerializeDuration"/>
    public bool ShouldSerializeLive() => Live != null;
    #endregion
}
