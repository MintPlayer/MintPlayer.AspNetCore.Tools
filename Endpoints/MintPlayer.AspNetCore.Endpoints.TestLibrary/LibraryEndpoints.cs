namespace MintPlayer.AspNetCore.Endpoints.TestLibrary;

/// <summary>The library's user type. The application derives its own and closes the endpoints with it.</summary>
public class LibUser
{
    /// <summary>What the endpoints report as the current user.</summary>
    public virtual string DisplayName => GetType().Name;
}

/// <summary>/lib/auth — always mapped.</summary>
public class LibAuthGroup : IEndpointGroup
{
    public static string Prefix => "/lib/auth";

    static void IEndpointGroup.Configure(RouteGroupBuilder group) => group.WithTags("Library");
}

/// <summary>
/// /lib/auth/passkeys — mapped only when <see cref="EnabledKey"/> is not <c>false</c> (PRD D5): the
/// shape of an optional cluster of library endpoints that its options switch off.
/// </summary>
[MemberOf<LibAuthGroup>]
public class LibPasskeysGroup : IEndpointGroup
{
    /// <summary>The configuration key that switches this group off.</summary>
    public const string EnabledKey = "TestLibrary:PasskeysEnabled";

    public static string Prefix => "/passkeys";

    static bool IEndpointGroup.IsEnabled(IServiceProvider services)
        => services.GetRequiredService<IConfiguration>().GetValue(EnabledKey, true);
}

/// <summary>
/// GET /lib/auth/passkeys/{id} — generic over the user type, with a route-bound property whose binder
/// this library's generator emits into the open class.
/// </summary>
[MemberOf<LibPasskeysGroup>]
public partial class Passkeys<TUser> : IGetEndpoint<string> where TUser : LibUser, new()
{
    public static string Path => "/{id}";

    [RouteParam] public int Id { get; set; }

    public override Task<IResult> HandleAsync(CancellationToken ct)
        => Task.FromResult(Results.Ok($"{new TUser().DisplayName}:{Id}"));
}

/// <summary>GET /lib/auth/whoami — a raw generic endpoint.</summary>
[MemberOf<LibAuthGroup>]
public class WhoAmI<TUser> : IGetEndpoint where TUser : LibUser, new()
{
    public static string Path => "/whoami";

    public Task<IResult> HandleAsync(HttpContext httpContext)
        => Task.FromResult(Results.Ok(new TUser().DisplayName));
}

/// <summary>GET /lib/echo — no constraint type to key on, so the application closes it explicitly.</summary>
public class Echo<TPayload> : IGetEndpoint where TPayload : class
{
    public static string Path => "/lib/echo";

    public Task<IResult> HandleAsync(HttpContext httpContext)
        => Task.FromResult(Results.Ok(typeof(TPayload).Name));
}
