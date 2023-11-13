using System.Xml.Serialization;

namespace MintPlayer.AspNetCore.OpenSearch.Data;

[XmlRoot("OpenSearchDescription", Namespace = "http://a9.com/-/spec/opensearch/1.1/")]
public class OpenSearchDescription
{
    [XmlElement("ShortName", Namespace = "http://a9.com/-/spec/opensearch/1.1/")]
    public string ShortName { get; set; }

    [XmlElement("Description", Namespace = "http://a9.com/-/spec/opensearch/1.1/")]
    public string Description { get; set; }

    [XmlElement("InputEncoding", Namespace = "http://a9.com/-/spec/opensearch/1.1/")]
    public string InputEncoding { get; set; }

    [XmlElement("Url", Namespace = "http://a9.com/-/spec/opensearch/1.1/")]
    public List<Url> Urls { get; set; }

    [XmlElement("Image", Namespace = "http://a9.com/-/spec/opensearch/1.1/")]
    public Image Image { get; set; }

    [XmlElement("SearchForm")]
    public string SearchForm { get; set; }

    [XmlElement("Contact", Namespace = "http://a9.com/-/spec/opensearch/1.1/")]
    public string Contact { get; set; }
}
