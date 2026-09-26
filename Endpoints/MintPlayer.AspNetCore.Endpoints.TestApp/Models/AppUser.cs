using MintPlayer.AspNetCore.Endpoints.TestLibrary;

namespace MintPlayer.AspNetCore.Endpoints.TestApp.Models;

/// <summary>
/// The application's user type, which closes the TestLibrary's generic endpoints (see
/// <c>[assembly: EndpointTypeArgument&lt;LibUser, AppUser&gt;]</c> in <c>Program.cs</c>).
/// </summary>
public class AppUser : LibUser;
