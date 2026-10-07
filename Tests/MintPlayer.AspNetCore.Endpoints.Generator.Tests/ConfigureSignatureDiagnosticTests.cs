using Microsoft.CodeAnalysis;
using MintPlayer.AspNetCore.Endpoints.Generator.Tests.Infrastructure;
using Xunit;

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests;

/// <summary>
/// MPEP035 (#36, PRD R1.3, AC3): a one-argument <c>Configure(RouteGroupBuilder)</c> or
/// <c>Configure(RouteHandlerBuilder)</c> on a group or endpoint is no longer called since 11.4.
/// </summary>
/// <remarks>
/// The implicit shape is the one that matters: it still compiles, as an unrelated static method, and
/// its conventions silently stop applying — the failure mode #36 was about, this time caused by the
/// upgrade. Pinned from both sides: the new signature and an unrelated <c>Configure</c> stay silent.
/// </remarks>
public class ConfigureSignatureDiagnosticTests
{
    private const string Preamble = """
        using System;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using MintPlayer.AspNetCore.Endpoints;

        namespace Fixtures;
        """;

    private static string Fixture(string body) => Preamble + "\n\n" + body;

    private static Diagnostic[] Reported(string source)
        => [.. EndpointGeneratorHarness.Run("Fixtures", Fixture(source)).Diagnostics.Where(d => d.Id == "MPEP035")];

    private static string TextAt(Location location)
        => location.SourceTree!.GetText().ToString(location.SourceSpan);

    private const string Handler = "public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.Ok());";

    [Fact]
    public void ImplicitOneArgumentGroupHook_ReportsMPEP035_OnTheHook()
    {
        var diagnostic = Assert.Single(Reported($$"""
            public class UsersApi : IEndpointGroup
            {
                public static string Prefix => "/users";
                public static void Configure(RouteGroupBuilder group) => group.WithTags("Users");
            }

            [MemberOf<UsersApi>]
            public class ListUsers : IGetEndpoint
            {
                public static string Path => "/";
                {{Handler}}
            }
            """));

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("Configure", TextAt(diagnostic.Location));
        Assert.Equal(
            "'UsersApi' declares Configure(RouteGroupBuilder) without an IServiceProvider parameter. Since 11.4 it is no longer called. Add 'IServiceProvider services' as the second parameter.",
            diagnostic.GetMessage());
    }

    [Fact]
    public void ImplicitOneArgumentEndpointHook_ReportsMPEP035()
    {
        var diagnostic = Assert.Single(Reported($$"""
            public class Health : IGetEndpoint
            {
                public static string Path => "/health";
                public static void Configure(RouteHandlerBuilder builder) => builder.WithDisplayName("x");
                {{Handler}}
            }
            """));

        Assert.Contains("'Health' declares Configure(RouteHandlerBuilder)", diagnostic.GetMessage());
    }

    /// <summary>
    /// The explicit shape is already CS0539, but the fix is the same, so MPEP035 says what it is; a
    /// fully qualified parameter type is matched by its rightmost name.
    /// </summary>
    [Fact]
    public void ExplicitOrFullyQualifiedOneArgumentHook_ReportsMPEP035()
    {
        var source = Fixture($$"""
            public class Health : IGetEndpoint
            {
                public static string Path => "/health";
                static void IEndpointBase.Configure(global::Microsoft.AspNetCore.Builder.RouteHandlerBuilder builder) { }
                {{Handler}}
            }
            """);

        var diagnostics = EndpointGeneratorHarness.RunAndCompile("Fixtures", source);

        Assert.Contains(diagnostics, d => d.Id == "CS0539");
        Assert.Single(diagnostics, d => d.Id == "MPEP035");
    }

    /// <summary>
    /// A hook on an abstract base class is found through the endpoints deriving from it — the base is
    /// never discovered on its own — and reported once, naming the base.
    /// </summary>
    [Fact]
    public void HookOnASharedBaseClass_IsReportedOnce_NamingTheBase()
    {
        var diagnostic = Assert.Single(Reported($$"""
            public abstract class AuditedEndpoint
            {
                public static void Configure(RouteHandlerBuilder builder) => builder.WithDisplayName("audited");
            }

            public class First : AuditedEndpoint, IGetEndpoint
            {
                public static string Path => "/first";
                {{Handler}}
            }

            public class Second : AuditedEndpoint, IGetEndpoint
            {
                public static string Path => "/second";
                {{Handler}}
            }
            """));

        Assert.StartsWith("'AuditedEndpoint' declares Configure(RouteHandlerBuilder)", diagnostic.GetMessage());
    }

    /// <summary>
    /// Silent for the new signatures, for a <c>Configure</c> with other parameter types, for an
    /// instance method, and for a builder that does not match the role (a group's
    /// <c>Configure(RouteHandlerBuilder)</c> is just a method).
    /// </summary>
    [Fact]
    public void NewSignatures_AndUnrelatedConfigureMethods_AreSilent()
    {
        var diagnostics = Reported($$"""
            public class UsersApi : IEndpointGroup
            {
                public static string Prefix => "/users";
                public static void Configure(RouteGroupBuilder group, IServiceProvider services) { }
                public static void Configure(RouteHandlerBuilder builder) { }
                public static void Configure(int count) { }
            }

            [MemberOf<UsersApi>]
            public class ListUsers : IGetEndpoint
            {
                public static string Path => "/";
                public static void Configure(RouteHandlerBuilder builder, IServiceProvider services) { }
                public void Configure(RouteHandlerBuilder builder) { }
                {{Handler}}
            }
            """);

        Assert.Empty(diagnostics);
    }
}
