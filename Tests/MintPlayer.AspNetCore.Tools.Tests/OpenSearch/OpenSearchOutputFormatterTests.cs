using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.AspNetCore.OpenSearch.Data;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.OpenSearch;

public class OpenSearchOutputFormatterTests
{
    /// <summary>
    /// Exposes the <c>protected</c> <c>CanWriteType</c> override. InternalsVisibleTo makes the
    /// class reachable but not its protected members, so a subclass is the only route in.
    /// </summary>
    private sealed class Probe : MintPlayer.AspNetCore.OpenSearch.Formatters.XmlSerializerOutputFormatter
    {
        public bool CanWrite(Type? type) => CanWriteType(type);
    }

    [Fact]
    public void SupportedMediaTypes_IsExactlyTheOpenSearchDescriptionType()
    {
        var formatter = new Probe();

        Assert.Equal(["application/opensearchdescription+xml"], formatter.SupportedMediaTypes.ToArray());
    }

    /// <summary>An OSDX file is a standalone document, so the XML declaration must be present.</summary>
    [Fact]
    public void WriterSettings_KeepsXmlDeclaration()
    {
        var formatter = new Probe();

        Assert.False(formatter.WriterSettings.OmitXmlDeclaration);
    }

    [Fact]
    public void CanWriteType_OpenSearchDescription_IsTrue()
    {
        Assert.True(new Probe().CanWrite(typeof(OpenSearchDescription)));
    }

    /// <summary>
    /// Load-bearing: the suggest endpoint writes an <c>object[]</c> through the same
    /// <c>ObjectResult</c> pipeline. This formatter sits at index 0 of <c>OutputFormatters</c>, so
    /// if it claimed <c>object[]</c> the suggest response would be XML — or an XmlSerializer
    /// failure — instead of falling through to the JSON formatter.
    /// </summary>
    [Fact]
    public void CanWriteType_ObjectArray_IsFalse()
    {
        Assert.False(new Probe().CanWrite(typeof(object[])));
    }

    [Theory]
    [InlineData(typeof(Image))]
    [InlineData(typeof(Url))]
    [InlineData(typeof(List<Url>))]
    [InlineData(typeof(string))]
    [InlineData(typeof(object))]
    [InlineData(typeof(string[]))]
    public void CanWriteType_AnythingElse_IsFalse(Type type)
    {
        Assert.False(new Probe().CanWrite(type));
    }

    [Fact]
    public void CanWriteType_Null_IsFalse()
    {
        Assert.False(new Probe().CanWrite(null));
    }

    /// <summary>
    /// A subclass matches too: the check is <c>IsAssignableFrom</c>, not <c>type ==</c>. It used to
    /// be exact equality, so a consumer subclassing the description silently lost the formatter —
    /// <c>ObjectResult.DeclaredType</c> decides what is asked about.
    /// </summary>
    [Fact]
    public void CanWriteType_SubclassOfOpenSearchDescription_IsTrue()
    {
        Assert.True(new Probe().CanWrite(typeof(DerivedDescription)));
    }

    /// <summary>
    /// The counterpart of the above: widening to assignability must not start claiming base types.
    /// <c>typeof(object)</c> in particular is what an untyped write asks about.
    /// </summary>
    [Fact]
    public void CanWriteType_BaseTypesOfOpenSearchDescription_AreFalse()
    {
        Assert.False(new Probe().CanWrite(typeof(object)));
    }

    private sealed class DerivedDescription : OpenSearchDescription;

    private static async Task<string> WriteAsync(object value, Type declaredType)
    {
        var formatter = new Probe();

        // The base formatter resolves IHttpResponseStreamWriterFactory from RequestServices, so a
        // bare DefaultHttpContext is not enough.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMvcCore();

        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };

        // The base formatter buffers into a FileBufferingWriteStream and drains to
        // Response.BodyWriter, so the response needs a real body feature — assigning Response.Body
        // on a DefaultHttpContext is not enough.
        var body = new MemoryStream();
        httpContext.Features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(body));

        var context = new OutputFormatterWriteContext(
            httpContext,
            // leaveOpen: true — the base formatter drains its FileBufferingWriteStream after the
            // writer is disposed, and StreamWriter would otherwise dispose that stream first.
            (stream, encoding) => new StreamWriter(stream, encoding, 1024, leaveOpen: true),
            declaredType,
            value);

        await formatter.WriteResponseBodyAsync(context, Encoding.UTF8);

        return Encoding.UTF8.GetString(body.ToArray());
    }

    [Fact]
    public async Task WriteResponseBodyAsync_EmitsXmlDeclaration()
    {
        var raw = await WriteAsync(new OpenSearchDescription { ShortName = "X" }, typeof(OpenSearchDescription));

        Assert.StartsWith("<?xml", raw.TrimStart('﻿'));
    }

    /// <summary>
    /// The <c>Serialize</c> override registers an empty prefix for the empty namespace, which
    /// suppresses the <c>xsi</c>/<c>xsd</c> declarations XmlSerializer otherwise stamps on the root.
    /// A reader stays unaffected either way, but the file a browser downloads is the product here.
    /// </summary>
    [Fact]
    public async Task WriteResponseBodyAsync_DoesNotEmitXsiOrXsdDeclarations()
    {
        var raw = await WriteAsync(new OpenSearchDescription { ShortName = "X" }, typeof(OpenSearchDescription));

        Assert.DoesNotContain("http://www.w3.org/2001/XMLSchema-instance", raw);
        Assert.DoesNotContain("http://www.w3.org/2001/XMLSchema", raw);
    }

    [Fact]
    public async Task WriteResponseBodyAsync_RoundTripsThroughXDocument()
    {
        var raw = await WriteAsync(
            new OpenSearchDescription
            {
                ShortName = "MintPlayer",
                Urls = [new Url { Type = "text/html", Method = "GET", Template = "https://example.com/search" }],
            },
            typeof(OpenSearchDescription));

        var document = XDocument.Parse(raw.TrimStart('﻿'));

        Assert.Equal(OpenSearchTestHost.A9 + "OpenSearchDescription", document.Root!.Name);
        Assert.Equal("MintPlayer", document.Root.Element(OpenSearchTestHost.A9 + "ShortName")!.Value);
        Assert.Single(document.Root.Elements(OpenSearchTestHost.A9 + "Url"));
    }

    /// <summary>
    /// The empty-prefix registration in <c>Serialize</c> maps the a9 namespace onto the default
    /// prefix, so the whole document — <c>SearchForm</c> included — is emitted unprefixed with no
    /// <c>xmlns=""</c> reset anywhere. D-S15 does not reproduce; see
    /// <c>OpenSearchDescriptionSerializationTests.Serialize_SearchForm_InheritsA9NamespaceFromTheRoot</c>.
    /// </summary>
    [Fact]
    public async Task WriteResponseBodyAsync_EmitsNoEmptyNamespaceReset()
    {
        var raw = await WriteAsync(
            new OpenSearchDescription { ShortName = "X", SearchForm = "https://example.com/" },
            typeof(OpenSearchDescription));

        Assert.DoesNotContain("xmlns=\"\"", raw);
        Assert.Contains("<SearchForm>https://example.com/</SearchForm>", raw);
    }
}
