using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MintPlayer.AspNetCore.MustChangePassword.Abstractions;
using MintPlayer.AspNetCore.MustChangePassword.Services;

// Deliberately the framework's own namespace: this is an Add* extension on IServiceCollection, and
// every consumer's Program.cs already has this using in scope, so the method is discoverable without
// an extra import. Same convention as the rest of ASP.NET Core.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registration extensions for the must-change-password flow.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Adds the default implementation of the <see cref="MustChangePasswordService{TUser, TKey}"/> to the service container.</summary>
    /// <typeparam name="TUser">Implemented type of the <see cref="IdentityUser"/>.</typeparam>
    /// <typeparam name="TKey">Type of the ID used for the <see cref="IdentityUser"/>. Ideally you would use <see cref="Guid"/>.</typeparam>
    /// <remarks>
    /// Also registers <see cref="Microsoft.AspNetCore.Http.IHttpContextAccessor"/>, which the service depends on — a
    /// dependency the caller should not have to know about. Calling this twice is a no-op.
    /// </remarks>
    public static IServiceCollection AddMustChangePassword<TUser, TKey>(this IServiceCollection services)
        where TUser : Microsoft.AspNetCore.Identity.IdentityUser<TKey>
        where TKey : IEquatable<TKey>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();
        services.TryAddScoped<IMustChangePasswordService<TUser, TKey>, MustChangePasswordService<TUser, TKey>>();

        return services;
    }
}
