using MintPlayer.AspNetCore.Endpoints;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.Endpoints;

/// <summary>
/// <see cref="EndpointDescriptor"/>'s equality.
/// </summary>
/// <remarks>
/// It is a record, so callers reasonably expect value equality — and the compiler-generated version
/// compares <c>Methods</c> by reference. Every endpoint's <c>Methods</c> used to be a freshly
/// allocated array per access, so two descriptions of the same endpoint never compared equal, and
/// <c>Distinct()</c>, <c>Contains()</c> and a dictionary key over the descriptor list all quietly did
/// the wrong thing.
/// </remarks>
public class EndpointDescriptorTests
{
    private sealed record Handler;

    private static EndpointDescriptor Describe(params string[] methods)
        => new("Name", "/api/users", methods, typeof(Handler));

    [Fact]
    public void Equals_ComparesMethodsStructurally()
    {
        Assert.Equal(Describe("GET"), Describe("GET"));
        Assert.Equal(Describe("GET", "HEAD"), Describe("GET", "HEAD"));
    }

    /// <summary>Different method lists, and different orders, are different descriptors.</summary>
    [Fact]
    public void Equals_DistinguishesDifferentMethodLists()
    {
        Assert.NotEqual(Describe("GET"), Describe("POST"));
        Assert.NotEqual(Describe("GET"), Describe("GET", "HEAD"));
        Assert.NotEqual(Describe("GET", "HEAD"), Describe("HEAD", "GET"));
    }

    [Fact]
    public void Equals_ComparesEveryOtherMember()
    {
        var descriptor = Describe("GET");

        Assert.NotEqual(descriptor, new EndpointDescriptor("Other", "/api/users", ["GET"], typeof(Handler)));
        Assert.NotEqual(descriptor, new EndpointDescriptor("Name", "/api/others", ["GET"], typeof(Handler)));
        Assert.NotEqual(descriptor, new EndpointDescriptor("Name", "/api/users", ["GET"], typeof(string)));
        Assert.False(descriptor.Equals(null));
    }

    /// <summary>
    /// Equal descriptors hash equally, which is what makes them usable in a set or as a dictionary
    /// key — the reference-based hash was the other half of the same problem.
    /// </summary>
    [Fact]
    public void GetHashCode_AgreesWithEquals()
    {
        Assert.Equal(Describe("GET", "HEAD").GetHashCode(), Describe("GET", "HEAD").GetHashCode());

        var set = new HashSet<EndpointDescriptor> { Describe("GET"), Describe("GET") };

        Assert.Single(set);
    }
}
