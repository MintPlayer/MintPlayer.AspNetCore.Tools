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
        services.AddOpenSearch<Services.OpenSearchService>();
        services.Configure<OpenSearchOptions>(options =>
        {
            options.OsdxEndpoint = "/opensearch.xml";
            options.SearchUrl = "/api/Subject/opensearch/redirect/{searchTerms}";
            options.SuggestUrl = "/api/Subject/opensearch/suggest/{searchTerms}";
            options.ImageUrl = "/assets/logo/music_note_16.png";
            options.ShortName = "MintPlayer";
            options.Description = "Search music on MintPlayer";
            options.Contact = "email@example.com";
        });
    }

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

        public async Task<RedirectResult> PerformSearch(string searchTerms)
        {
            return new RedirectResult($"/search/{searchTerms}");
        }

        public async Task<IEnumerable<string>> ProvideSuggestions(string searchTerms)
        {
            return new[] { searchTerms + 'o', new string(searchTerms.Reverse().ToArray()) };
        }
    }

### Reference OpenSearchDescription from HTML
Open your index.html (angular app) or _ViewStart.cshtml (Razor) and add a link to your OpenSearchDescription:

    <link rel="search" type="application/opensearchdescription+xml" href="/opensearch.xml" title="Search through MintPlayer">
