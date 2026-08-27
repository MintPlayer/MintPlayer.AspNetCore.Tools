using Xunit;

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests;

/// <summary>
/// <see cref="EndpointInfo.GetBaseClassName"/> and the equality that the incremental pipeline
/// caches on.
/// </summary>
/// <remarks>
/// The base class name is what the generator injects into the user's partial declaration, so a
/// wrong verb here is a CS0115 in the consumer's own file. The mapping is a plain switch, which is
/// exactly the kind of code where a copy-paste slip survives review.
/// </remarks>
public class EndpointInfoTests
{
    private static EndpointInfo Info(
        EndpointLevel level,
        HttpMethodKind httpMethod,
        string? requestTypeFqn = "global::Fixtures.Request",
        string? responseTypeFqn = null,
        bool isPartial = true,
        bool hasExistingBaseClass = false,
        string? groupTypeFqn = null,
        bool hasMultipleGroups = false,
        bool baseChainReachesEndpointBase = false,
        string? descriptorName = null)
        => new(
            "global::Fixtures.MyEndpoint", "Fixtures", "MyEndpoint",
            isPartial, hasExistingBaseClass,
            level, httpMethod,
            requestTypeFqn, responseTypeFqn,
            groupTypeFqn, hasMultipleGroups,
            baseChainReachesEndpointBase, descriptorName);

    /// <summary>A raw endpoint needs no base class — it handles <c>HttpContext</c> itself.</summary>
    [Fact]
    public void GetBaseClassName_ForRawEndpoint_IsNull()
    {
        Assert.Null(Info(EndpointLevel.Raw, HttpMethodKind.Get).GetBaseClassName());
    }

    // The verb arrives as a string because the enum is internal and a public test method cannot
    // take a less accessible parameter type.
    [Theory]
    [InlineData(nameof(HttpMethodKind.Get), "GetEndpoint")]
    [InlineData(nameof(HttpMethodKind.Post), "PostEndpoint")]
    [InlineData(nameof(HttpMethodKind.Put), "PutEndpoint")]
    [InlineData(nameof(HttpMethodKind.Patch), "PatchEndpoint")]
    [InlineData(nameof(HttpMethodKind.Delete), "DeleteEndpoint")]
    // A verb the generator could not infer falls back to the shared base, which leaves binding
    // abstract for the user to supply.
    [InlineData(nameof(HttpMethodKind.Custom), "EndpointBase")]
    public void GetBaseClassName_ForTypedEndpoint_PicksThePerVerbBase(string method, string expected)
    {
        var info = Info(EndpointLevel.Typed, Enum.Parse<HttpMethodKind>(method));

        Assert.Equal($"global::MintPlayer.AspNetCore.Endpoints.{expected}<global::Fixtures.Request>", info.GetBaseClassName());
    }

    /// <summary>
    /// The response type is deliberately absent from the base class: only the request type is a
    /// generic parameter, the response type is used for <c>Produces</c> instead.
    /// </summary>
    [Fact]
    public void GetBaseClassName_ForTypedWithResponse_IsGenericOnTheRequestTypeOnly()
    {
        var info = Info(EndpointLevel.TypedWithResponse, HttpMethodKind.Post, responseTypeFqn: "global::Fixtures.Response");

        Assert.Equal(
            "global::MintPlayer.AspNetCore.Endpoints.PostEndpoint<global::Fixtures.Request>",
            info.GetBaseClassName());
    }

    [Fact]
    public void Equals_ComparesEverySignificantMember()
    {
        var info = Info(EndpointLevel.Typed, HttpMethodKind.Post);

        Assert.Equal(info, Info(EndpointLevel.Typed, HttpMethodKind.Post));
        Assert.NotEqual(info, Info(EndpointLevel.TypedWithResponse, HttpMethodKind.Post));
        Assert.NotEqual(info, Info(EndpointLevel.Typed, HttpMethodKind.Put));
        Assert.NotEqual(info, Info(EndpointLevel.Typed, HttpMethodKind.Post, requestTypeFqn: "global::Fixtures.Other"));
        Assert.NotEqual(info, Info(EndpointLevel.Typed, HttpMethodKind.Post, responseTypeFqn: "global::Fixtures.Response"));
        Assert.NotEqual(info, Info(EndpointLevel.Typed, HttpMethodKind.Post, isPartial: false));
        Assert.NotEqual(info, Info(EndpointLevel.Typed, HttpMethodKind.Post, hasExistingBaseClass: true));
        Assert.NotEqual(info, Info(EndpointLevel.Typed, HttpMethodKind.Post, groupTypeFqn: "global::Fixtures.ApiGroup"));
        Assert.NotEqual(info, Info(EndpointLevel.Typed, HttpMethodKind.Post, hasMultipleGroups: true));
        Assert.NotEqual(info, Info(EndpointLevel.Typed, HttpMethodKind.Post, baseChainReachesEndpointBase: true));
        Assert.NotEqual(info, Info(EndpointLevel.Typed, HttpMethodKind.Post, descriptorName: "Named"));
        Assert.False(info.Equals(null));
        Assert.False(info.Equals("global::Fixtures.MyEndpoint"));
    }

    /// <summary>
    /// Only the fully qualified name feeds the hash code, so two infos for the same class with
    /// different details collide. That is legal, and it is what makes the duplicate infos a partial
    /// class split across files produces land in the same <c>GroupBy</c> bucket.
    /// </summary>
    [Fact]
    public void GetHashCode_IsDerivedFromTheFullyQualifiedNameOnly()
    {
        Assert.Equal(
            Info(EndpointLevel.Typed, HttpMethodKind.Post).GetHashCode(),
            Info(EndpointLevel.Raw, HttpMethodKind.Delete, hasExistingBaseClass: true).GetHashCode());
    }

    /// <summary>
    /// The descriptor name comes from <c>[EndpointDescriptorName]</c>, and falls back to the class
    /// name — which is what the generator emitted unconditionally before the attribute was read.
    /// </summary>
    [Fact]
    public void EffectiveDescriptorName_PrefersTheAttributeThenTheClassName()
    {
        Assert.Equal("MyEndpoint", Info(EndpointLevel.Raw, HttpMethodKind.Get).EffectiveDescriptorName);
        Assert.Equal("Named", Info(EndpointLevel.Raw, HttpMethodKind.Get, descriptorName: "Named").EffectiveDescriptorName);
    }

    [Fact]
    public void GroupInfo_Equals_ComparesEveryMember()
    {
        var group = new GroupInfo("global::Fixtures.UsersApi", "global::Fixtures.ApiGroup", false);

        Assert.Equal(group, new GroupInfo("global::Fixtures.UsersApi", "global::Fixtures.ApiGroup", false));
        Assert.NotEqual(group, new GroupInfo("global::Fixtures.OtherApi", "global::Fixtures.ApiGroup", false));
        Assert.NotEqual(group, new GroupInfo("global::Fixtures.UsersApi", null, false));
        Assert.NotEqual(group, new GroupInfo("global::Fixtures.UsersApi", "global::Fixtures.ApiGroup", true));
        Assert.False(group.Equals(null));
        Assert.False(group.Equals("global::Fixtures.UsersApi"));
    }
}
