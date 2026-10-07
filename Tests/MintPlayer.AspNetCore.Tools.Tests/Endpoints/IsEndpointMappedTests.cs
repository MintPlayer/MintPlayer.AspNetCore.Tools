using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.AspNetCore.Endpoints;
using Xunit;
using static MintPlayer.AspNetCore.Tools.Tests.Endpoints.MapEndpointGenericTests;

namespace MintPlayer.AspNetCore.Tools.Tests.Endpoints;

/// <summary>
/// Issue #38 (AC2, AC6): <c>IsEndpointMapped</c> answers by endpoint class — closed types by equality,
/// generic definitions through the class's own type and its base classes — never by route string.
/// </summary>
public class IsEndpointMappedTests
{
    /// <summary>Unsealed, so a non-generic class can close it by derivation (unlike <see cref="Echo{T}"/>).</summary>
    public class EchoBase<T> : IGetEndpoint
    {
        public static string Path => "/echo-base";

        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok(typeof(T).Name));
    }

    public sealed class EchoString : EchoBase<string>;

    /// <summary>The real endpoint the impostors imitate. Never mapped.</summary>
    public sealed class SignIn : IPostEndpoint
    {
        public static string Path => "/sign-in";

        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
    }

    public sealed class Mapped : IGetEndpoint
    {
        public static string Path => "/mapped";

        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
    }

    /// <summary>
    /// Every endpoint mapped on <paramref name="map"/>, read after all mapping and conventions (PRD D3:
    /// reading the route builder's sources any earlier would freeze them).
    /// </summary>
    private static EndpointDataSource Map(Action<WebApplication> map)
    {
        var app = WebApplication.CreateBuilder([]).Build();
        map(app);

        return new CompositeEndpointDataSource(((IEndpointRouteBuilder)app).DataSources);
    }

    [Fact]
    public void ClosedType_MatchesByEquality()
    {
        var endpoints = Map(app => app.MapEndpoint<Mapped>());

        Assert.True(endpoints.IsEndpointMapped<Mapped>());
        Assert.True(endpoints.IsEndpointMapped(typeof(Mapped)));
        Assert.False(endpoints.IsEndpointMapped<SignIn>());
    }

    /// <summary>The Spark shape: <c>MapEndpoint&lt;X&lt;TUser&gt;&gt;()</c>, asked about as <c>X&lt;&gt;</c>.</summary>
    [Fact]
    public void GenericDefinition_MatchesAClosing()
    {
        var endpoints = Map(app => app.MapEndpoint<Echo<string>>());

        Assert.True(endpoints.IsEndpointMapped(typeof(Echo<>)));
        Assert.True(endpoints.IsEndpointMapped<Echo<string>>());
        Assert.False(endpoints.IsEndpointMapped<Echo<int>>());
    }

    /// <summary>PRD D2: a derived closing is a mapping of the generic endpoint; a closed base is not the query.</summary>
    [Fact]
    public void GenericDefinition_MatchesADerivedClosing_ButAClosedBaseDoesNot()
    {
        var endpoints = Map(app => app.MapEndpoint<EchoString>());

        Assert.True(endpoints.IsEndpointMapped<EchoString>());
        Assert.True(endpoints.IsEndpointMapped(typeof(EchoBase<>)));
        Assert.False(endpoints.IsEndpointMapped<EchoBase<string>>());
    }

    [Fact]
    public void NestedInAGenericType_FollowsTheClr()
    {
        var endpoints = Map(app => app.MapEndpoint<Outer<string>.Inner>());

        Assert.True(endpoints.IsEndpointMapped(typeof(Outer<>.Inner)));
        Assert.False(endpoints.IsEndpointMapped(typeof(Outer<>)));
    }

    [Fact]
    public void EndpointInADisabledGroup_IsNotMapped()
    {
        var endpoints = Map(app =>
        {
            app.MapEndpoint<InDisabledGroup>();
            app.MapEndpoint<Mapped>();
        });

        Assert.False(endpoints.IsEndpointMapped<InDisabledGroup>());
        Assert.True(endpoints.IsEndpointMapped<Mapped>()); // control: the same Map call did map
        Assert.Single(endpoints.Endpoints);
    }

    /// <summary>
    /// The prototype's impostor probe: a class with the same simple name in another namespace, and a
    /// plain lambda on the real endpoint's route. Neither is the endpoint class, so neither answers.
    /// </summary>
    [Fact]
    public void SameNameClass_AndLambdaOnTheSameRoute_DoNotMatch()
    {
        var endpoints = Map(app =>
        {
            app.MapEndpoint<Impostors.SignIn>();
            app.MapPost(SignIn.Path, () => Results.Ok());
        });

        Assert.Equal(2, endpoints.Endpoints.Count);
        Assert.True(endpoints.IsEndpointMapped<Impostors.SignIn>());
        Assert.False(endpoints.IsEndpointMapped<SignIn>());
    }

    public interface IMarker<T>;

    public sealed class Marked : IGetEndpoint, IMarker<string>
    {
        public static string Path => "/marked";

        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
    }

    /// <summary>
    /// Interfaces are never walked, or a query for <c>IGetEndpoint</c> would match every GET. Asked of an
    /// endpoint that really implements both interfaces, so a walk would answer <see langword="true"/>.
    /// </summary>
    [Fact]
    public void Interfaces_NeverMatch()
    {
        var endpoints = Map(app => app.MapEndpoint<Marked>());

        Assert.True(typeof(IGetEndpoint).IsAssignableFrom(typeof(Marked)));
        Assert.False(endpoints.IsEndpointMapped(typeof(IGetEndpoint)));
        Assert.False(endpoints.IsEndpointMapped(typeof(IMarker<>)));
        Assert.False(endpoints.IsEndpointMapped<IMarker<string>>());
        Assert.True(endpoints.IsEndpointMapped<Marked>());
    }

    /// <summary>The endpoint's own <c>Configure</c> adds a second instance after <c>ForMetadata</c>'s.</summary>
    public sealed class Relabelled : IGetEndpoint
    {
        public static string Path => "/relabelled";

        static void IEndpointBase.Configure(RouteHandlerBuilder builder, IServiceProvider services)
            => builder.WithMetadata(new EndpointTypeMetadata(typeof(Mapped)));

        public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
    }

    /// <summary>Documented on <c>IsEndpointMapped</c>: each endpoint is read with <c>GetMetadata</c>, so the last instance wins.</summary>
    [Fact]
    public void SecondInstance_AddedByConfigure_Wins()
    {
        var endpoints = Map(app => app.MapEndpoint<Relabelled>());

        Assert.Equal(2, Assert.Single(endpoints.Endpoints).Metadata.GetOrderedMetadata<EndpointTypeMetadata>().Count);
        Assert.True(endpoints.IsEndpointMapped<Mapped>());
        Assert.False(endpoints.IsEndpointMapped<Relabelled>());
    }

    [Fact]
    public void Metadata_RejectsANullType()
        => Assert.Throws<ArgumentNullException>(() => new EndpointTypeMetadata(null!));

    [Fact]
    public void Arguments_AreValidated()
    {
        var endpoints = Map(app => app.MapEndpoint<EchoString>());

        Assert.Throws<ArgumentNullException>(() => endpoints.IsEndpointMapped(null!));
        Assert.Throws<ArgumentNullException>(() => ((EndpointDataSource)null!).IsEndpointMapped(typeof(Mapped)));

        // EchoBase<T> as seen from a generic subclass: contains generic parameters, but is no definition.
        var partiallyOpen = typeof(PartiallyOpen<>).BaseType!;
        Assert.True(partiallyOpen.ContainsGenericParameters && !partiallyOpen.IsGenericTypeDefinition);
        Assert.Throws<ArgumentException>(() => endpoints.IsEndpointMapped(partiallyOpen));
    }

    public class PartiallyOpen<T> : EchoBase<List<T>>;

    /// <summary>
    /// AC6 and README: the container's data source is empty until the host starts, so an early call
    /// answers <see langword="false"/> without an error. Pinned, because R6 documents it.
    /// </summary>
    [Fact]
    public async Task ContainerDataSource_IsCompleteOnlyAfterStart()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.MapEndpoint<Mapped>();
        var endpoints = app.Services.GetRequiredService<EndpointDataSource>();

        Assert.False(endpoints.IsEndpointMapped<Mapped>());

        await app.StartAsync();

        Assert.True(endpoints.IsEndpointMapped<Mapped>());
    }
}
