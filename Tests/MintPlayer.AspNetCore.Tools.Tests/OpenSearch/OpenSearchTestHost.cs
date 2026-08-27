using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.AspNetCore.OpenSearch;
using MintPlayer.AspNetCore.OpenSearch.Abstractions;

namespace MintPlayer.AspNetCore.Tools.Tests.OpenSearch;

/// <summary>
/// Shared helpers for the OpenSearch suites: an inline <see cref="TestServer"/> and
/// XML/culture utilities.
/// </summary>
internal static class OpenSearchTestHost
{
    /// <summary>The OpenSearch 1.1 namespace every element of the description is supposed to live in.</summary>
    public static readonly XNamespace A9 = "http://a9.com/-/spec/opensearch/1.1/";

    /// <summary>
    /// Builds a running <see cref="TestServer"/> with <c>MapOpenSearch</c> wired up.
    /// </summary>
    /// <param name="configure">
    /// Options callback, or <c>null</c> to call the parameterless <c>AddOpenSearch</c> overload so
    /// that <c>OpenSearchOptions</c> is never configured at all.
    /// </param>
    /// <param name="service">
    /// Singleton instance that overrides the scoped registration, so a test can read back what the
    /// endpoints passed in.
    /// </param>
    /// <param name="extraServices">Applied after <c>AddOpenSearch</c>, so it can override MvcOptions.</param>
    public static TestServer Create(
        Action<OpenSearchOptions>? configure,
        FakeOpenSearchService? service = null,
        Action<IServiceCollection>? extraServices = null)
    {
        // WebHostBuilder + the IWebHostBuilder TestServer ctor are marked deprecated in .NET 10, but
        // they are still the shortest inline way to host three endpoints; WebApplicationFactory is
        // overkill for a library with no Program.cs.
#pragma warning disable ASPDEPR004, ASPDEPR008
        var builder = new WebHostBuilder()
            .UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                if (configure is null)
                    services.AddOpenSearch<FakeOpenSearchService>();
                else
                    services.AddOpenSearch<FakeOpenSearchService>(configure);

                // Registered last so GetRequiredService hands back the instance the test holds.
                if (service is not null)
                    services.AddSingleton<IOpenSearchService>(service);

                extraServices?.Invoke(services);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapOpenSearch());
            });

        return new TestServer(builder);
#pragma warning restore ASPDEPR004, ASPDEPR008
    }

    /// <summary>
    /// An <see cref="HttpClient"/> that does <b>not</b> follow redirects, so the search endpoint's
    /// 302-vs-301 can be observed.
    /// </summary>
    public static HttpClient CreateNonRedirectingClient(this TestServer server)
        => new(server.CreateHandler()) { BaseAddress = server.BaseAddress };

    public static XDocument Serialize<T>(T value)
    {
        var serializer = new XmlSerializer(typeof(T));
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new System.Text.UTF8Encoding(false) }))
        {
            serializer.Serialize(writer, value);
        }

        return XDocument.Parse(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    /// <summary>Runs <paramref name="action"/> with both culture slots set, then restores them.</summary>
    public static void WithCulture(string name, Action action)
    {
        var culture = CultureInfo.GetCultureInfo(name);
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
