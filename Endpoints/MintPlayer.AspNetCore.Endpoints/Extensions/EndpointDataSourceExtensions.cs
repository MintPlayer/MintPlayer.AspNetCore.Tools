using Microsoft.AspNetCore.Routing;

namespace MintPlayer.AspNetCore.Endpoints;

/// <summary>Answers whether an endpoint class is mapped, by type rather than by route string.</summary>
/// <remarks>
/// <para>
/// <b>Ask at request time, or from <c>IHostApplicationLifetime.ApplicationStarted</c> on.</b> The
/// <see cref="EndpointDataSource"/> in the container holds no endpoints until the host has started, so
/// a call in <c>Program.cs</c> before <c>app.Run()</c>, or in <c>IHostedService.StartAsync</c>, answers
/// <see langword="false"/> without any error. There is deliberately no overload on
/// <c>IEndpointRouteBuilder</c>: reading its data sources during startup builds the endpoints, after
/// which adding a convention to an already-mapped route throws.
/// </para>
/// <para>
/// Only endpoints mapped by this library carry an <see cref="EndpointTypeMetadata"/>. A plain
/// <c>MapGet</c> lambda or <c>MapIdentityApi</c> never matches.
/// </para>
/// </remarks>
public static class EndpointDataSourceExtensions
{
    /// <summary>
    /// Whether an endpoint of class <paramref name="endpointType"/> is mapped in
    /// <paramref name="dataSource"/>, typically the composite resolved from the container.
    /// </summary>
    /// <param name="dataSource">The endpoints to search.</param>
    /// <param name="endpointType">
    /// A closed endpoint class, matched by equality; or a generic type definition such as
    /// <c>typeof(ListExternalLogins&lt;&gt;)</c>, which matches any mapped endpoint whose class, or one of
    /// its base classes, is a closing of it. Interfaces are never matched, and a closed base class does
    /// not match a derived endpoint: only the definition does. A query for a shared generic base such as
    /// <c>PostEndpoint&lt;,&gt;</c> therefore matches every endpoint derived from it.
    /// </param>
    /// <remarks>
    /// Each endpoint's type is read with <c>GetMetadata&lt;EndpointTypeMetadata&gt;()</c>, so when an
    /// endpoint's own <c>Configure</c> adds another instance, the last one wins.
    /// <para>
    /// An endpoint whose group's or its own <c>IsEnabled</c> returned <see langword="false"/> is not
    /// mapped and answers <see langword="false"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="endpointType"/> contains generic parameters but is not a generic type definition
    /// (for example <c>typeof(Derived&lt;&gt;).BaseType</c>), which no mapped endpoint can ever be.
    /// </exception>
    public static bool IsEndpointMapped(this EndpointDataSource dataSource, Type endpointType)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(endpointType);

        if (endpointType.ContainsGenericParameters && !endpointType.IsGenericTypeDefinition)
            throw new ArgumentException(
                $"'{endpointType}' is partially open. Pass a closed type or a generic type definition such as typeof(Endpoint<>).",
                nameof(endpointType));

        if (endpointType.IsInterface)
            return false;

        foreach (var endpoint in dataSource.Endpoints)
        {
            if (endpoint.Metadata.GetMetadata<EndpointTypeMetadata>() is { } metadata
                && Matches(metadata.EndpointType, endpointType))
                return true;
        }

        return false;
    }

    /// <summary>Whether an endpoint of class <typeparamref name="TEndpoint"/> is mapped in <paramref name="dataSource"/>.</summary>
    /// <inheritdoc cref="IsEndpointMapped(EndpointDataSource, Type)" path="/remarks"/>
    public static bool IsEndpointMapped<TEndpoint>(this EndpointDataSource dataSource)
        => dataSource.IsEndpointMapped(typeof(TEndpoint));

    private static bool Matches(Type mapped, Type query)
    {
        if (!query.IsGenericTypeDefinition)
            return mapped == query;

        for (Type? type = mapped; type is not null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == query)
                return true;
        }

        return false;
    }
}
