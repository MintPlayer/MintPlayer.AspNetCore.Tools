using MintPlayer.AspNetCore.Endpoints;
using MintPlayer.AspNetCore.Endpoints.TestApp.Models;

[assembly: EndpointsMethodName("MapTestAppEndpoints")]

var builder = WebApplication.CreateBuilder(args);

// Nothing endpoint-specific to configure: the generated EndpointOpenApi.g.cs attaches its schema
// transformers to the endpoints themselves.
builder.Services.AddOpenApi();

// Validates request bodies marked [ValidatableType] (CreateUserRequest). The endpoints invoke it
// themselves after binding; without it nothing is validated. AddTestAppValidation() is a one-line
// wrapper over AddValidation(), exposed so a host in another assembly can register this assembly's
// types too — and calling it here rather than AddValidation() directly keeps a single AddValidation()
// call site in the compilation, which the net10.0 validation generator requires (see TestAppValidation).
builder.Services.AddTestAppValidation();

// The user endpoints take IUserStore in their primary constructors. It is scoped — one per request,
// which the generated mapping honours because it builds each endpoint from the request's services —
// over a singleton UserData, so what one request writes the next can read.
builder.Services.AddSingleton<UserData>();
builder.Services.AddScoped<IUserStore, InMemoryUserStore>();

var app = builder.Build();

app.MapOpenApi();
app.MapTestAppEndpoints();

app.Run();
