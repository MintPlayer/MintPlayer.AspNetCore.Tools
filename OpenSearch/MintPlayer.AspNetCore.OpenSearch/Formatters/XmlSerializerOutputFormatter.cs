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

        /// <remarks>
        /// Assignability, not equality, so a consumer that subclasses
        /// <see cref="Data.OpenSearchDescription"/> keeps this formatter. Everything else — including
        /// the <c>object[]</c> the suggest endpoint writes — must still be refused: this formatter
        /// sits at index 0 of <c>OutputFormatters</c>, so claiming a type here takes it away from the
        /// JSON formatter.
        /// </remarks>
        protected override bool CanWriteType(Type? type)
            => type is not null && typeof(Data.OpenSearchDescription).IsAssignableFrom(type);
    }
}
