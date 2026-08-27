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
Open the NuGet package manager and install `MintPlayer.AspNetCore.SitemapXml` in your project
### Package manager console

    Install-Package MintPlayer.AspNetCore.SitemapXml

## Usage
### Adding SitemapXML services
Add the SitemapXML services (Startup.cs).

    services.AddSitemapXml();

This call makes the `ISitemapXml` service available as a scoped service.

You can also pass options to the method:

    services.AddSitemapXml(options => options.StylesheetUrl = "/sitemap.xsl");

`AddSitemapXml()` is idempotent: calling it twice (two libraries each doing their own setup, say)
registers one `ISitemapXml` and one output formatter. If your application registered its own
`ISitemapXml` first, it is left in place.

`StylesheetUrl` is used both as the route pattern of `MapDefaultSitemapXmlStylesheet()` and as the
`href` of the `xml-stylesheet` processing instruction, so it must be an absolute path starting with
`/` and must not contain a double quote. A blank or whitespace value counts as "not configured".
Anything else throws `InvalidOperationException` — at map time for the endpoint, and on the first
sitemap response for the processing instruction.

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

### The data model
`UrlSet` / `SitemapIndex` are the only two types the output formatter serializes (subclasses of
either are accepted, so you can add your own extension elements).

* `Url.Loc`, `Sitemap.Loc` and `Image.Location` are **required** by the sitemap protocols. Leaving
  one null or blank throws while serializing, rather than emitting a document a crawler will
  reject.
* `Url.LastMod`, `Url.ChangeFreq` and `Sitemap.LastMod` are `DateTime?` / `ChangeFreq?`. Both
  elements are optional in the spec and are **omitted** when null — they are not written as
  `xsi:nil`, and an unset date no longer serializes as `0001-01-01`.
* `ChangeFreq` covers all seven spec values: `Hourly`, `Daily`, `Monthly`, `Yearly`, `Always`,
  `Weekly`, `Never`. The three newest are appended rather than inserted in spec order, so the
  underlying integers of the original four are unchanged for anyone who persisted them.
* Every other member is optional and omitted when null.

### Paging
`ISitemapXml.PageCount(total, perPage)` rounds up, answers `0` for `total == 0`, and throws
`ArgumentOutOfRangeException` for a negative `total` or a non-positive `perPage`.

`ISitemapXml.GetSitemapIndex(items, perPage, urlFunc)` walks `items` **exactly once** and returns a
fully evaluated result, so it costs one round trip against an `IQueryable` and accepts a
single-pass source such as a data reader. Note the argument order of `urlFunc`: `(perPage, page)`.

### robots.txt
To let search engines know about your sitemap, add the following to the `robots.txt` file

    Sitemap: /Sitemap
    
### Styling your sitemap
You can use an XSL stylesheet for your sitemaps. Modify Startup@ConfigureServices

    services.AddSitemapXml(options => options.StylesheetUrl = "/sitemap.xsl");

To use the built-in xml-stylesheet:

    endpoints.MapDefaultSitemapXmlStylesheet();

Now an XML Stylesheet is hosted on the specified URL. You no longer need to put a sitemap.xsl in the assets folder.

The bundled stylesheet is XSLT 1.0, which is what browsers and `XslCompiledTransform` implement. It
loads its CSS from `https://unpkg.com`, so it renders unstyled offline and needs that host allowed
by your Content-Security-Policy; serve your own stylesheet instead of calling
`MapDefaultSitemapXmlStylesheet()` if that is not acceptable.
