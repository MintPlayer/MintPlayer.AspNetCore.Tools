namespace MintPlayer.AspNetCore.Endpoints.TestApp.Models;

/// <summary>
/// Registers validation for the request types declared in this assembly, for a host in
/// <i>another</i> assembly that uses them.
/// </summary>
/// <remarks>
/// <c>Microsoft.Extensions.Validation</c> discovers <c>[ValidatableType]</c> types per compilation:
/// the resolver an <c>AddValidation()</c> call registers covers only the types of the assembly that
/// makes that call. A host elsewhere that calls only its own <c>AddValidation()</c> therefore never
/// validates <see cref="CreateUserRequest"/>, silently (PRD R5.5). The remedy is this shape: the
/// declaring assembly makes the call itself and exposes it, and the host calls it alongside its own.
/// <para>
/// The sample's own <c>Program.cs</c> calls this too, instead of <c>AddValidation()</c> directly,
/// because on net10.0 the validation generator throws (CS8785, duplicate hint name
/// <c>ValidatableInfoResolver.g.cs</c>) when one compilation contains more than one
/// <c>AddValidation()</c> call — and then registers no resolver at all, silently. Keep this the only
/// call site in the assembly.
/// </para>
/// </remarks>
public static class TestAppValidation
{
    /// <summary>Adds the validation resolver for this assembly's <c>[ValidatableType]</c> types.</summary>
    public static IServiceCollection AddTestAppValidation(this IServiceCollection services)
        => services.AddValidation();
}
