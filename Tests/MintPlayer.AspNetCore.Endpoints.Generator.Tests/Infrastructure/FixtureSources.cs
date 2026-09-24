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
    /// group nesting with a root group that is only ever referenced as a parent.
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

        public record GetUserRequest(int Id);
        public record UserResponse(int Id, string Name);
        public record CreateUserRequest(string Name);
        public record CreateUserResponse(int Id, string Name);
        public record UpdateUserRequest(int Id, string Name);

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
        public class ListUsers : IGetEndpoint
        {
            public static string Path => "/";
            public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());
        }

        [MemberOf<UsersApi>]
        public partial class GetUser : IGetEndpoint<GetUserRequest, UserResponse>
        {
            public static string Path => "/{id}";

            protected override ValueTask<GetUserRequest?> BindRequestAsync(HttpContext context)
                => ValueTask.FromResult<GetUserRequest?>(new GetUserRequest(1));

            public override Task<IResult> HandleAsync(GetUserRequest request, CancellationToken ct)
                => Task.FromResult(Results.Ok(new UserResponse(request.Id, "Alice")));
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
        public partial class UpdateUser : IPutEndpoint<UpdateUserRequest>
        {
            public static string Path => "/{id}";

            public override Task<IResult> HandleAsync(UpdateUserRequest request, CancellationToken ct)
                => Task.FromResult(Results.Ok());
        }

        [MemberOf<UsersApi>]
        public partial class PatchUser : IPatchEndpoint<UpdateUserRequest, UserResponse>
        {
            public static string Path => "/{id}/patch";

            public override Task<IResult> HandleAsync(UpdateUserRequest request, CancellationToken ct)
                => Task.FromResult(Results.Ok(new UserResponse(request.Id, request.Name)));
        }

        [MemberOf<UsersApi>]
        public partial class DeleteUser : IDeleteEndpoint<GetUserRequest>
        {
            public static string Path => "/{id}";

            protected override ValueTask<GetUserRequest?> BindRequestAsync(HttpContext context)
                => ValueTask.FromResult<GetUserRequest?>(new GetUserRequest(1));

            public override Task<IResult> HandleAsync(GetUserRequest request, CancellationToken ct)
                => Task.FromResult(Results.NoContent());
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
