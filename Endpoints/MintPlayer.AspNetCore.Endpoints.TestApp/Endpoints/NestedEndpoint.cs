using MintPlayer.AspNetCore.Endpoints.TestApp.Models;

namespace MintPlayer.AspNetCore.Endpoints.TestApp.Endpoints;

/// <summary>
/// A container type whose only job is to nest an endpoint inside it.
/// </summary>
/// <remarks>
/// This shape used to emit code that did not compile. The generator wrote the endpoint's partial
/// declaration into a flat <c>namespace { }</c> block without reopening the containing type, so the
/// emitted <c>partial class NestedGetUser</c> referred to a type that did not exist at that scope.
/// Nothing in the sample app or the fixture corpus was nested, which is why a suite of 988 tests
/// did not catch it.
/// <para>
/// It is a sample rather than only a fixture on purpose: the generator test harness runs the
/// generator in-process, so it proves the emitted text is right, but only a real build proves the
/// emitted text is <i>accepted by the compiler</i> in a real project.
/// </para>
/// </remarks>
/// <remarks>
/// The container itself must be <c>partial</c>. Without it the generated file cannot reopen the
/// type, and the compiler reports <c>CS0260 Missing partial modifier</c> against the consumer's own
/// declaration — an error that says nothing about endpoints. That is what MPEP019 exists to replace.
/// </remarks>
public static partial class NestedContainer
{
    /// <summary>
    /// GET /api/users/nested/{id} — identical in behaviour to <see cref="GetUser"/>, and declared
    /// inside a containing type so the nesting is exercised end to end.
    /// </summary>
    [MemberOf<UsersApi>]
    public partial class NestedGetUser : IGetEndpoint<GetUserRequest, UserResponse>
    {
        public static string Path => "/nested/{id}";

        protected override ValueTask<GetUserRequest?> BindRequestAsync(HttpContext context)
        {
            if (!int.TryParse(context.Request.RouteValues["id"]?.ToString(), out var id))
                throw new EndpointBindingException(StatusCodes.Status400BadRequest, "The id must be an integer.");

            return ValueTask.FromResult<GetUserRequest?>(new GetUserRequest(id));
        }

        public override Task<IResult> HandleAsync(GetUserRequest request, CancellationToken ct)
            => Task.FromResult(Results.Ok(new UserResponse(request.Id, "Nested", "nested@example.com")));
    }
}
