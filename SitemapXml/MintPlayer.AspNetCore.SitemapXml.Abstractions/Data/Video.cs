using System.Xml.Serialization;

namespace MintPlayer.AspNetCore.SitemapXml.Abstractions.Data;

[XmlType("video", Namespace = "http://www.google.com/schemas/sitemap-video/1.1")]
public class Video
{
    #region ThumbnailLocation
    /// <summary>URL to the video thumbnail image</summary>
    [XmlElement("thumbnail_loc", Namespace = "http://www.google.com/schemas/sitemap-video/1.1")]
    public string ThumbnailLocation { get; set; }

    public bool ShouldSerializeThumbnailLocation() => ThumbnailLocation != null;
    #endregion
    #region Title
    /// <summary>Title for the video</summary>
    [XmlElement("title", Namespace = "http://www.google.com/schemas/sitemap-video/1.1")]
    public string Title { get; set; }
    #endregion
    #region Description
    /// <summary>Description for the video</summary>
    [XmlElement("description", Namespace = "http://www.google.com/schemas/sitemap-video/1.1")]
    public string Description { get; set; }
    #endregion
    #region ContentLocation
    /// <summary>URL to the actual video resource</summary>
    [XmlElement("content_loc", Namespace = "http://www.google.com/schemas/sitemap-video/1.1")]
    public string ContentLocation { get; set; }
    #endregion
    #region PlayerLocation
    /// <summary>URL where the video is hosted</summary>
    [XmlElement("player_loc", Namespace = "http://www.google.com/schemas/sitemap-video/1.1")]
    public string PlayerLocation { get; set; }
    #endregion
    #region Duration
    /// <summary>Duration of the video</summary>
    [XmlElement("duration", Namespace = "http://www.google.com/schemas/sitemap-video/1.1")]
    public int? Duration { get; set; }

    public bool ShouldSerializeDuration() => Duration != null;
    #endregion
    #region ExpirationDate
    /// <summary>Date that specifies how long the video remains available</summary>
    [XmlElement("expiration_date", Namespace = "http://www.google.com/schemas/sitemap-video/1.1")]
    public DateTime? ExpirationDate { get; set; }

    public bool ShouldSerializeExpirationDate() => ExpirationDate != null;
    #endregion
    #region Rating
    /// <summary>A rating for the video</summary>
    [XmlElement("rating", Namespace = "http://www.google.com/schemas/sitemap-video/1.1")]
    public double? Rating { get; set; }

    public bool ShouldSerializeRating() => Rating != null;
    #endregion
    #region ViewCount
    /// <summary>A rating for the video</summary>
    [XmlElement("view_count", Namespace = "http://www.google.com/schemas/sitemap-video/1.1")]
    public int? ViewCount { get; set; }

    public bool ShouldSerializeViewCount() => ViewCount != null;
    #endregion
    #region PublicationDate
    /// <summary>Date when the video was published</summary>
    [XmlElement("publication_date", Namespace = "http://www.google.com/schemas/sitemap-video/1.1")]
    public DateTime? PublicationDate { get; set; }

    public bool ShouldSerializePublicationDate() => PublicationDate != null;
    #endregion
    #region FamilyFriendly
    /// <summary>Specifies whether the video can be viewed by people of any age</summary>
    [XmlElement("family_friendly", Namespace = "http://www.google.com/schemas/sitemap-video/1.1")]
    public bool? FamilyFriendly { get; set; }
    #endregion
    #region Live
    /// <summary>Specifies whether this video is a livestream</summary>
    [XmlElement("live", Namespace = "http://www.google.com/schemas/sitemap-video/1.1")]
    public bool? Live { get; set; }
    #endregion
}
