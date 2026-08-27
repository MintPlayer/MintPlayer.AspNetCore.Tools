using Xunit;

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests;

/// <summary>
/// The two pure string functions on <see cref="AssemblyInfo"/>: they decide the name of the
/// extension method every consumer calls, and the name of the class that carries it.
/// </summary>
/// <remarks>
/// These are asserted directly rather than through generated text because they carry real defects
/// (D-G12 crash inputs, D-G13 collision) whose inputs a compilation cannot even express — an
/// assembly name is not required to be a valid identifier.
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
    }

    [Fact]
    public void GetMethodName_OverrideWins_AndIsUsedVerbatim()
    {
        var info = new AssemblyInfo("MyApp.Api", "MapCustomEndpoints");

        Assert.Equal("MapCustomEndpoints", info.GetMethodName());
    }

    /// <summary>
    /// Pins D-G12: an empty dot-segment indexes <c>s[0]</c> on an empty string. The exception
    /// escapes into Roslyn, which turns the whole generator off for that compilation.
    /// </summary>
    [Theory]
    [InlineData("My..Api")]
    [InlineData(".My")]
    [InlineData("My.")]
    [InlineData("")]
    public void GetMethodName_EmptySegment_Throws_KnownBug(string assemblyName)
    {
        var info = new AssemblyInfo(assemblyName, null);

        Assert.Throws<IndexOutOfRangeException>(() => info.GetMethodName());
    }

    /// <summary>
    /// Pins D-G12: nothing sanitises the segments, so any character that is legal in an assembly
    /// name but illegal in an identifier is copied straight into the emitted method name.
    /// </summary>
    [Theory]
    [InlineData("My-App", "MapMy-AppEndpoints")]
    [InlineData("My App", "MapMy AppEndpoints")]
    [InlineData("2Fast", "Map2FastEndpoints")]
    public void GetMethodName_IllegalIdentifierCharacters_PassThrough_KnownBug(string assemblyName, string expected)
    {
        var info = new AssemblyInfo(assemblyName, null);

        Assert.Equal(expected, info.GetMethodName());
    }

    /// <summary>
    /// Pins D-G12: an empty override is taken at face value, so the generator emits a method with
    /// no name at all.
    /// </summary>
    [Fact]
    public void GetMethodName_EmptyOverride_YieldsNoName_KnownBug()
    {
        var info = new AssemblyInfo("MyApp.Api", "");

        Assert.Equal("", info.GetMethodName());
    }

    [Theory]
    [InlineData("MyApp.Api", null, "MyAppApiEndpointsExtensions")]
    [InlineData("MyApp.Api", "MapCustomEndpoints", "CustomEndpointsExtensions")]
    // No "Map" prefix to strip, so the whole override becomes part of the class name.
    [InlineData("MyApp.Api", "RegisterEndpoints", "RegisterEndpointsExtensions")]
    public void GetSafeClassName_StripsAMapPrefixAndAppendsExtensions(
        string assemblyName, string? methodNameOverride, string expected)
    {
        var info = new AssemblyInfo(assemblyName, methodNameOverride);

        Assert.Equal(expected, info.GetSafeClassName());
    }

    /// <summary>
    /// Pins D-G13: <c>GetSafeClassName</c> is "safe" in name only. The generated class always lands
    /// in the library's own namespace, so an override that strips down to the name of a shipped type
    /// collides with it — here with the real
    /// <c>MintPlayer.AspNetCore.Endpoints.EndpointRouteBuilderExtensions</c>, giving the consumer a
    /// CS0101 in code they cannot edit.
    /// </summary>
    [Fact]
    public void GetSafeClassName_CollidesWithAShippedType_KnownBug()
    {
        var info = new AssemblyInfo("MyApp.Api", "MapEndpointRouteBuilder");

        Assert.Equal("EndpointRouteBuilderExtensions", info.GetSafeClassName());
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

    [Fact]
    public void GetHashCode_IsDerivedFromTheAssemblyNameOnly()
    {
        Assert.Equal(
            new AssemblyInfo("MyApp.Api", null).GetHashCode(),
            new AssemblyInfo("MyApp.Api", "MapCustomEndpoints").GetHashCode());
    }
}
