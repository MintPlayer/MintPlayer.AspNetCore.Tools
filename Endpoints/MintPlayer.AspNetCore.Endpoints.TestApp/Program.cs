using MintPlayer.AspNetCore.Endpoints;

[assembly: EndpointsMethodName("MapTestAppEndpoints")]

var builder = WebApplication.CreateBuilder(args);

// Nothing endpoint-specific to configure: the generated EndpointOpenApi.g.cs attaches its schema
// transformers to the endpoints themselves.
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapOpenApi();
app.MapTestAppEndpoints();

app.Run();
