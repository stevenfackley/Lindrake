using FluentAssertions;
using Lindrake.Web.Pages;
using Lindrake.Web.Services;

namespace Lindrake.UnitTests.Web;

public sealed class PageAccessTests
{
    [Theory]
    [InlineData(typeof(Home))]
    [InlineData(typeof(Agent))]
    [InlineData(typeof(DataSources))]
    [InlineData(typeof(Entities))]
    public void AppPages_RequireASignedInUser(Type page)
    {
        PageAccess.RequiresAuthentication(page).Should().BeTrue();
    }

    [Theory]
    [InlineData(typeof(Login))]
    [InlineData(typeof(Register))]
    public void AccountPages_AreAnonymous(Type page)
    {
        PageAccess.RequiresAuthentication(page).Should().BeFalse();
    }

    [Fact]
    public void ComponentWithoutAuthorize_IsAnonymous()
    {
        PageAccess.RequiresAuthentication(typeof(Lindrake.Web.Layout.NavMenu)).Should().BeFalse();
    }

    [Theory]
    [InlineData("datasources", "datasources")]
    [InlineData("agent?x=1", "agent?x=1")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("login", "")]
    [InlineData("login?returnUrl=agent", "")]
    [InlineData("https://evil.example/", "")]
    [InlineData("//evil.example/", "")]
    [InlineData("/\\evil.example", "")]
    [InlineData("\\\\evil.example", "")]
    [InlineData("javascript:alert(1)", "")]
    public void SafeReturnUrl_OnlyAllowsLocalPaths(string? returnUrl, string expected)
    {
        PageAccess.SafeReturnUrl(returnUrl).Should().Be(expected);
    }
}
