namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests.Infrastructure;

/// <summary>
/// Fixture sources shared by more than one generator suite.
/// </summary>
/// <remarks>
/// <see cref="Corpus"/> mirrors the sample TestApp, which is the project's own statement of what
/// it supports. Keeping one copy here means a shape added to the sample can be added here once and
/// picked up by every suite that walks the corpus, rather than drifting per test class.
/// </remarks>
internal static class FixtureSources
{
    /// <summary>Namespace every fixture type lives in.</summary>
    public const string Ns = "Fixtures";

    /// <summary>
    /// Every endpoint shape the library claims to support: raw, raw multi-method, raw in a group,
    /// typed, typed-with-response, a <c>SuccessStatusCode</c> override of 201, and a two-level
    /// group nesting with a root group that is only ever referenced as a parent. Since M4 it also
    /// carries the binding shapes: a response-only GET (<c>IGetEndpoint&lt;TResponse&gt;</c>) with a
    /// <c>[RouteParam]</c>, a raw DELETE carrying a <c>[RouteParam]</c> (so it needs a generated
    /// <c>IParameterBinder</c> partial), a raw list endpoint with a defaulted <c>[QueryParam]</c>, and
    /// body endpoints whose route value lives on the endpoint rather than in the body type.
    /// </summary>
    public const string Corpus = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using MintPlayer.AspNetCore.Endpoints;

        namespace Fixtures;

        public record UserResponse(int Id, string Name);
        public record CreateUserRequest(string Name);
        public record CreateUserResponse(int Id, string Name);
        public record UpdateUserBody(string Name);

        public class ApiGroup : IEndpointGroup
        {
            public static string Prefix => "/api";
        }

        [MemberOf<ApiGroup>]
        public class UsersApi : IEndpointGroup
        {
            public static string Prefix => "/users";
            static void IEndpointGroup.Configure(RouteGroupBuilder group) => group.WithTags("Users");
        }

        [MemberOf<ApiGroup>]
        public class ProductsApi : IEndpointGroup
        {
            public static string Prefix => "/products";
        }

        public class HealthCheck : IGetEndpoint
        {
            public static string Path => "/health";
            public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
        }

        public class PreflightEndpoint : IEndpoint
        {
            public static string Path => "/api/{**path}";
            public static IEnumerable<string> Methods => ["OPTIONS", "HEAD"];
            public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
        }

        [MemberOf<UsersApi>]
        public partial class ListUsers : IGetEndpoint
        {
            public static string Path => "/";

            [QueryParam] public int Page { get; set; } = 1;

            public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok(Page));
        }

        [MemberOf<UsersApi>]
        public partial class GetUser : IGetEndpoint<UserResponse>
        {
            public static string Path => "/{id}";

            [RouteParam] public int Id { get; set; }

            public override Task<IResult> HandleAsync(CancellationToken ct)
                => Task.FromResult(Results.Ok(new UserResponse(Id, "Alice")));
        }

        [MemberOf<UsersApi>]
        public partial class CreateUser : IPostEndpoint<CreateUserRequest, CreateUserResponse>
        {
            public static string Path => "/";

            static int IEndpoint<CreateUserRequest, CreateUserResponse>.SuccessStatusCode => 201;

            public override Task<IResult> HandleAsync(CreateUserRequest request, CancellationToken ct)
                => Task.FromResult(Results.Ok(new CreateUserResponse(1, request.Name)));
        }

        [MemberOf<UsersApi>]
        public partial class UpdateUser : IPutEndpoint<UpdateUserBody>
        {
            public static string Path => "/{id}";

            [RouteParam] public int Id { get; set; }

            public override Task<IResult> HandleAsync(UpdateUserBody request, CancellationToken ct)
                => Task.FromResult(Results.Ok());
        }

        [MemberOf<UsersApi>]
        public partial class PatchUser : IPatchEndpoint<UpdateUserBody, UserResponse>
        {
            public static string Path => "/{id}/patch";

            [RouteParam] public int Id { get; set; }

            public override Task<IResult> HandleAsync(UpdateUserBody request, CancellationToken ct)
                => Task.FromResult(Results.Ok(new UserResponse(Id, request.Name)));
        }

        [MemberOf<UsersApi>]
        public partial class DeleteUser : IDeleteEndpoint
        {
            public static string Path => "/{id}";

            [RouteParam] public int Id { get; set; }

            public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.NoContent());
        }

        [MemberOf<ProductsApi>]
        public class ListProducts : IGetEndpoint
        {
            public static string Path => "/";
            public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
        }
        """;

    /// <summary>A single raw GET endpoint outside any group — the smallest thing that emits.</summary>
    public const string RawGetEndpoint = """
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Http;
        using MintPlayer.AspNetCore.Endpoints;

        namespace Fixtures;

        public class HealthCheck : IGetEndpoint
        {
            public static string Path => "/health";
            public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
        }
        """;

    /// <summary>An assembly-level attribute source, for the method-name override tests.</summary>
    public static string MethodNameOverride(string name) => $"""
        using MintPlayer.AspNetCore.Endpoints;

        [assembly: EndpointsMethodName("{name}")]
        """;
}
