using System.Xml;
using System.Xml.Serialization;

namespace MintPlayer.AspNetCore.OpenSearch.Formatters
{
    internal class XmlSerializerOutputFormatter : Microsoft.AspNetCore.Mvc.Formatters.XmlSerializerOutputFormatter
    {
        public XmlSerializerOutputFormatter()
        {
            this.WriterSettings.OmitXmlDeclaration = false;
            this.SupportedMediaTypes.Clear();
            this.SupportedMediaTypes.Add("application/opensearchdescription+xml");
        }

        protected override void Serialize(XmlSerializer xmlSerializer, XmlWriter xmlWriter, object? value)
        {
            var ns = new XmlSerializerNamespaces();
            ns.Add(string.Empty, string.Empty);

            xmlSerializer.Serialize(xmlWriter, value, ns);
        }

        protected override bool CanWriteType(Type? type)
        {
            if (type == typeof(Data.OpenSearchDescription))
            {
                return true;
            }
            //else if (type == typeof(Data.Image))
            //{
            //    return true;
            //}
            //else if (type == typeof(Data.Url))
            //{
            //    return true;
            //}
            else
            {
                return false;
            }
        }
    }
}
