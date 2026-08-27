using System.Reflection;
using System.Runtime.CompilerServices;
using MintPlayer.AspNetCore.OpenSearch;
using MintPlayer.AspNetCore.OpenSearch.Abstractions;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.OpenSearch;

/// <summary>
/// Shape pins for the published surface of both OpenSearch packages. These are the signatures
/// consumers compile against; changing one is a breaking change that no other test would catch.
/// </summary>
public class OpenSearchContractTests
{
    private static readonly Assembly Abstractions = typeof(IOpenSearchService).Assembly;
    private static readonly Assembly Library = typeof(OpenSearchExtensions).Assembly;

    [Fact]
    public void Abstractions_ExportsTheServiceAndItsRedirect()
    {
        var exported = Abstractions.GetExportedTypes().OrderBy(t => t.Name).ToArray();

        Assert.Equal([typeof(IOpenSearchService), typeof(OpenSearchRedirect)], exported);
    }

    /// <summary>
    /// The contract package must stay framework-free: it is the one a consumer implements, and it
    /// used to drag in the whole of ASP.NET because <c>PerformSearch</c> returned an MVC
    /// <c>RedirectResult</c>.
    /// </summary>
    [Fact]
    public void Abstractions_ReferencesNoAspNetCoreAssembly()
    {
        var referenced = Abstractions.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

        Assert.DoesNotContain(referenced, name => name!.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    [Fact]
    public void IOpenSearchService_HasExactlyTwoMethods()
    {
        var methods = typeof(IOpenSearchService).GetMethods().Select(m => m.Name).OrderBy(n => n).ToArray();

        Assert.Equal(["PerformSearch", "ProvideSuggestions"], methods);
    }

    [Fact]
    public void ProvideSuggestions_ReturnsTaskOfEnumerableOfString()
    {
        var method = typeof(IOpenSearchService).GetMethod(nameof(IOpenSearchService.ProvideSuggestions))!;

        Assert.Equal(typeof(Task<IEnumerable<string>>), method.ReturnType);
        var parameter = Assert.Single(method.GetParameters());
        Assert.Equal(typeof(string), parameter.ParameterType);
        Assert.Equal("searchTerms", parameter.Name);
    }

    [Fact]
    public void PerformSearch_ReturnsTaskOfOpenSearchRedirect()
    {
        var method = typeof(IOpenSearchService).GetMethod(nameof(IOpenSearchService.PerformSearch))!;

        Assert.Equal(typeof(Task<OpenSearchRedirect>), method.ReturnType);
        var parameter = Assert.Single(method.GetParameters());
        Assert.Equal(typeof(string), parameter.ParameterType);
        Assert.Equal("searchTerms", parameter.Name);
    }

    /// <summary>
    /// Both parameters are declared <c>string?</c> — the query can genuinely be absent, e.g. a bare
    /// GET of the search endpoint with no query string.
    /// </summary>
    [Theory]
    [InlineData(nameof(IOpenSearchService.ProvideSuggestions))]
    [InlineData(nameof(IOpenSearchService.PerformSearch))]
    public void SearchTermsParameter_IsNullableAnnotated(string methodName)
    {
        var parameter = typeof(IOpenSearchService).GetMethod(methodName)!.GetParameters()[0];

        var info = new NullabilityInfoContext().Create(parameter);

        Assert.Equal(NullabilityState.Nullable, info.WriteState);
    }

    /// <summary>
    /// <c>OpenSearchRedirect</c> carries exactly the three things the endpoint needs, and nothing a
    /// consumer would have to reference a framework to construct.
    /// </summary>
    [Fact]
    public void OpenSearchRedirect_IsAValueLikeRecordOfUrlAndTwoFlags()
    {
        Assert.True(typeof(OpenSearchRedirect).IsSealed);
        Assert.Equal(
            ["Permanent", "PreserveMethod", "Url"],
            typeof(OpenSearchRedirect).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray());

        Assert.Equal(new OpenSearchRedirect("/x"), new OpenSearchRedirect("/x"));
        Assert.NotEqual(new OpenSearchRedirect("/x"), new OpenSearchRedirect("/x", Permanent: true));
    }

    /// <summary>Both flags default to false, so <c>new OpenSearchRedirect(url)</c> means a 302.</summary>
    [Fact]
    public void OpenSearchRedirect_FlagsDefaultToFalse()
    {
        var redirect = new OpenSearchRedirect("/x");

        Assert.False(redirect.Permanent);
        Assert.False(redirect.PreserveMethod);
    }

    [Fact]
    public void OpenSearchOptions_ExposesTheDocumentedSettings()
    {
        var names = typeof(OpenSearchOptions).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();

        Assert.Equal(
            [
                "Contact", "Description", "ImageHeight", "ImageType", "ImageUrl", "ImageWidth",
                "OsdxEndpoint", "SearchTermsParameter", "SearchUrl", "ShortName", "SuggestUrl",
            ],
            names);
    }

    [Fact]
    public void OpenSearchOptions_HasAPublicParameterlessConstructor()
    {
        Assert.NotNull(typeof(OpenSearchOptions).GetConstructor(Type.EmptyTypes));
    }

    /// <summary>
    /// D-S27 fixed: the options that are genuinely absent by default are annotated <c>string?</c>,
    /// so the signature no longer lies to a consumer reading it under <c>Nullable=enable</c>.
    /// </summary>
    [Theory]
    [InlineData(nameof(OpenSearchOptions.OsdxEndpoint))]
    [InlineData(nameof(OpenSearchOptions.SearchUrl))]
    [InlineData(nameof(OpenSearchOptions.SuggestUrl))]
    [InlineData(nameof(OpenSearchOptions.ImageUrl))]
    [InlineData(nameof(OpenSearchOptions.ShortName))]
    [InlineData(nameof(OpenSearchOptions.Description))]
    [InlineData(nameof(OpenSearchOptions.Contact))]
    public void OpenSearchOptions_UnsetStringProperty_IsNullableAnnotatedAndNull(string propertyName)
    {
        var property = typeof(OpenSearchOptions).GetProperty(propertyName)!;

        Assert.Equal(typeof(string), property.PropertyType);
        Assert.Equal(NullabilityState.Nullable, new NullabilityInfoContext().Create(property).ReadState);
        Assert.Null(property.GetValue(new OpenSearchOptions()));
    }

    /// <summary>
    /// The other half of D-S27: an option that is non-nullable must actually carry a value. These
    /// three do, via field initialisers.
    /// </summary>
    [Fact]
    public void OpenSearchOptions_NonNullableProperties_HaveDefaults()
    {
        var options = new OpenSearchOptions();

        Assert.Equal("q", options.SearchTermsParameter);
        Assert.Equal("image/png", options.ImageType);
        Assert.Equal(16, options.ImageWidth);
        Assert.Equal(16, options.ImageHeight);

        var context = new NullabilityInfoContext();
        Assert.Equal(NullabilityState.NotNull, context.Create(typeof(OpenSearchOptions).GetProperty(nameof(OpenSearchOptions.SearchTermsParameter))!).ReadState);
        Assert.Equal(NullabilityState.NotNull, context.Create(typeof(OpenSearchOptions).GetProperty(nameof(OpenSearchOptions.ImageType))!).ReadState);
    }

    /// <summary>
    /// The formatter, the <c>HttpContext</c> extensions and the <c>string</c> extensions must stay
    /// internal — implementation detail reachable from tests only through InternalsVisibleTo.
    /// </summary>
    [Theory]
    [InlineData("MintPlayer.AspNetCore.OpenSearch.Formatters.XmlSerializerOutputFormatter")]
    [InlineData("MintPlayer.AspNetCore.OpenSearch.Extensions.HttpContextExtensions")]
    [InlineData("MintPlayer.AspNetCore.OpenSearch.Extensions.StringExtensions")]
    [InlineData("MintPlayer.AspNetCore.OpenSearch.OpenSearchMarker")]
    public void ImplementationDetail_IsNotExported(string typeName)
    {
        var type = Library.GetType(typeName);

        Assert.NotNull(type);
        Assert.False(type.IsPublic);
    }

    [Fact]
    public void Library_GrantsInternalsVisibilityToThisTestAssembly()
    {
        var granted = Library.GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(a => a.AssemblyName)
            .ToArray();

        Assert.Contains("MintPlayer.AspNetCore.Tools.Tests", granted);
    }

    [Fact]
    public void OpenSearchExtensions_ExposesTheThreeEntryPoints()
    {
        var names = typeof(OpenSearchExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToArray();

        Assert.Equal(2, names.Count(n => n == "AddOpenSearch"));
        Assert.Single(names, n => n == "MapOpenSearch");
    }

    /// <summary>
    /// Both <c>AddOpenSearch</c> overloads constrain <c>TService</c> to a concrete
    /// <see cref="IOpenSearchService"/>, so a consumer cannot register an interface or a struct.
    /// </summary>
    [Fact]
    public void AddOpenSearch_ConstrainsServiceToIOpenSearchService()
    {
        var overloads = typeof(OpenSearchExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == "AddOpenSearch")
            .ToArray();

        Assert.Equal(2, overloads.Length);
        foreach (var overload in overloads)
        {
            var argument = Assert.Single(overload.GetGenericArguments());
            Assert.Contains(typeof(IOpenSearchService), argument.GetGenericParameterConstraints());
            Assert.True(argument.GenericParameterAttributes.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint));
        }
    }

    /// <summary>
    /// D-S9 fixed: <c>NullIfEmpty</c> is internal in both packages now. While it was public on
    /// <c>string?</c> in this package *and* in SitemapXml, an app installing both with implicit
    /// usings on got an ambiguous-call error on every use.
    /// </summary>
    [Fact]
    public void NullIfEmpty_IsInternal()
    {
        var type = typeof(MintPlayer.AspNetCore.OpenSearch.Extensions.StringExtensions);

        Assert.False(type.IsPublic);
        Assert.Null(type.GetMethod("NullIfEmpty", BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(type.GetMethod("NullIfEmpty", BindingFlags.NonPublic | BindingFlags.Static));
    }

    /// <summary>
    /// D-S9's second half: whitespace counts as absent, so <c>OsdxEndpoint = "   "</c> falls back to
    /// the default instead of reaching the leading-slash check and throwing.
    /// </summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("\t\r\n", null)]
    [InlineData("x", "x")]
    [InlineData(" x ", " x ")]
    public void NullIfEmpty_MapsBlankToNullAndPreservesEverythingElse(string? input, string? expected)
    {
        Assert.Equal(expected, MintPlayer.AspNetCore.OpenSearch.Extensions.StringExtensions.NullIfEmpty(input));
    }

    [Fact]
    public void DataTypes_AreExportedForConsumerInspection()
    {
        var exported = Library.GetExportedTypes().Select(t => t.FullName).ToArray();

        Assert.Contains("MintPlayer.AspNetCore.OpenSearch.Data.OpenSearchDescription", exported);
        Assert.Contains("MintPlayer.AspNetCore.OpenSearch.Data.Url", exported);
        Assert.Contains("MintPlayer.AspNetCore.OpenSearch.Data.Image", exported);
    }

    /// <summary>
    /// D-S27 for the DTOs: every string member is nullable-annotated, because
    /// <c>XmlSerializer</c> omits an unset element and the library relies on that (a null
    /// <c>Contact</c> or <c>Image</c> must disappear rather than serialize empty).
    /// </summary>
    [Theory]
    [InlineData(typeof(MintPlayer.AspNetCore.OpenSearch.Data.OpenSearchDescription))]
    [InlineData(typeof(MintPlayer.AspNetCore.OpenSearch.Data.Url))]
    [InlineData(typeof(MintPlayer.AspNetCore.OpenSearch.Data.Image))]
    public void DataTypes_ReferenceProperties_AreNullableAnnotated(Type type)
    {
        var context = new NullabilityInfoContext();

        foreach (var property in type.GetProperties().Where(p => !p.PropertyType.IsValueType))
        {
            Assert.Equal(NullabilityState.Nullable, context.Create(property).ReadState);
        }
    }
}
