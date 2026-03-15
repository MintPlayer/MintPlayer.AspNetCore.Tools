using MintPlayer.AspNetCore.Endpoints;

[assembly: EndpointsMethodName("MapTestAppEndpoints")]

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapTestAppEndpoints();

app.Run();
