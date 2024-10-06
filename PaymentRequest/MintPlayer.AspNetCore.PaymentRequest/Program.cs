// Implements the following draft: https://developer.mozilla.org/en-US/docs/Web/API/Payment_Handler_API

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

var app = builder.Build();

// Configure the HTTP request pipeline.

app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    await next();
});
app.UseStaticFiles();
app.MapMethods("/pay", [HttpMethods.Head], async (context) =>
{
    context.Response.Headers["Link"] = "</pay/payment-manifest.json>; rel=\"payment-method-manifest\"";
});
app.MapGet("/pay/payment-manifest.json", async (context) =>
{
    await context.Response.WriteAsJsonAsync(new
    {
        default_applications = new string[]
        {
            "https://localhost:7208/manifest.json"
        },
        supported_origins = new string[]
        {
            "https://localhost:7208"
        }
    });
});
app.MapGet("/manifest.json", async (context) =>
{
    await context.Response.WriteAsJsonAsync(new
    {
        name = "MintPlayer",
        short_name = "MintPlayer",
        theme_color = "#1976d2",
        background_color = "#fafafa",
        display = "standalone",
        scope = "/",
        start_url = "/",
        icons = new[] {
            new {
                src = "music_note_192.png",
                sizes = "192x192",
                type = "image/png"
            },
        },
        serviceworker = new {
            src = "worker.js",
            scope = "/",
            use_cache = false,
        }
    });
});
app.MapControllers();

app.Run();

