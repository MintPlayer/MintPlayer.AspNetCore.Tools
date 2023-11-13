# MintPlayer.AspNetCore.SitemapXml

[![License](https://img.shields.io/badge/License-Apache%202.0-green.svg)](https://opensource.org/licenses/Apache-2.0)

Helper library to host a sitemap from your ASP.NET Core application

## Version info
| Package                                       | Release                                                                                                                                                                                       | Preview                                                                                                                                                                                          | Downloads |
|-----------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|-----------|
| MintPlayer.AspNetCore.SitemapXml              | [![NuGet Version](https://img.shields.io/nuget/v/MintPlayer.AspNetCore.SitemapXml.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.AspNetCore.SitemapXml)                           | [![NuGet Version](https://img.shields.io/nuget/vpre/MintPlayer.AspNetCore.SitemapXml.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.AspNetCore.SitemapXml)                           | [![NuGet](https://img.shields.io/nuget/dt/MintPlayer.AspNetCore.SitemapXml.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.AspNetCore.SitemapXml) |
| MintPlayer.AspNetCore.SitemapXml.Abstractions | [![NuGet Version](https://img.shields.io/nuget/v/MintPlayer.AspNetCore.SitemapXml.Abstractions.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.AspNetCore.SitemapXml.Abstractions) | [![NuGet Version](https://img.shields.io/nuget/vpre/MintPlayer.AspNetCore.SitemapXml.Abstractions.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.AspNetCore.SitemapXml.Abstractions) | [![NuGet](https://img.shields.io/nuget/dt/MintPlayer.AspNetCore.SitemapXml.Abstractions.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.AspNetCore.SitemapXml.Abstractions) |

## Installation
### NuGet package manager
Open the NuGet package manager and install MintPlayer.AspNetCore.SitemapXml in your project
### Package manager console
Install-Package MintPlayer.AspNetCore.SitemapXml
## Usage
### Adding SitemapXML services
Add the SitemapXML services (Startup@ConfigureServices).

    services.AddSitemapXml();

This call makes the ISitemapXml service available as a scoped service.

### Enable XML formatters
Modify Startup@ConfigureServices

    services
        .AddControllersWithViews(options => {
            options.RespectBrowserAcceptHeader = true;
        })
        .AddXmlSerializerFormatters()
        .AddMvcOptions(mvc_options => {
            mvc_options.OutputFormatters.Insert(0, new Microsoft.AspNetCore.Mvc.Formatters.XmlSerializerOutputFormatter());
        })
        .SetCompatibilityVersion(Microsoft.AspNetCore.Mvc.CompatibilityVersion.Latest);

### Add SitemapController
An example of your SitemapController. Notice the use of the `Produces` attribute:

    [Controller]
    [Route("[controller]")]
    public class SitemapController : Controller
    {
        private ISitemapXml sitemapXml;
        private IPersonRepository personRepository;
        public SitemapController(ISitemapXml sitemapXml, IPersonRepository personRepository)
        {
            this.sitemapXml = sitemapXml;
            this.personRepository = personRepository;
        }

        [Produces("application/xml")]
        [HttpGet(Name = "sitemap-index")]
        public SitemapIndex Index()
        {
            const int per_page = 100;

            var people = personRepository.GetPeople().ToList();
            var person_urls = sitemapXml.GetSitemapIndex(people, per_page, (perPage, page) => Url.RouteUrl("sitemap-person", new { count = perPage, page }, Request.Scheme));
            
            return new SitemapIndex(person_urls);
        }

        [Produces("application/xml")]
        [HttpGet("{count}/{page}", Name = "sitemap")]
        public UrlSet Sitemap(int count, int page)
        {
            var people = personRepository.GetPeople(count, page);
            return new UrlSet(people.Select(p => {
                var url = new Url {
                    Loc = $"{Request.Scheme}://{Request.Host}/person/{p.Id}",
                    ChangeFreq = SitemapXml.Enums.ChangeFreq.Monthly,
                    LastMod = p.DateUpdate,
                };
                url.Links.Add(new Link {
                    Rel = "alternate",
                    HrefLang = "nl",
                    Href = $"{Request.Scheme}://{Request.Host}/person/{p.Id}?lang=nl"
                });
                url.Links.Add(new Link {
                    Rel = "alternate",
                    HrefLang = "fr",
                    Href =  $"{Request.Scheme}://{Request.Host}/person/{p.Id}?lang=fr"
                });
                return url;
            }));
        }
    }
    
### Styling your sitemap
You can use an XSL stylesheet for your sitemaps. Modify Startup@ConfigureServices

    services
        .AddControllersWithViews(options => {
            options.RespectBrowserAcceptHeader = true;
        })
        .AddXmlSerializerFormatters()
        .AddMvcOptions(mvc_options => {
            mvc_options.OutputFormatters.Insert(0, new Microsoft.AspNetCore.Mvc.Formatters.XmlSerializerOutputFormatter());
        })
        .AddSitemapXmlFormatters(options => {
            options.StylesheetUrl = "/assets/sitemap.xsl";
        })
        .SetCompatibilityVersion(Microsoft.AspNetCore.Mvc.CompatibilityVersion.Latest);

Now you can either put your own XSLT file in the ClientApp/src/assets folder, or call the `UseDefaultSitemapXmlStylesheet` middleware.

### Using the built-in XML stylesheet
Put the following middleware before the app.UseMvc call:

    app.UseDefaultSitemapXmlStylesheet(options => {
        options.StylesheetUrl = "/assets/sitemap.xsl";
    });

Now an XML Stylesheet is hosted on the specified URL. You no longer need to put a sitemap.xsl in the assets folder.
