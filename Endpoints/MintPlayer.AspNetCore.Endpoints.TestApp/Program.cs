using MintPlayer.AspNetCore.Endpoints;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Source-generated extension method — discovers all endpoints in this assembly
app.MapMintPlayerAspNetCoreEndpointsTestAppEndpoints();

app.Run();
