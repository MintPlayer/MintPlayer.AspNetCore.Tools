using System.Collections.Concurrent;

namespace MintPlayer.AspNetCore.Endpoints.TestApp.Models;

/// <summary>
/// The users the sample's endpoints read and write — registered <b>scoped</b>, and injected into the
/// endpoints through their primary constructors.
/// </summary>
/// <remarks>
/// The generated <c>MapTestAppEndpoints()</c> builds every endpoint per request from
/// <c>HttpContext.RequestServices</c>, so a scoped service reaches an endpoint's constructor exactly
/// as it reaches an MVC controller's: one instance per request, shared by everything in that request.
/// <see cref="InstanceId"/> exists so that is observable (see <c>UserStoreScope</c>).
/// </remarks>
public interface IUserStore
{
    /// <summary>Unique to this instance, and so to the request scope that created it.</summary>
    Guid InstanceId { get; }

    IReadOnlyList<UserResponse> List();
    UserResponse? Find(int id);
    UserResponse? FindByName(string name);
    UserResponse Add(string name, string email);
    UserResponse Upsert(int id, string name, string email);
    bool Remove(int id);
}

/// <summary>
/// The data behind <see cref="IUserStore"/>: one per application, registered as a singleton.
/// </summary>
/// <remarks>
/// Kept apart from the store so the store can be scoped and still see what earlier requests wrote.
/// Seeded with Alice (1) and Bob (2); new users are numbered from 3.
/// </remarks>
public sealed class UserData
{
    private int lastId = 2;

    public ConcurrentDictionary<int, UserResponse> Users { get; } = new(new Dictionary<int, UserResponse>
    {
        [1] = new UserResponse(1, "Alice", "alice@example.com"),
        [2] = new UserResponse(2, "Bob", "bob@example.com"),
    });

    public int NextId() => Interlocked.Increment(ref lastId);
}

/// <summary>A scoped view over the singleton <see cref="UserData"/>.</summary>
public sealed class InMemoryUserStore(UserData data) : IUserStore
{
    public Guid InstanceId { get; } = Guid.NewGuid();

    public IReadOnlyList<UserResponse> List() => data.Users.Values.OrderBy(user => user.Id).ToList();

    public UserResponse? Find(int id) => data.Users.TryGetValue(id, out var user) ? user : null;

    public UserResponse? FindByName(string name) =>
        data.Users.Values.OrderBy(user => user.Id).FirstOrDefault(user => string.Equals(user.Name, name, StringComparison.OrdinalIgnoreCase));

    public UserResponse Add(string name, string email)
    {
        var user = new UserResponse(data.NextId(), name, email);
        data.Users[user.Id] = user;
        return user;
    }

    public UserResponse Upsert(int id, string name, string email) =>
        data.Users[id] = new UserResponse(id, name, email);

    public bool Remove(int id) => data.Users.TryRemove(id, out _);
}
