using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using MintPlayer.AspNetCore.Endpoints.Generator;

namespace MintPlayer.AspNetCore.Endpoints.Generator.Tests.Infrastructure;

/// <summary>
/// Executes a generated mapping extension against a real routing pipeline.
/// </summary>
/// <remarks>
/// The generator hands almost everything that matters to the C# compiler and to ASP.NET Core:
/// routes come from <c>TEndpoint.Path</c> composed by <c>MapGroup</c>, and the <c>Produces</c>
/// status code from <c>TEndpoint.SuccessStatusCode</c>. Both are static abstract interface members
/// resolved at runtime, so the only way to assert on their values is to run the emitted code.
/// <para>
/// Endpoints are read from <see cref="IEndpointRouteBuilder.DataSources"/> rather than from the
/// container, because that is where <c>MapGroup</c> puts the group data source that applies the
/// prefix, and it needs no host to be started.
/// </para>
/// </remarks>
internal static class GeneratedEndpointHost
{
    /// <summary>
    /// Invokes the generated <c>Map…Endpoints</c> extension for <paramref name="assemblyName"/> and
    /// returns the routes it registered, with group prefixes already composed.
    /// </summary>
    public static IReadOnlyList<RouteEndpoint> MapAndCollectRoutes(Assembly generated, string assemblyName)
    {
        var app = WebApplication.CreateBuilder().Build();

        MappingMethod(generated, assemblyName).Invoke(null, [app]);

        return [.. ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()];
    }

    /// <summary>The descriptor list the generator emits as a static property.</summary>
    public static IReadOnlyList<EndpointDescriptor> Descriptors(Assembly generated, string assemblyName)
        => (IReadOnlyList<EndpointDescriptor>)ExtensionsType(generated, assemblyName)
            .GetProperty("Endpoints", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

    private static MethodInfo MappingMethod(Assembly generated, string assemblyName)
        => ExtensionsType(generated, assemblyName)
            .GetMethod(Info(assemblyName).GetMethodName(), BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"No mapping method in '{assemblyName}'.");

    private static Type ExtensionsType(Assembly generated, string assemblyName)
        => generated.GetType($"MintPlayer.AspNetCore.Endpoints.{Info(assemblyName).GetSafeClassName()}")
            ?? throw new InvalidOperationException($"No generated extensions class in '{assemblyName}'.");

    // Reusing the generator's own naming keeps the host from encoding a second, divergent copy of
    // the rule; the naming itself is asserted by AssemblyInfoTests.
    private static AssemblyInfo Info(string assemblyName) => new(assemblyName, null);
}
