using FluentAssertions;
using Lindrake.Web.Services;

namespace Lindrake.UnitTests.Web;

public sealed class ApiBaseAddressTests
{
    private const string HostBase = "https://lindrake.example/app/";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_IsTheAppsOwnOrigin(string? configured)
    {
        var uri = ApiBaseAddress.Resolve(configured, HostBase);

        uri.Should().Be(new Uri("https://lindrake.example/"));
        new Uri(uri, "api/datasources").Should().Be(new Uri("https://lindrake.example/api/datasources"));
    }

    [Theory]
    [InlineData("http://localhost:5000", "http://localhost:5000/")]
    [InlineData("http://localhost:5000/", "http://localhost:5000/")]
    [InlineData("https://api.example/v1", "https://api.example/v1/")]
    public void Configured_IsUsedWithATrailingSlash(string configured, string expected)
    {
        var uri = ApiBaseAddress.Resolve(configured, HostBase);

        uri.Should().Be(new Uri(expected));
        new Uri(uri, "api/agent").AbsoluteUri.Should().Be(expected + "api/agent");
    }

    [Theory]
    [InlineData("localhost:5000")]
    [InlineData("/api")]
    [InlineData("ftp://files.example")]
    public void NotAnAbsoluteHttpUrl_Throws(string configured)
    {
        var act = () => ApiBaseAddress.Resolve(configured, HostBase);

        act.Should().Throw<InvalidOperationException>().WithMessage("*ApiBaseUrl*");
    }
}
