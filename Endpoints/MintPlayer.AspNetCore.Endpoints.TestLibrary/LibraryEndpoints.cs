using System.Runtime.CompilerServices;

namespace MintPlayer.AspNetCore.Endpoints.TestLibrary;

/// <summary>The library's user type. The application derives its own and closes the endpoints with it.</summary>
public class LibUser
{
    /// <summary>What the endpoints report as the current user.</summary>
    public virtual string DisplayName => GetType().Name;
}

/// <summary>
/// /lib/auth — always mapped. Its <c>Configure</c> adds the <c>LibraryAudit</c> tag only when
/// <see cref="AuditTagKey"/> is <c>true</c> (#36): a convention that depends on configuration, on a
/// group whose routes exist either way.
/// </summary>
public class LibAuthGroup : IEndpointGroup
{
    /// <summary>The configuration key that adds the <c>LibraryAudit</c> tag.</summary>
    public const string AuditTagKey = "TestLibrary:AuditTag";

    public static string Prefix => "/lib/auth";

    static void IEndpointGroup.Configure(RouteGroupBuilder group, IServiceProvider services)
    {
        group.WithTags("Library");

        if (services.GetRequiredService<IConfiguration>().GetValue(AuditTagKey, false))
            group.WithTags("LibraryAudit");
    }
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

/// <summary>
/// GET /lib/auth/hooks/{id} by default; a group-relative route configured at <see cref="PathKey"/>
/// moves it (#37). Generic, so the application closes it and maps it on the configured route — the
/// open-endpoint record carries <c>PathConfigurable</c>.
/// </summary>
[MemberOf<LibAuthGroup>]
public partial class ConfiguredHook<TUser> : IGetEndpoint<string> where TUser : LibUser, new()
{
    /// <summary>The configuration key whose value, when set, replaces <see cref="Path"/>.</summary>
    public const string PathKey = "TestLibrary:HookPath";

    public static string Path => "/hooks/{id}";

    /// <summary>The configured route, or null for <see cref="Path"/>.</summary>
    public static string? GetPath(IServiceProvider services)
        => services.GetRequiredService<IConfiguration>()[PathKey];

    [RouteParam] public int Id { get; set; }

    public override Task<IResult> HandleAsync(CancellationToken ct)
        => Task.FromResult(Results.Ok($"hook:{new TUser().DisplayName}:{Id}"));
}

/// <summary>
/// Counts the map-time hook calls of <see cref="ManageAccount{TUser}"/> per application (keyed on its
/// root provider), so a test can assert that a disabled endpoint's <c>GetPath</c> and <c>Configure</c>
/// never ran — without racing other hosts mapping the same app in parallel.
/// </summary>
public static class ManageAccountCalls
{
    /// <summary>The configuration key that switches <see cref="ManageAccount{TUser}"/> on; off by default.</summary>
    public const string EnabledKey = "TestLibrary:ManageEnabled";

    private static readonly ConditionalWeakTable<IServiceProvider, Counts> calls = new();

    /// <summary>How often the hooks ran for the application whose root provider is <paramref name="services"/>.</summary>
    public static Counts For(IServiceProvider services) => calls.GetValue(services, _ => new Counts());

    /// <summary>One application's hook calls.</summary>
    public sealed class Counts
    {
        /// <summary>Calls to <c>GetPath</c>.</summary>
        public int GetPath;

        /// <summary>Calls to <c>Configure</c>.</summary>
        public int Configure;
    }
}

/// <summary>
/// GET /lib/auth/manage — an endpoint-level <c>IsEnabled</c> (PRD R5): its group is always mapped, and
/// this endpoint only when <see cref="ManageAccountCalls.EnabledKey"/> is <c>true</c>. Its
/// <c>GetPath</c> and <c>Configure</c> only count their calls, so a disabled endpoint can be shown to
/// skip them.
/// </summary>
[MemberOf<LibAuthGroup>]
public class ManageAccount<TUser> : IGetEndpoint where TUser : LibUser, new()
{
    public static string Path => "/manage";

    static bool IEndpointBase.IsEnabled(IServiceProvider services)
        => services.GetRequiredService<IConfiguration>().GetValue(ManageAccountCalls.EnabledKey, false);

    public static string? GetPath(IServiceProvider services)
    {
        Interlocked.Increment(ref ManageAccountCalls.For(services).GetPath);
        return null;
    }

    public static void Configure(RouteHandlerBuilder builder, IServiceProvider services)
        => Interlocked.Increment(ref ManageAccountCalls.For(services).Configure);

    public Task<IResult> HandleAsync(HttpContext httpContext)
        => Task.FromResult(Results.Ok(new TUser().DisplayName));
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
