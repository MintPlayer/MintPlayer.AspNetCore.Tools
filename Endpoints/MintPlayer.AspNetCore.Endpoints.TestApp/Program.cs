using MintPlayer.AspNetCore.Endpoints;
using MintPlayer.AspNetCore.Endpoints.TestApp.Models;

[assembly: EndpointsMethodName("MapTestAppEndpoints")]

// Every build of this project also runs this file: Microsoft.Extensions.ApiDescription.Server launches
// it under GetDocument.Insider to write the committed contract snapshot, openapi/*.json (PRD R7.5).
// Registrations and endpoint mappings must run so the document is complete; anything with a real side
// effect (a migration, a seed, a message-bus connection, starting the server) goes behind this flag.
var isBuildTimeDocumentGeneration = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

var builder = WebApplication.CreateBuilder(args);

// Nothing endpoint-specific to configure: the generated EndpointOpenApi.g.cs attaches its schema
// transformers to the endpoints themselves. The version is pinned because the default moved
// 3.0 -> 3.1 -> 3.2 across three releases (net10.0 writes 3.1.1, net11.0 3.2.0), and an unpinned
// snapshot would churn on every SDK upgrade.
builder.Services.AddOpenApi(options => options.OpenApiVersion = Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_1);

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

// This sample has no startup side effects, so nothing is behind the flag yet; a real app wraps its
// migrations, seeding and similar in `if (!isBuildTimeDocumentGeneration) { … }` here.
//
// app.Run() itself must NOT be skipped. The tool swaps in a no-op server and reads the endpoints from
// the running host: measured, returning before Run() writes a document with an empty "paths".
_ = isBuildTimeDocumentGeneration;

app.Run();
