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
    /// <summary>A password that satisfies the default Identity password policy.</summary>
    public const string InitialPassword = "Old1!pass";

    /// <summary>A different password that also satisfies the default policy.</summary>
    public const string NewPassword = "New1!pass";

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
