# MintPlayer.AspNetCore.OpenSearch

[![License](https://img.shields.io/badge/License-Apache%202.0-green.svg)](https://opensource.org/licenses/Apache-2.0)

Easily add OpenSearch to your ASP.NET Core website

## Version info
| Package                                       | Release                                                                                                                                                                                       | Preview                                                                                                                                                                                          | Downloads |
|-----------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|-----------|
| MintPlayer.AspNetCore.OpenSearch              | [![NuGet Version](https://img.shields.io/nuget/v/MintPlayer.AspNetCore.OpenSearch.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.AspNetCore.OpenSearch)                           | [![NuGet Version](https://img.shields.io/nuget/vpre/MintPlayer.AspNetCore.OpenSearch.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.AspNetCore.OpenSearch)                           | [![NuGet](https://img.shields.io/nuget/dt/MintPlayer.AspNetCore.OpenSearch.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.AspNetCore.OpenSearch) |
| MintPlayer.AspNetCore.OpenSearch.Abstractions | [![NuGet Version](https://img.shields.io/nuget/v/MintPlayer.AspNetCore.OpenSearch.Abstractions.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.AspNetCore.OpenSearch.Abstractions) | [![NuGet Version](https://img.shields.io/nuget/vpre/MintPlayer.AspNetCore.OpenSearch.Abstractions.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.AspNetCore.OpenSearch.Abstractions) | [![NuGet](https://img.shields.io/nuget/dt/MintPlayer.AspNetCore.OpenSearch.Abstractions.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.AspNetCore.OpenSearch.Abstractions) |

## Installation
### NuGet package manager
Open the NuGet package manager and install `MintPlayer.AspNetCore.OpenSearch` in your project
### Package manager console

    Install-Package MintPlayer.AspNetCore.OpenSearch

## Usage
### Register the OpenSearch services and configure the options

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddOpenSearch<Services.OpenSearchService>(options =>
        {
            options.OsdxEndpoint = "/opensearch.xml";
            options.SearchUrl = "/search";
            options.SuggestUrl = "/api/Subject/opensearch/suggest";
            options.SearchTermsParameter = "q";
            options.ImageUrl = "/assets/logo/music_note_16.png";
            options.ImageWidth = 16;
            options.ImageHeight = 16;
            options.ImageType = "image/png";
            options.ShortName = "MintPlayer";
            options.Description = "Search music on MintPlayer";
            options.Contact = "email@example.com";
        });
    }

`AddOpenSearch<TService>()` may also be called without an options callback; every setting has a
usable default. Calling `MapOpenSearch()` **without** `AddOpenSearch` throws.

### Options
| Option                 | Type     | Default                       | Description |
|------------------------|----------|-------------------------------|-------------|
| `OsdxEndpoint`         | `string?`| `/opensearch.xml`             | Path the OpenSearch description document is served at. Must start with `/`. |
| `SearchUrl`            | `string?`| `/search`                     | Path the user is redirected from. Must start with `/`. |
| `SuggestUrl`           | `string?`| `/suggest`                    | Path that serves suggestions. Must start with `/`. |
| `SearchTermsParameter` | `string` | `q`                           | Query-string parameter carrying the user's query. Both handlers read it, and it is where the emitted templates put the OpenSearch `{searchTerms}` macro. Set it to whatever your own search page expects. |
| `ImageUrl`             | `string?`| *(none)*                      | Path of the icon. Must start with `/`. When unset, no `<Image>` is advertised. |
| `ImageWidth`           | `int`    | `16`                          | Width in pixels of that icon. |
| `ImageHeight`          | `int`    | `16`                          | Height in pixels of that icon. |
| `ImageType`            | `string` | `image/png`                   | Media type of that icon. |
| `ShortName`            | `string?`| entry assembly's simple name  | Name of your search engine. The OpenSearch 1.1 spec caps this at **16 characters**; longer values are emitted unchanged, so keep it short yourself. |
| `Description`          | `string?`| `Search {ShortName}`          | Description of your search engine. The spec caps this at **1024 characters**. |
| `Contact`              | `string?`| *(none)*                      | Contact address. When unset, no `<Contact>` is emitted. |

A blank (empty or whitespace-only) value counts as unset and falls back to the default. Any of the
four path options that is non-blank and does not start with `/` throws an `InvalidOperationException`
from `MapOpenSearch()`. `OsdxEndpoint`, `SearchUrl` and `SuggestUrl` are mapped as route patterns, so
they may not contain a query string either — use `SearchTermsParameter` for that. (`ImageUrl` is not a
route, so `/logo.png?v=2` is fine.)

### Adding OpenSearch middleware
Add OpenSearch before `MapControllers` in the middleware pipeline (Startup.cs):

    app.MapOpenSearch();
    // or
    endpoints.MapOpenSearch();

### Build your OpenSearch service
This is an example implementation of the IOpenSearchService:

    public class OpenSearchService : IOpenSearchService
    {
        private readonly ISubjectRepository subjectRepository;
        public OpenSearchService(ISubjectRepository subjectRepository)
        {
            this.subjectRepository = subjectRepository;
        }

        public Task<OpenSearchRedirect> PerformSearch(string? searchTerms)
        {
            return Task.FromResult(new OpenSearchRedirect($"/search/{searchTerms}"));
        }

        public async Task<IEnumerable<string>> ProvideSuggestions(string? searchTerms)
        {
            return await subjectRepository.Suggest(searchTerms);
        }
    }

`OpenSearchRedirect(string Url, bool Permanent = false, bool PreserveMethod = false)` maps onto the
response status code: 302 by default, 301 for `Permanent`, 307 for `PreserveMethod`, 308 for both.
It lives in the Abstractions package, which references no ASP.NET assembly at all, so the contract
can be implemented (and faked in a unit test) from a plain class library.

### What the description advertises
`MapOpenSearch()` emits three `<Url>` elements. The `text/html` and
`application/x-suggestions+json` templates carry the `{searchTerms}` macro in the query string, e.g.

    <Url type="text/html" method="GET" template="https://example.com/search?q={searchTerms}" />

so a client knows where to put the user's query. The handlers read that same parameter back off the
query string, which means `SearchUrl`/`SuggestUrl` are plain literal paths — do **not** put a
`{searchTerms}` route token in them.

The suggest endpoint responds with `application/x-suggestions+json`, the media type the description
advertises, in the standard `[query, [completions]]` shape.

### Reference OpenSearchDescription from HTML
Open your index.html (angular app) or _ViewStart.cshtml (Razor) and add a link to your OpenSearchDescription:

    <link rel="search" type="application/opensearchdescription+xml" href="/opensearch.xml" title="Search through MintPlayer">

## Breaking changes in the next major version
* `IOpenSearchService.PerformSearch` returns `Task<OpenSearchRedirect>` instead of
  `Task<Microsoft.AspNetCore.Mvc.RedirectResult>`. The Abstractions package no longer references
  ASP.NET Core.
* `SearchUrl` and `SuggestUrl` are now literal paths; a `{searchTerms}` route token in them no
  longer does anything (it never worked — the handlers read the query string).
* Every path option is validated to start with `/`; previously only `OsdxEndpoint` was, and it threw
  the bare `Exception` type rather than `InvalidOperationException`.
* `MapOpenSearch()` throws when `AddOpenSearch` was never called.
* The `<Image>` and `<Contact>` elements are omitted when unconfigured, instead of being emitted
  empty or pointing at the site root.
* The suggest response is served as `application/x-suggestions+json` instead of `application/json`.
* `ShortName` falls back to the **entry assembly's** simple name rather than this library's full
  assembly identity, and the `Content-Disposition` filename derived from it is quoted and sanitised.
* `MintPlayer.AspNetCore.OpenSearch.Extensions.StringExtensions.NullIfEmpty` is `internal`; it was
  public and collided with the identically named extension in `MintPlayer.AspNetCore.SitemapXml`.
* Every option and DTO property that can be absent is annotated `string?`.
