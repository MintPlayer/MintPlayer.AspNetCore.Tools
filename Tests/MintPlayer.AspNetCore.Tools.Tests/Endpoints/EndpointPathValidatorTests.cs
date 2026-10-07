using MintPlayer.AspNetCore.Endpoints;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.Endpoints;

/// <summary>
/// <see cref="EndpointPathValidator"/>, the startup check on a route chosen by <c>GetPath</c> (PRD
/// R2.10, AC10): parameter names must match the default <c>Path</c>'s, case-insensitively; literal
/// segments, constraints, defaults and optional or catch-all markers may differ.
/// </summary>
/// <remarks>
/// Both mapping paths call it; their own tests (<c>MapEndpointGenericTests</c>,
/// <c>TestLibraryEndToEndTests</c>) pin that it is called, this one pins the rule.
/// </remarks>
public class EndpointPathValidatorTests
{
    private sealed class HookEndpoint;

    [Theory]
    [InlineData("/custom/{id}", "/hooks/{id}")]
    [InlineData("/custom/{ID:int}", "/hooks/{id}")]
    [InlineData("/x/{id?}", "/hooks/{id}")]
    [InlineData("/x/{id=5}", "/hooks/{id}")]
    [InlineData("/x/{**id}", "/hooks/{id}")]
    [InlineData("/{b}/{a}", "/{a}/{b}")]
    [InlineData("/only/literals", "/also/literals")]
    public void SameParameterNames_Pass(string configured, string @default)
        => EndpointPathValidator.EnsureSameParameters(typeof(HookEndpoint), configured, @default);

    /// <summary>The message names the endpoint, both patterns and what each side lacks.</summary>
    [Fact]
    public void RenamedParameter_Throws_NamingBothSides()
    {
        var failure = Assert.Throws<InvalidOperationException>(
            () => EndpointPathValidator.EnsureSameParameters(typeof(HookEndpoint), "/x/{key}", "/hooks/{id}"));

        Assert.Contains(typeof(HookEndpoint).FullName!, failure.Message);
        Assert.Contains("'/x/{key}' lacks {id}", failure.Message);
        Assert.Contains("'/hooks/{id}' lacks {key}", failure.Message);
    }

    [Fact]
    public void DroppedOrAddedParameter_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => EndpointPathValidator.EnsureSameParameters(typeof(HookEndpoint), "/x", "/hooks/{id}"));
        Assert.Throws<InvalidOperationException>(() => EndpointPathValidator.EnsureSameParameters(typeof(HookEndpoint), "/x/{id}/{extra}", "/hooks/{id}"));
    }

    /// <summary>A configured value that is not a route pattern fails startup the same way, naming the endpoint.</summary>
    [Fact]
    public void InvalidConfiguredPattern_Throws_InvalidOperationException()
    {
        var failure = Assert.Throws<InvalidOperationException>(
            () => EndpointPathValidator.EnsureSameParameters(typeof(HookEndpoint), "/x/{id", "/hooks/{id}"));

        Assert.Contains(typeof(HookEndpoint).FullName!, failure.Message);
        Assert.Contains("GetPath", failure.Message);
    }
}
