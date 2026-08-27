using System.Reflection;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.Infrastructure;

/// <summary>
/// Guards the coverage wiring itself, rather than any library's behaviour.
/// </summary>
/// <remarks>
/// Both facts asserted here fail <i>silently</i> in CI if they regress — no build error, no
/// warning, just a coverage report quietly missing data. See docs/PRD-TestCoverage.md P3.1.
/// </remarks>
public class CoverageShapeTests
{
    /// <summary>
    /// Every shippable assembly must be loadable from this test project, because coverlet
    /// instruments what has a PDB in the test host's output directory. A package dropped from
    /// this project's ProjectReference list disappears from the coverage report with no error.
    /// </summary>
    /// <remarks>
    /// MintPlayer.AspNetCore.Endpoints.TestApp is deliberately absent: it is IsPackable=false
    /// sample code, referenced only for WebApplicationFactory, and excluded by name in
    /// coverlet.runsettings.
    /// </remarks>
    [Theory]
    [InlineData("MintPlayer.AspNetCore.ChangePassword")]
    [InlineData("MintPlayer.AspNetCore.Endpoints")]
    [InlineData("MintPlayer.AspNetCore.Endpoints.Abstractions")]
    [InlineData("MintPlayer.AspNetCore.Hsts")]
    [InlineData("MintPlayer.AspNetCore.LoggerProviders")]
    [InlineData("MintPlayer.AspNetCore.MustChangePassword")]
    [InlineData("MintPlayer.AspNetCore.MustChangePassword.Abstractions")]
    [InlineData("MintPlayer.AspNetCore.NoSniff")]
    [InlineData("MintPlayer.AspNetCore.OpenSearch")]
    [InlineData("MintPlayer.AspNetCore.OpenSearch.Abstractions")]
    [InlineData("MintPlayer.AspNetCore.SitemapXml")]
    [InlineData("MintPlayer.AspNetCore.SitemapXml.Abstractions")]
    [InlineData("MintPlayer.AspNetCore.SubDirectoryViews")]
    [InlineData("MintPlayer.Timestamps")]
    public void EveryShippablePackage_IsReferencedAndLoadable(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);

        Assert.NotNull(assembly);
        Assert.Equal(assemblyName, assembly.GetName().Name);
    }

    /// <summary>
    /// The libraries under test must span more than one top-level repository folder.
    /// </summary>
    /// <remarks>
    /// This is the machine-checkable half of the path-shape invariant. Coverlet sets the
    /// Cobertura &lt;source&gt; element to the longest common directory prefix of all instrumented
    /// documents, and the coverage server suffix-matches report paths against `git ls-files`,
    /// dropping ambiguous ones silently. Spanning several top-level folders keeps that prefix at
    /// the repository root, so paths stay unambiguous even for the five duplicated basenames in
    /// this repo (Image.cs, ServiceCollectionExtensions.cs, StringExtensions.cs, Url.cs,
    /// XmlSerializerOutputFormatter.cs).
    /// </remarks>
    [Fact]
    public void InstrumentedLibraries_SpanMultipleTopLevelFolders()
    {
        // One representative type per top-level folder that contributes shipped code.
        Type[] representatives =
        [
            typeof(MintPlayer.AspNetCore.ChangePassword.ApplicationBuilderExtensions), // ChangePassword/
            typeof(MintPlayer.AspNetCore.Endpoints.EndpointDescriptor),                // Endpoints/
            typeof(MintPlayer.AspNetCore.Hsts.ImprovedHstsMiddlewareExtensions),       // Hsts/
            typeof(MintPlayer.AspNetCore.NoSniff.NoSniffMiddleware),                   // NoSniff/
            typeof(MintPlayer.AspNetCore.SitemapXml.SitemapXmlExtensions),             // SitemapXml/
            typeof(MintPlayer.AspNetCore.OpenSearch.Data.OpenSearchDescription),       // OpenSearch/
            typeof(MintPlayer.AspNetCore.SubDirectoryViews.ServiceCollectionExtensions), // SubDirectoryViews/
        ];

        var distinctAssemblies = representatives.Select(t => t.Assembly).Distinct().Count();

        Assert.True(
            distinctAssemblies >= 5,
            $"Expected shipped code from at least 5 distinct assemblies across different top-level " +
            $"folders, found {distinctAssemblies}. Narrowing this test project's ProjectReference " +
            $"set collapses coverage report paths to bare filenames, which makes duplicated " +
            $"basenames ambiguous and silently drops them from the totals.");
    }
}
