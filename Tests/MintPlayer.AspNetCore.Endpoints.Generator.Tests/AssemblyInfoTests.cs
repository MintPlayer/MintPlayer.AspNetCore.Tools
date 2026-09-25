using Xunit;

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests;

/// <summary>
/// The two pure string functions on <see cref="AssemblyInfo"/>: they decide the name of the
/// extension method every consumer calls, and the name of the class that carries it.
/// </summary>
/// <remarks>
/// These are asserted directly rather than through generated text because their hard inputs cannot
/// be expressed by a compilation at all — an assembly name is not required to be a valid C#
/// identifier, and the override is whatever string the consumer typed.
/// </remarks>
public class AssemblyInfoTests
{
    [Theory]
    [InlineData("MyApp.Api", "MapMyAppApiEndpoints")]
    [InlineData("Api", "MapApiEndpoints")]
    [InlineData("myapp", "MapMyappEndpoints")]
    [InlineData("MintPlayer.AspNetCore.Endpoints.TestApp", "MapMintPlayerAspNetCoreEndpointsTestAppEndpoints")]
    public void GetMethodName_ConcatenatesCapitalisedDotSegments(string assemblyName, string expected)
    {
        var info = new AssemblyInfo(assemblyName, null);

        Assert.Equal(expected, info.GetMethodName());
        Assert.False(info.MethodNameWasSanitised);
    }

    [Fact]
    public void GetMethodName_OverrideWins_AndIsUsedVerbatim()
    {
        var info = new AssemblyInfo("MyApp.Api", "MapCustomEndpoints");

        Assert.Equal("MapCustomEndpoints", info.GetMethodName());
        Assert.False(info.MethodNameWasSanitised);
    }

    /// <summary>
    /// An empty dot-segment is skipped rather than indexing <c>s[0]</c> on an empty string.
    /// </summary>
    /// <remarks>
    /// The <see cref="IndexOutOfRangeException"/> this used to throw did not surface anywhere: the
    /// Tools package's <c>Produce</c> swallows it, so the consumer got no file, no diagnostic and not
    /// even CS8785 — only a CS1061 on the <c>Map…Endpoints()</c> call they were told to write.
    /// <c>"My..Api"</c> is unusual but legal, and a trailing dot arrives the same way from a mistyped
    /// <c>&lt;AssemblyName&gt;</c>.
    /// </remarks>
    [Theory]
    [InlineData("My..Api", "MapMyApiEndpoints")]
    [InlineData(".My", "MapMyEndpoints")]
    [InlineData("My.", "MapMyEndpoints")]
    [InlineData("", "MapAssemblyEndpoints")]
    public void GetMethodName_EmptySegment_IsSkipped_AndReportedAsSanitised(string assemblyName, string expected)
    {
        var info = new AssemblyInfo(assemblyName, null);

        Assert.Equal(expected, info.GetMethodName());
        Assert.True(info.MethodNameWasSanitised);
    }

    /// <summary>
    /// Characters that are legal in an assembly name but not in an identifier are dropped.
    /// </summary>
    /// <remarks>
    /// A hyphenated assembly name is entirely ordinary (<c>my-app.csproj</c>). Letting one through
    /// produced generated code that could not compile, with the errors in a file the consumer never
    /// wrote.
    /// </remarks>
    [Theory]
    [InlineData("My-App", "MapMyAppEndpoints", true)]
    [InlineData("My App", "MapMyAppEndpoints", true)]
    // A leading digit needs no fixup: the "Map" prefix already starts the identifier.
    [InlineData("2Fast", "Map2FastEndpoints", false)]
    public void GetMethodName_IllegalIdentifierCharacters_AreDropped(
        string assemblyName, string expected, bool sanitised)
    {
        var info = new AssemblyInfo(assemblyName, null);

        Assert.Equal(expected, info.GetMethodName());
        Assert.Equal(sanitised, info.MethodNameWasSanitised);
    }

    /// <summary>
    /// An override that survives sanitisation to nothing falls back to the assembly-derived name,
    /// rather than emitting a method with no name at all.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-")]
    public void GetMethodName_UnusableOverride_FallsBackToTheAssemblyName(string methodNameOverride)
    {
        var info = new AssemblyInfo("MyApp.Api", methodNameOverride);

        Assert.Equal("MapMyAppApiEndpoints", info.GetMethodName());
        Assert.True(info.MethodNameWasSanitised);
    }

    /// <summary>An override starting with a digit is prefixed, because nothing else precedes it.</summary>
    [Fact]
    public void GetMethodName_OverrideStartingWithADigit_IsMadeAValidIdentifier()
    {
        var info = new AssemblyInfo("MyApp.Api", "2Map");

        Assert.Equal("_2Map", info.GetMethodName());
        Assert.True(info.MethodNameWasSanitised);
    }

    [Theory]
    [InlineData("MyApp.Api", null, "MyAppApiEndpointsExtensions")]
    [InlineData("MyApp.Api", "MapCustomEndpoints", "CustomEndpointsExtensions")]
    // No "Map" prefix to strip, so the whole override becomes part of the class name.
    [InlineData("MyApp.Api", "RegisterEndpoints", "RegisterEndpointsExtensions")]
    // "Map" and nothing else would leave an empty class name.
    [InlineData("MyApp.Api", "Map", "EndpointMappingExtensions")]
    public void GetSafeClassName_StripsAMapPrefixAndAppendsExtensions(
        string assemblyName, string? methodNameOverride, string expected)
    {
        var info = new AssemblyInfo(assemblyName, methodNameOverride);

        Assert.Equal(expected, info.GetSafeClassName());
    }

    /// <summary>
    /// The class name can still coincide with a shipped type's name — what makes that harmless is
    /// the namespace it is emitted into, which is not the library's own.
    /// </summary>
    /// <remarks>
    /// <c>"MapEndpointRouteBuilder"</c> reads like a perfectly reasonable choice and strips down to
    /// <c>EndpointRouteBuilderExtensions</c>, which the runtime package defines. In the library's own
    /// namespace that silently shadowed the shipped type — source beats metadata for a name, so it
    /// was not even a CS0101. See
    /// <see cref="EndpointMethodNameTests.MethodNameOverrideMatchingAShippedTypeName_DoesNotShadowIt"/>
    /// for the half that fixes it.
    /// </remarks>
    [Fact]
    public void GetSafeClassName_MayCoincideWithAShippedTypeName()
    {
        var info = new AssemblyInfo("MyApp.Api", "MapEndpointRouteBuilder");

        Assert.Equal(typeof(EndpointRouteBuilderExtensions).Name, info.GetSafeClassName());
    }

    /// <summary>
    /// The incremental pipeline caches on this equality, so it is load-bearing rather than
    /// ceremonial. See <see cref="EndpointGeneratorIncrementalTests"/> for the end of that chain.
    /// </summary>
    [Fact]
    public void Equals_ComparesBothMembers()
    {
        var info = new AssemblyInfo("MyApp.Api", "MapCustomEndpoints");

        Assert.Equal(info, new AssemblyInfo("MyApp.Api", "MapCustomEndpoints"));
        Assert.NotEqual(info, new AssemblyInfo("MyApp.Other", "MapCustomEndpoints"));
        Assert.NotEqual(info, new AssemblyInfo("MyApp.Api", "MapOtherEndpoints"));
        Assert.NotEqual(info, new AssemblyInfo("MyApp.Api", null));
        Assert.False(info.Equals(null));
        Assert.False(info.Equals("MyApp.Api"));
    }

    /// <summary>
    /// The OpenAPI flag takes part in equality.
    /// </summary>
    /// <remarks>
    /// If it did not, adding <c>Microsoft.AspNetCore.OpenApi</c> to a project would compare the model
    /// equal to the previous one, the cached outputs would be reused, and <c>EndpointOpenApi.g.cs</c>
    /// would not appear until some unrelated edit invalidated the cache.
    /// </remarks>
    [Fact]
    public void Equals_ComparesTheOpenApiFlag()
    {
        Assert.Equal(new AssemblyInfo("MyApp.Api", null, true), new AssemblyInfo("MyApp.Api", null, true));
        Assert.NotEqual(new AssemblyInfo("MyApp.Api", null, true), new AssemblyInfo("MyApp.Api", null, false));
    }

    /// <summary>
    /// Equal models have equal hash codes, and a changed member makes them unequal (PRD D17). The
    /// hash is the one <c>[GenerateEquality]</c> generates; nothing hashes this model into a collection,
    /// so no particular hash is pinned.
    /// </summary>
    [Fact]
    public void GetHashCode_IsEqualForEqualModels()
    {
        Assert.Equal(
            new AssemblyInfo("MyApp.Api", "MapCustomEndpoints", true).GetHashCode(),
            new AssemblyInfo("MyApp.Api", "MapCustomEndpoints", true).GetHashCode());
        Assert.NotEqual(
            new AssemblyInfo("MyApp.Api", "MapCustomEndpoints", true),
            new AssemblyInfo("MyApp.Api", "MapCustomEndpoints", false));
    }
}
