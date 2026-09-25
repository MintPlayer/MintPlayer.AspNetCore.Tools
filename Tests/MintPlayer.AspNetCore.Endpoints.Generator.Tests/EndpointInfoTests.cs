using System.Collections.Immutable;
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
        bool baseChainReachesEndpointBase = false,
        string? descriptorName = null,
        string? route = null,
        ImmutableArray<BoundProperty> boundProperties = default)
        => new(
            "global::Fixtures.MyEndpoint", "Fixtures", "MyEndpoint",
            isPartial, hasExistingBaseClass,
            level, httpMethod,
            requestTypeFqn, responseTypeFqn,
            groupTypeFqn,
            baseChainReachesEndpointBase, descriptorName,
            route: route,
            boundProperties: boundProperties);

    private static BoundProperty Bound(string name = "Id", string key = "Id", bool hasInitializer = false)
        => new(name, key, BoundSource.Route, BoundKind.Parsable, "global::System.Int32", "int",
            isOptional: false, hasInitializer: hasInitializer, isSettable: true, location: null);

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
        // HasMultipleGroups used to be compared here. It is gone with MPEP003: two memberships on one
        // type is CS0579, so there is no longer a second group to record.
        Assert.NotEqual(info, Info(EndpointLevel.Typed, HttpMethodKind.Post, baseChainReachesEndpointBase: true));
        Assert.NotEqual(info, Info(EndpointLevel.Typed, HttpMethodKind.Post, descriptorName: "Named"));
        Assert.NotEqual(info, Info(EndpointLevel.Typed, HttpMethodKind.Post, route: "/users"));
        Assert.False(info.Equals(null));
        Assert.False(info.Equals("global::Fixtures.MyEndpoint"));
    }

    /// <summary>
    /// The contract the incremental pipeline needs from the generated equality (PRD D17): equal
    /// models have equal hash codes, and a changed member makes them unequal.
    /// </summary>
    /// <remarks>
    /// This used to pin a hash derived from the fully qualified name only, on the claim that a
    /// <c>GroupBy</c> depended on it. None does: every <c>GroupBy</c> keys on the name string, and no
    /// model is hashed into a collection. The hash is now whatever <c>[GenerateEquality]</c>
    /// generates, which is free to include every compared member.
    /// </remarks>
    [Fact]
    public void GetHashCode_IsEqualForEqualModels()
    {
        var bound = ImmutableArray.Create(Bound());

        Assert.Equal(
            Info(EndpointLevel.Typed, HttpMethodKind.Post, route: "/users", boundProperties: bound).GetHashCode(),
            Info(EndpointLevel.Typed, HttpMethodKind.Post, route: "/users", boundProperties: ImmutableArray.Create(Bound())).GetHashCode());
        Assert.NotEqual(
            Info(EndpointLevel.Typed, HttpMethodKind.Post, boundProperties: bound),
            Info(EndpointLevel.Typed, HttpMethodKind.Post, boundProperties: ImmutableArray.Create(Bound(hasInitializer: true))));
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

    /// <summary>
    /// The response-only rung (<c>IGetEndpoint&lt;TResponse&gt;</c>, <c>IDeleteEndpoint&lt;TResponse&gt;</c>)
    /// has no request type, so it cannot use a per-verb <c>…Endpoint&lt;TRequest&gt;</c> base; it gets
    /// the non-generic <c>ResponseEndpoint</c>.
    /// </summary>
    /// <remarks>
    /// Before M4 the arity-1 GET meant "request" and mapped to <c>GetEndpoint&lt;TRequest&gt;</c>. A
    /// regression to that mapping would feed the response type in as a request and make the
    /// consumer's <c>override HandleAsync(CancellationToken)</c> a CS0115.
    /// </remarks>
    [Fact]
    public void GetBaseClassName_ForResponseOnlyEndpoint_IsResponseEndpoint()
    {
        var info = Info(EndpointLevel.ResponseOnly, HttpMethodKind.Get, requestTypeFqn: null, responseTypeFqn: "global::Fixtures.Response");

        Assert.Equal("global::MintPlayer.AspNetCore.Endpoints.ResponseEndpoint", info.GetBaseClassName());
    }

    /// <summary>
    /// Bound properties are part of the cached model and are compared <b>by sequence</b>, not by
    /// array reference.
    /// </summary>
    /// <remarks>
    /// <see cref="ImmutableArray{T}"/>'s own <c>Equals</c> compares the backing array by reference,
    /// so two runs over an unchanged compilation — which always build fresh arrays — would never be
    /// equal, and every keystroke would re-emit. The opposite mistake, leaving the array out of the
    /// comparison, would keep a stale binder after a property is renamed. Both are silent.
    /// </remarks>
    [Fact]
    public void Equals_ComparesBoundPropertiesBySequence()
    {
        var info = Info(EndpointLevel.ResponseOnly, HttpMethodKind.Get, boundProperties: [Bound()]);

        // A distinct array holding distinct-but-equal elements.
        Assert.Equal(info, Info(EndpointLevel.ResponseOnly, HttpMethodKind.Get, boundProperties: [Bound()]));

        Assert.NotEqual(info, Info(EndpointLevel.ResponseOnly, HttpMethodKind.Get));
        Assert.NotEqual(info, Info(EndpointLevel.ResponseOnly, HttpMethodKind.Get, boundProperties: [Bound(key: "userId")]));
        Assert.NotEqual(info, Info(EndpointLevel.ResponseOnly, HttpMethodKind.Get, boundProperties: [Bound(hasInitializer: true)]));
        Assert.NotEqual(info, Info(EndpointLevel.ResponseOnly, HttpMethodKind.Get, boundProperties: [Bound(), Bound("Page", "Page")]));

        // default and empty are the same "no bound properties".
        Assert.Equal(
            Info(EndpointLevel.Raw, HttpMethodKind.Get),
            Info(EndpointLevel.Raw, HttpMethodKind.Get, boundProperties: ImmutableArray<BoundProperty>.Empty));
    }

    [Fact]
    public void GroupInfo_Equals_ComparesEveryMember()
    {
        var group = new GroupInfo("global::Fixtures.UsersApi", "global::Fixtures.ApiGroup", prefix: "/users");

        Assert.Equal(group, new GroupInfo("global::Fixtures.UsersApi", "global::Fixtures.ApiGroup", prefix: "/users"));
        Assert.NotEqual(group, new GroupInfo("global::Fixtures.OtherApi", "global::Fixtures.ApiGroup", prefix: "/users"));
        Assert.NotEqual(group, new GroupInfo("global::Fixtures.UsersApi", null, prefix: "/users"));
        // HasMultipleParents used to be compared here; it went with MPEP004, since a group with two
        // parents is now CS0579. The prefix is the member that took its place in the comparison.
        Assert.NotEqual(group, new GroupInfo("global::Fixtures.UsersApi", "global::Fixtures.ApiGroup", prefix: "/other"));
        Assert.False(group.Equals(null));
        Assert.False(group.Equals("global::Fixtures.UsersApi"));
    }
}
