using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.AspNetCore.MustChangePassword.Abstractions;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.MustChangePassword.Fakes;

/// <summary>
/// A container wired the way a consuming application wires it — a real
/// <c>UserManager&lt;TestUser&gt;</c> over <see cref="TestUserStore"/>, the library's own
/// <c>AddMustChangePassword</c> registration, and a <see cref="RecordingAuthenticationService"/>
/// reachable through <c>HttpContext.RequestServices</c>.
/// </summary>
/// <remarks>
/// <c>IHttpContextAccessor</c> comes from <c>AddMustChangePassword</c> itself — the library registers
/// the dependency of the service it registers (D-M22 fixed, asserted in
/// <c>AddMustChangePasswordServiceRegistrationTests</c>), so no test has to paper over it.
/// </remarks>
internal sealed class MustChangePasswordTestHarness : IDisposable
{
    // Synthetic fixture credentials. Every credential-shaped string literal in this test area
    // lives here, deliberately, so there is exactly one place to look.
    //
    // They have to look like real passwords: the harness builds a real UserManager with the
    // default Identity password policy, so anything simpler is rejected by PasswordValidator
    // and the tests would be asserting validation failures rather than the flow under test.
    // That shape also makes automated secret scanners flag them — centralising them here is
    // what makes a scanner hit quick to triage instead of a hunt through six files.

    /// <summary>The account's starting password. Satisfies the default Identity policy.</summary>
    public const string InitialPassword = "Old1!pass";

    /// <summary>The password the user is changing to.</summary>
    public const string NewPassword = "New1!pass";

    /// <summary>A third value, for "confirmation does not match" cases.</summary>
    public const string MismatchedPassword = "Different1!pass";

    /// <summary>A value that is no longer current, for replay/stale-credential cases.</summary>
    public const string StalePassword = "Stale1!pass";

    /// <summary>A further distinct value, for a second change attempt.</summary>
    public const string SecondNewPassword = "Third1!pass";

    private readonly ServiceProvider root;
    private readonly IServiceScope scope;

    public MustChangePasswordTestHarness()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton<TestUserData>();
        services.AddIdentityCore<TestUser>().AddUserStore<TestUserStore>();

        services.AddSingleton<RecordingPasswordValidator>();
        services.AddSingleton<IPasswordValidator<TestUser>>(sp => sp.GetRequiredService<RecordingPasswordValidator>());

        services.AddSingleton<RecordingAuthenticationService>();
        services.AddSingleton<IAuthenticationService>(sp => sp.GetRequiredService<RecordingAuthenticationService>());
        services.AddMustChangePassword<TestUser, string>();

        root = services.BuildServiceProvider();
        scope = root.CreateScope();

        Authentication = root.GetRequiredService<RecordingAuthenticationService>();
        PasswordValidator = root.GetRequiredService<RecordingPasswordValidator>();
        UserData = root.GetRequiredService<TestUserData>();
        UserManager = scope.ServiceProvider.GetRequiredService<UserManager<TestUser>>();
        Accessor = root.GetRequiredService<IHttpContextAccessor>();
        HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        Accessor.HttpContext = HttpContext;
        Service = scope.ServiceProvider.GetRequiredService<IMustChangePasswordService<TestUser, string>>();
    }

    public RecordingAuthenticationService Authentication { get; }

    public RecordingPasswordValidator PasswordValidator { get; }

    public TestUserData UserData { get; }

    public UserManager<TestUser> UserManager { get; }

    public IHttpContextAccessor Accessor { get; }

    public DefaultHttpContext HttpContext { get; }

    public IMustChangePasswordService<TestUser, string> Service { get; }

    /// <summary>Creates a user through the real <c>UserManager</c>, so the hash is a real hash.</summary>
    public async Task<TestUser> SeedUserAsync(string userName = "alice", string? email = "alice@example.com", string password = InitialPassword)
    {
        var user = new TestUser { UserName = userName, Email = email };
        var result = await UserManager.CreateAsync(user, password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Code)));
        return user;
    }

    public void Dispose()
    {
        scope.Dispose();
        root.Dispose();
    }
}
