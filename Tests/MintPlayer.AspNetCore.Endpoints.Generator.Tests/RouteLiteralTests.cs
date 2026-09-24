using Microsoft.CodeAnalysis;
using MintPlayer.AspNetCore.Endpoints.Generator.Tests.Infrastructure;
using Xunit;

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests;

/// <summary>
/// Pins exactly which spellings of <c>static abstract string Path</c> the generator can read at
/// compile time, and which it cannot.
/// </summary>
/// <remarks>
/// This boundary is load-bearing rather than incidental. Every route-shaped diagnostic — the
/// duplicate-route check, the route-parameter-versus-property check, the group-relative-path
/// check — is built on <see cref="RouteLiteral"/>, and each of them must stay <b>silent</b> where
/// recovery fails rather than guess. So the negative cases below are as important as the positive
/// ones: a future change that made an unreadable form quietly return <c>""</c> instead of
/// <c>null</c> would turn "we cannot check this" into "we checked, and the route is empty", and
/// the diagnostics would start reporting confidently wrong things.
/// </remarks>
public class RouteLiteralTests
{
    private const string Preamble = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Http;
        using MintPlayer.AspNetCore.Endpoints;

        namespace Fixtures;
        """;

    /// <summary>
    /// Reads <c>Path</c> off the named type in a compiled fixture, exactly as the generator's
    /// transform does.
    /// </summary>
    private static string? ReadPath(string body, string typeName = "Fixtures.Probe")
    {
        var compilation = EndpointGeneratorHarness.CreateCompilation("Fixtures", [$"{Preamble}\n\n{body}"]);

        var symbol = compilation.GetTypeByMetadataName(typeName);
        Assert.NotNull(symbol);

        // Any model from the compilation will do; RouteLiteral fetches the one for the
        // expression's own tree, which is what makes a base class in another file work.
        var model = compilation.GetSemanticModel(compilation.SyntaxTrees.First());

        return RouteLiteral.Read(symbol!, "Path", model, CancellationToken.None);
    }

    [Theory]
    // Expression-bodied string literal -- overwhelmingly the common form.
    [InlineData("""public class Probe { public static string Path => "/users/{id}"; }""", "/users/{id}")]
    // Block getter with a single return.
    [InlineData("""public class Probe { public static string Path { get { return "/b/{id}"; } } }""", "/b/{id}")]
    // Indirection through a const, which GetConstantValue folds.
    [InlineData("""public class Probe { private const string P = "/c/{id}"; public static string Path => P; }""", "/c/{id}")]
    // Concatenation of two consts.
    [InlineData("""public class Probe { private const string Root = "/d"; public static string Path => Root + "/{id}"; }""", "/d/{id}")]
    // Auto-property initializer.
    [InlineData("""public class Probe { public static string Path { get; } = "/f/{id}"; }""", "/f/{id}")]
    // Getter with an expression body, as opposed to the property having one.
    [InlineData("""public class Probe { public static string Path { get => "/g/{id}"; } }""", "/g/{id}")]
    // Concatenation of two literals.
    [InlineData("""public class Probe { public static string Path => "/k/" + "{id}"; }""", "/k/{id}")]
    // Raw string literal.
    [InlineData(""""public class Probe { public static string Path => """/m/{id}"""; }"""", "/m/{id}")]
    // nameof is a compile-time constant, so it folds like any other.
    [InlineData("""public class Probe { public static string Path => nameof(Probe); }""", "Probe")]
    public void Read_RecoversEveryConstantForm(string source, string expected)
    {
        Assert.Equal(expected, ReadPath(source));
    }

    [Theory]
    // Interpolation of a runtime value. Nothing constant to fold.
    [InlineData("""public class Probe { public static string Path => $"/e/{Guid.NewGuid()}"; }""")]
    // static readonly is NOT const -- the single most plausible mistake a reader could make about
    // this boundary, which is why it has its own case.
    [InlineData("""public class Probe { private static readonly string V = "/j/{id}"; public static string Path => V; }""")]
    // A method call is opaque.
    [InlineData("""public class Probe { public static string Path => string.Concat("/g/", "{id}"); }""")]
    // Two statements: there is no single expression, and picking the first return would invent a
    // route the endpoint may not answer on.
    [InlineData("""public class Probe { public static string Path { get { var x = "/n"; return x + "/{id}"; } } }""")]
    public void Read_ReturnsNullRatherThanGuessing(string source)
    {
        Assert.Null(ReadPath(source));
    }

    /// <summary>
    /// A partial split across files is one symbol with two declarations; the one carrying the
    /// property is the one that must be read.
    /// </summary>
    [Fact]
    public void Read_FindsThePathDeclaredInAnotherPartialFile()
    {
        var compilation = EndpointGeneratorHarness.CreateCompilation("Fixtures",
        [
            $$"""
            {{Preamble}}

            public partial class Probe { }
            """,
            $$"""
            {{Preamble}}

            public partial class Probe { public static string Path => "/h/{id}"; }
            """,
        ]);

        var symbol = compilation.GetTypeByMetadataName("Fixtures.Probe");
        var model = compilation.GetSemanticModel(compilation.SyntaxTrees.First());

        Assert.Equal("/h/{id}", RouteLiteral.Read(symbol!, "Path", model, CancellationToken.None));
    }

    /// <summary>
    /// An endpoint may inherit <c>Path</c> from a shared base class. The base's declaration lives
    /// in a different syntax tree, which is the case that forces <see cref="RouteLiteral"/> to go
    /// through <c>SemanticModel.Compilation</c> rather than reuse the model it was handed.
    /// </summary>
    [Fact]
    public void Read_FindsThePathDeclaredOnABaseClass()
    {
        var compilation = EndpointGeneratorHarness.CreateCompilation("Fixtures",
        [
            $$"""
            {{Preamble}}

            public class ProbeBase { public static string Path => "/i/{id}"; }
            """,
            $$"""
            {{Preamble}}

            public class Probe : ProbeBase { }
            """,
        ]);

        var symbol = compilation.GetTypeByMetadataName("Fixtures.Probe");
        var model = compilation.GetSemanticModel(compilation.SyntaxTrees.First());

        Assert.Equal("/i/{id}", RouteLiteral.Read(symbol!, "Path", model, CancellationToken.None));
    }

    /// <summary>
    /// A type declaring no such member is "unknown", not a failure.
    /// </summary>
    [Fact]
    public void Read_ReturnsNullWhenTheMemberIsAbsent()
    {
        Assert.Null(ReadPath("public class Probe { }"));
    }

    /// <summary>
    /// <c>Prefix</c> goes through the same code path, so groups get the same guarantees.
    /// </summary>
    [Fact]
    public void Read_WorksForAGroupPrefixToo()
    {
        var compilation = EndpointGeneratorHarness.CreateCompilation("Fixtures",
            [$$"""
              {{Preamble}}

              public class Probe { public static string Prefix => "/api/users"; }
              """]);

        var symbol = compilation.GetTypeByMetadataName("Fixtures.Probe");
        var model = compilation.GetSemanticModel(compilation.SyntaxTrees.First());

        Assert.Equal("/api/users", RouteLiteral.Read(symbol!, "Prefix", model, CancellationToken.None));
    }
}
