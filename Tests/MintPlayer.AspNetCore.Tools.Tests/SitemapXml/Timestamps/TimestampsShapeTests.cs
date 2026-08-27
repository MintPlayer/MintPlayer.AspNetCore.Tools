using System.Reflection;
using MintPlayer.Timestamps;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.SitemapXml.Timestamps;

/// <summary>
/// <c>MintPlayer.Timestamps</c> is four interfaces with zero executable lines, so it can never
/// show up in a coverage report. What it does have is a shape other packages depend on:
/// <c>ISitemapXml.GetSitemapIndex</c> constrains its item type to <c>IUpdateTimestamp</c>, and any
/// change to these members breaks every consumer at compile time. These two reflection guards
/// are the whole of its test surface — the behavioural contract is exercised by
/// <see cref="SitemapXmlServiceTests"/>.
/// </summary>
public class TimestampsShapeTests
{
    [Fact]
    public void ITimestamps_ExtendsTheThreeSingleTimestampInterfaces()
    {
        var implemented = typeof(ITimestamps).GetInterfaces();

        Assert.Equal(
            [typeof(IDeleteTimestamp), typeof(IInsertTimestamp), typeof(IUpdateTimestamp)],
            implemented.OrderBy(t => t.Name).ToArray());
    }

    /// <summary>
    /// Each interface declares exactly one read-write <see cref="DateTime"/> property under a
    /// fixed name. Non-nullable is deliberate on the consuming side — <c>GetSitemapIndex</c> calls
    /// <c>Max(item => item.DateUpdate)</c>, which would not compile against a nullable.
    /// </summary>
    [Theory]
    [InlineData(typeof(IInsertTimestamp), "DateInsert")]
    [InlineData(typeof(IUpdateTimestamp), "DateUpdate")]
    [InlineData(typeof(IDeleteTimestamp), "DateDelete")]
    public void EachTimestampInterface_DeclaresOneReadWriteDateTimeProperty(Type interfaceType, string propertyName)
    {
        Assert.True(interfaceType.IsInterface);
        Assert.True(interfaceType.IsPublic);
        Assert.Empty(interfaceType.GetInterfaces());

        var property = Assert.Single(interfaceType.GetProperties());
        Assert.Equal(propertyName, property.Name);
        Assert.Equal(typeof(DateTime), property.PropertyType);
        Assert.True(property.CanRead);
        Assert.True(property.CanWrite);

        // No methods beyond the property accessors — the package is a pure shape.
        Assert.Equal(
            2,
            interfaceType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Length);
    }
}
