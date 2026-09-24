using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MintPlayer.AspNetCore.Endpoints.Generator.Tests.Infrastructure;
using Xunit;

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests;

/// <summary>
/// MPEP015 (M7, PRD R5.3): a request type with validation rules but no <c>[ValidatableType]</c>.
/// </summary>
/// <remarks>
/// <para>
/// The failure mode is silence. <c>Microsoft.Extensions.Validation</c> cannot discover a type through
/// a generated <c>Map</c> call, so an unmarked request type with <c>[Required]</c> on it is simply
/// never validated: invalid input returns 200, and neither .NET 10 nor .NET 11 reports anything
/// (.NET 11's ASP0038 covers only a <i>marked</i> type with no <c>AddValidation()</c>). These tests
/// pin that MPEP015 fires on every spelling of that shape — property attribute, positional record
/// parameter attribute, <c>IValidatableObject</c> — and stays quiet everywhere the consumer has
/// nothing to fix, because a warning that fires on correct code gets suppressed wholesale.
/// </para>
/// <para>
/// None of the fixtures that fire use <c>[ValidatableType]</c>, so they compile on net10.0 without
/// the ASP0029 suppression; that is what lets the no-cascade assertion be a plain full compile.
/// </para>
/// </remarks>
public class RequestValidationDiagnosticTests
{
    private const string Preamble = """
        using System.Collections.Generic;
        using System.ComponentModel.DataAnnotations;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Http;
        using Microsoft.Extensions.Validation;
        using MintPlayer.AspNetCore.Endpoints;

        namespace Fixtures;
        """;

    private static string Endpoint(string requestType) => $$"""
        public partial class Signup : IPostEndpoint<{{requestType}}>
        {
            public static string Path => "/signup";

            public override Task<IResult> HandleAsync({{requestType}} request, CancellationToken ct)
                => Task.FromResult(Results.Ok());
        }
        """;

    private static string Fixture(string types, string requestType = "SignupRequest")
        => Preamble + "\n\n" + types + "\n\n" + Endpoint(requestType);

    private static string TextAt(Location location)
        => location.SourceTree!.GetText().ToString(location.SourceSpan);

    private static Diagnostic[] Mpep015(GeneratorDriverRunResult result)
        => [.. result.Diagnostics.Where(d => d.Id == "MPEP015")];

    /// <summary>
    /// Asserts MPEP015 is the generator's only diagnostic, fully shaped, and that compiling the
    /// fixture with the generated code produces no error — the warning must not suppress emission
    /// and leave a <c>CS</c> error in its wake (R3.4).
    /// </summary>
    private static Diagnostic AssertSoleMpep015(string source)
    {
        var result = EndpointGeneratorHarness.Run("Fixtures", source);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("MPEP015", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("Signup", TextAt(diagnostic.Location));

        var message = diagnostic.GetMessage();
        Assert.Contains("'Fixtures.SignupRequest'", message);
        Assert.Contains("'Signup'", message);
        Assert.Contains("validation never runs", message);
        Assert.Contains("[Microsoft.Extensions.Validation.ValidatableType]", message);
        Assert.Contains("AddValidation()", message);

        var errors = EndpointGeneratorHarness.RunAndCompile("Fixtures", source)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.True(errors.Length == 0, string.Join(" | ", errors.Select(e => $"{e.Id}: {e.GetMessage()}")));

        return diagnostic;
    }

    // ---- fires ----------------------------------------------------------------------------------

    /// <summary>The canonical silent no-op: a DataAnnotations attribute on a property.</summary>
    [Fact]
    public void PropertyAttribute_WithoutValidatableType_ReportsMPEP015()
    {
        var diagnostic = AssertSoleMpep015(Fixture("""
            public sealed class SignupRequest
            {
                [Required] public string? Name { get; set; }
            }
            """));

        Assert.Contains("carries validation attributes", diagnostic.GetMessage());
    }

    /// <summary>
    /// An attribute on a positional record parameter, written without a <c>property:</c> target.
    /// </summary>
    /// <remarks>
    /// That attribute lands on the constructor parameter, not on the property, so a check reading only
    /// properties misses the most common record spelling. The validation generator does honour it
    /// there (measured on both TFMs; pinned at runtime by <c>RequestValidationTests</c>), so it is the
    /// same silent no-op and gets the same fix.
    /// </remarks>
    [Fact]
    public void PositionalRecordParameterAttribute_WithoutValidatableType_ReportsMPEP015()
    {
        AssertSoleMpep015(Fixture("""
            public sealed record SignupRequest([Required] string Name, [EmailAddress] string Email);
            """));
    }

    /// <summary>The explicit <c>property:</c> target is the same shape.</summary>
    [Fact]
    public void PropertyTargetedRecordAttribute_WithoutValidatableType_ReportsMPEP015()
    {
        AssertSoleMpep015(Fixture("""
            public sealed record SignupRequest([property: Required] string Name);
            """));
    }

    /// <summary>
    /// A custom attribute derived from <c>ValidationAttribute</c>, on a property inherited from a base
    /// class, still counts.
    /// </summary>
    /// <remarks>
    /// A check that matched attribute names, or read only the type's own members, would miss both.
    /// </remarks>
    [Fact]
    public void InheritedCustomValidationAttribute_ReportsMPEP015()
    {
        AssertSoleMpep015(Fixture("""
            public sealed class EvenAttribute : ValidationAttribute
            {
                public override bool IsValid(object? value) => value is int number && number % 2 == 0;
            }

            public class SignupBase
            {
                [Even] public int Seats { get; set; }
            }

            public sealed class SignupRequest : SignupBase;
            """));
    }

    /// <summary><c>IValidatableObject</c> without any attribute is the same silent no-op.</summary>
    [Fact]
    public void ValidatableObject_WithoutValidatableType_ReportsMPEP015()
    {
        var diagnostic = AssertSoleMpep015(Fixture("""
            public sealed class SignupRequest : IValidatableObject
            {
                public int Seats { get; set; }

                public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
                {
                    yield break;
                }
            }
            """));

        Assert.Contains("implements IValidatableObject", diagnostic.GetMessage());
    }

    /// <summary>The typed-with-response level is checked too, not only <c>IPostEndpoint&lt;TRequest&gt;</c>.</summary>
    [Fact]
    public void TypedWithResponse_ReportsMPEP015()
    {
        var source = Preamble + """


            public sealed record SignupRequest([Required] string Name);
            public sealed record SignupResponse(int Id);

            public partial class Signup : IPostEndpoint<SignupRequest, SignupResponse>
            {
                public static string Path => "/signup";

                public override Task<IResult> HandleAsync(SignupRequest request, CancellationToken ct)
                    => Task.FromResult(Results.Ok());
            }
            """;

        AssertSoleMpep015(source);
    }

    // ---- stays silent ---------------------------------------------------------------------------

    /// <summary>The fixed shape — <c>[ValidatableType]</c> present — reports nothing.</summary>
    [Fact]
    public void WithValidatableType_ReportsNothing()
    {
        var result = EndpointGeneratorHarness.Run("Fixtures", Fixture("""
            [ValidatableType]
            public sealed record SignupRequest([Required] string Name) : IValidatableObject
            {
                public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
                {
                    yield break;
                }
            }
            """));

        Assert.Empty(result.Diagnostics);
    }

    /// <summary>A request type with no validation rules at all has nothing to fix.</summary>
    [Fact]
    public void WithoutDataAnnotations_ReportsNothing()
    {
        var result = EndpointGeneratorHarness.Run("Fixtures", Fixture("""
            public sealed record SignupRequest(string Name, int Seats);
            """));

        Assert.Empty(result.Diagnostics);
    }

    /// <summary>
    /// A non-validation attribute (<c>[Display]</c>) is not a validation rule.
    /// </summary>
    [Fact]
    public void NonValidationDataAnnotation_ReportsNothing()
    {
        var result = EndpointGeneratorHarness.Run("Fixtures", Fixture("""
            public sealed class SignupRequest
            {
                [Display(Name = "Full name")] public string? Name { get; set; }
            }
            """));

        Assert.Empty(result.Diagnostics);
    }

    /// <summary>
    /// A response-only endpoint has no body to validate: DataAnnotations on its <i>response</i> type
    /// are not a validation gap.
    /// </summary>
    [Fact]
    public void ResponseOnlyEndpoint_ReportsNothing()
    {
        var result = EndpointGeneratorHarness.Run("Fixtures", Preamble + """


            public sealed record SignupResponse([Required] string Name);

            public partial class GetSignup : IGetEndpoint<SignupResponse>
            {
                public static string Path => "/signup";

                public override Task<IResult> HandleAsync(CancellationToken ct)
                    => Task.FromResult(Results.Ok());
            }
            """);

        Assert.Empty(Mpep015(result));
    }

    /// <summary>
    /// A consumer whose compilation cannot resolve <c>ValidatableTypeAttribute</c> could not act on
    /// the advice, so MPEP015 stays silent rather than telling them to use a type they do not have.
    /// </summary>
    /// <remarks>
    /// The first assertion guards against a vacuous pass: if the harness ever stopped removing the
    /// assembly, this test would be checking the ordinary case.
    /// </remarks>
    [Fact]
    public void ValidatableTypeAttributeUnresolvable_ReportsNothing()
    {
        var source = Fixture("""
            public sealed class SignupRequest
            {
                [Required] public string? Name { get; set; }
            }
            """);

        var compilation = EndpointGeneratorHarness.CreateCompilation("Fixtures", [source.Replace("using Microsoft.Extensions.Validation;\n", "").Replace("using Microsoft.Extensions.Validation;\r\n", "")], includeValidation: false);
        Assert.Null(compilation.GetTypeByMetadataName("Microsoft.Extensions.Validation.ValidatableTypeAttribute"));
        Assert.NotNull(EndpointGeneratorHarness.CreateCompilation("Fixtures", [source])
            .GetTypeByMetadataName("Microsoft.Extensions.Validation.ValidatableTypeAttribute"));

        var result = CSharpGeneratorDriver.Create(new EndpointGenerator()).RunGenerators(compilation).GetRunResult();

        Assert.Empty(Mpep015(result));
    }

    /// <summary>
    /// The corpus and the sample stay clean, and the sample's <c>CreateUserRequest</c> is covered by
    /// its <c>[ValidatableType]</c> — see <c>RouteDiagnosticTests.TheTestApp_ProducesNoWarningOrErrorFromTheGenerator</c>.
    /// This test pins the other half of R5.4: the generator never emits <c>[ValidatableType]</c>
    /// itself, marked request or not.
    /// </summary>
    /// <remarks>
    /// Emitting it would look like a fix and would do nothing: generators do not see each other's
    /// output, and .NET 11 reports ASP0037 on exactly that.
    /// </remarks>
    [Theory]
    [InlineData("public sealed record SignupRequest([Required] string Name);")]
    [InlineData("[ValidatableType] public sealed record SignupRequest([Required] string Name);")]
    public void GeneratedCode_NeverContainsValidatableType(string types)
    {
        var result = EndpointGeneratorHarness.Run("Fixtures", Fixture(types));

        Assert.NotEmpty(result.GeneratedTrees);
        foreach (var tree in result.GeneratedTrees)
            Assert.DoesNotContain("ValidatableType", tree.ToString());
    }
}
