using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Mvc;
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
    public void Abstractions_ExportsOnlyIOpenSearchService()
    {
        var exported = Abstractions.GetExportedTypes();

        Assert.Equal([typeof(IOpenSearchService)], exported);
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
    public void PerformSearch_ReturnsTaskOfRedirectResult()
    {
        var method = typeof(IOpenSearchService).GetMethod(nameof(IOpenSearchService.PerformSearch))!;

        Assert.Equal(typeof(Task<RedirectResult>), method.ReturnType);
        var parameter = Assert.Single(method.GetParameters());
        Assert.Equal(typeof(string), parameter.ParameterType);
        Assert.Equal("searchTerms", parameter.Name);
    }

    /// <summary>
    /// Both parameters are declared <c>string?</c>, which is honest: D-S18 means the library hands
    /// the implementation null on every single call.
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
    /// Pins the abstraction leak: <c>PerformSearch</c> returns an MVC <see cref="RedirectResult"/>,
    /// so a would-be zero-dependency contract package drags in the whole MVC framework reference and
    /// forces every implementation — including a non-MVC one — to construct an MVC action result.
    /// A URL plus a permanence flag would say the same thing with no dependency. And the parts of
    /// <c>RedirectResult</c> that carry meaning (<c>Permanent</c>, <c>PreserveMethod</c>) are
    /// discarded by the caller anyway (D-S23), so the type is paying its cost for nothing.
    /// </summary>
    [Fact]
    public void IOpenSearchService_LeaksMvcRedirectResult_KnownGap()
    {
        var method = typeof(IOpenSearchService).GetMethod(nameof(IOpenSearchService.PerformSearch))!;
        var returned = method.ReturnType.GetGenericArguments()[0];

        Assert.Equal("Microsoft.AspNetCore.Mvc", returned.Namespace);
        Assert.NotNull(returned.GetProperty(nameof(RedirectResult.Permanent)));
        Assert.NotNull(returned.GetProperty(nameof(RedirectResult.PreserveMethod)));
    }

    [Fact]
    public void OpenSearchOptions_ExposesTheSevenDocumentedSettings()
    {
        var names = typeof(OpenSearchOptions).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();

        Assert.Equal(
            ["Contact", "Description", "ImageUrl", "OsdxEndpoint", "SearchUrl", "ShortName", "SuggestUrl"],
            names);
    }

    [Fact]
    public void OpenSearchOptions_HasAPublicParameterlessConstructor()
    {
        Assert.NotNull(typeof(OpenSearchOptions).GetConstructor(Type.EmptyTypes));
    }

    /// <summary>
    /// Pins D-S27's premise from the test side: every option is declared non-nullable <c>string</c>
    /// yet is in fact null on a default instance, which is what makes the null-fallback chains in
    /// <c>MapOpenSearch</c> necessary.
    /// </summary>
    [Fact]
    public void OpenSearchOptions_PropertiesAreDeclaredNonNullableButDefaultToNull_KnownGap()
    {
        var options = new OpenSearchOptions();
        var context = new NullabilityInfoContext();

        foreach (var property in typeof(OpenSearchOptions).GetProperties())
        {
            Assert.Equal(typeof(string), property.PropertyType);
            Assert.Equal(NullabilityState.NotNull, context.Create(property).ReadState);
            Assert.Null(property.GetValue(options));
        }
    }

    /// <summary>
    /// The formatter and the <c>HttpContext</c> extensions must stay internal — they are
    /// implementation detail reachable from tests only through InternalsVisibleTo.
    /// </summary>
    [Theory]
    [InlineData("MintPlayer.AspNetCore.OpenSearch.Formatters.XmlSerializerOutputFormatter")]
    [InlineData("MintPlayer.AspNetCore.OpenSearch.Extensions.HttpContextExtensions")]
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
    /// <c>NullIfEmpty</c> is public on <c>string?</c> in both this package and SitemapXml — the
    /// ambiguity D-S9 describes. Pinned here so the fix is visible as a signature change.
    /// </summary>
    [Fact]
    public void NullIfEmpty_IsPublic_KnownGap()
    {
        var method = typeof(MintPlayer.AspNetCore.OpenSearch.Extensions.StringExtensions)
            .GetMethod("NullIfEmpty", BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.True(method.IsPublic);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("x", "x")]
    public void NullIfEmpty_MapsOnlyTheEmptyStringToNull(string? input, string? expected)
    {
        Assert.Equal(expected, MintPlayer.AspNetCore.OpenSearch.Extensions.StringExtensions.NullIfEmpty(input));
    }

    /// <summary>
    /// Whitespace survives <c>NullIfEmpty</c>, so <c>OsdxEndpoint = "   "</c> reaches the
    /// leading-slash check and throws rather than falling back to the default — the second half of
    /// D-S9.
    /// </summary>
    [Fact]
    public void NullIfEmpty_DoesNotTrimWhitespace_KnownGap()
    {
        Assert.Equal("   ", MintPlayer.AspNetCore.OpenSearch.Extensions.StringExtensions.NullIfEmpty("   "));
    }

    [Fact]
    public void DataTypes_AreExportedForConsumerInspection()
    {
        var exported = Library.GetExportedTypes().Select(t => t.FullName).ToArray();

        Assert.Contains("MintPlayer.AspNetCore.OpenSearch.Data.OpenSearchDescription", exported);
        Assert.Contains("MintPlayer.AspNetCore.OpenSearch.Data.Url", exported);
        Assert.Contains("MintPlayer.AspNetCore.OpenSearch.Data.Image", exported);
    }
}
