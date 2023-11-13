using Microsoft.AspNetCore.Identity;
using MintPlayer.AspNetCore.MustChangePassword.Abstractions;
using MintPlayer.AspNetCore.MustChangePassword.Services;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>Adds the default implementation of the <see cref="MustChangePasswordService{TUser, TKey}"/> to the service container.</summary>
    /// <typeparam name="TUser">Implemented type of the <see cref="IdentityUser"/>.</typeparam>
    /// <typeparam name="TKey">Type of the ID used for the <see cref="IdentityUser"/>. Ideally you would use <see cref="Guid"/>.</typeparam>
    public static IServiceCollection AddMustChangePassword<TUser, TKey>(this IServiceCollection services)
        where TUser : Microsoft.AspNetCore.Identity.IdentityUser<TKey>
        where TKey : IEquatable<TKey>
    {
        return services.AddScoped<IMustChangePasswordService<TUser, TKey>, MustChangePasswordService<TUser, TKey>>();
    }
}
