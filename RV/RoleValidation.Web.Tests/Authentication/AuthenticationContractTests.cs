using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using RoleValidation.Web.Authentication;
using RoleValidation.Web.Controllers;

namespace RoleValidation.Web.Tests.Authentication;

public sealed class AuthenticationConfigurationContractTests
{
    [Fact]
    public void TrackedAppSettings_Should_ExposeOnlySafeAuthenticationShape()
    {
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "appsettings.json")));
        JsonElement root = document.RootElement;
        JsonElement companyLogin = root
            .GetProperty("Authentication")
            .GetProperty("CompanyLogin");
        JsonElement encryption = root
            .GetProperty("Security")
            .GetProperty("TextEncryption");

        Assert.Equal(string.Empty, companyLogin.GetProperty("LoginUrl").GetString());
        Assert.Equal(string.Empty, companyLogin.GetProperty("PublicOrigin").GetString());
        Assert.Equal(0, companyLogin.GetProperty("SessionLifetimeMinutes").GetInt32());
        Assert.True(encryption.GetProperty("EncryptedConfiguration").GetBoolean());
        Assert.False(encryption.TryGetProperty("Passphrase", out _));
        Assert.False(root.TryGetProperty("ConnectionStrings", out _));
    }
}

public sealed class AuthenticationPipelineContractTests
{
    [Fact]
    public void Program_Should_RegisterAuthenticationAndRunItBeforeAuthorization()
    {
        string source = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Program.cs"));
        int registration = source.IndexOf(
            "AddRoleValidationAuthentication",
            StringComparison.Ordinal);
        int routing = source.IndexOf("UseRouting()", StringComparison.Ordinal);
        int authentication = source.IndexOf(
            "UseAuthentication()",
            StringComparison.Ordinal);
        int authorization = source.IndexOf(
            "UseAuthorization()",
            StringComparison.Ordinal);

        Assert.True(registration >= 0);
        Assert.True(routing >= 0);
        Assert.True(authentication > routing);
        Assert.True(authorization > authentication);
        Assert.Contains(
            "app.MapStaticAssets().AllowAnonymous();",
            source,
            StringComparison.Ordinal);
    }
}

public sealed class ProtectedEndpointContractTests
{
    [Fact]
    public void AuthenticatedLayouts_Should_ShowCurrentUserAndPostAntiforgeryLogout()
    {
        foreach (string layoutName in new[]
                 {
                     "_Layout.cshtml",
                     "_ManagementTopbar.cshtml"
                 })
        {
            string layout = File.ReadAllText(Path.Combine(
                AppContext.BaseDirectory,
                "Views",
                "Shared",
                layoutName));

            Assert.Contains("User.Identity?.Name", layout);
            Assert.Contains("asp-controller=\"Authentication\"", layout);
            Assert.Contains("asp-action=\"Logout\"", layout);
            Assert.Contains("method=\"post\"", layout);
            Assert.Contains("@Html.AntiForgeryToken()", layout);
            Assert.DoesNotMatch(
                "<a[^>]*(Logout|logout)",
                layout);
        }
    }

    [Theory]
    [InlineData(nameof(ApplicationUsersController.Index))]
    [InlineData(nameof(ApplicationUsersController.Export))]
    public void ApplicationUserReadEndpoints_Should_RequireAdminRead(
        string actionName)
    {
        MethodInfo action = typeof(ApplicationUsersController)
            .GetMethod(actionName)!;

        AuthorizeAttribute attribute = Assert.Single(
            action.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(
            RoleValidationAuthorizationPolicies.AdminRead,
            attribute.Policy);
    }

    [Fact]
    public void ErrorAndCompanyLoginEntryPoints_Should_BeExplicitlyAnonymous()
    {
        Assert.NotNull(typeof(ApplicationUsersController)
            .GetMethod(nameof(ApplicationUsersController.Error))!
            .GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.NotNull(typeof(AuthenticationController)
            .GetMethod(nameof(AuthenticationController.Begin))!
            .GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.NotNull(typeof(AuthenticationController)
            .GetMethod(nameof(AuthenticationController.Callback))!
            .GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.NotNull(typeof(AuthenticationController)
            .GetMethod(nameof(AuthenticationController.Denied))!
            .GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public void Logout_Should_RequireRoleValidationUser()
    {
        AuthorizeAttribute attribute = Assert.Single(
            typeof(AuthenticationController)
                .GetMethod(nameof(AuthenticationController.Logout))!
                .GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal(
            RoleValidationAuthorizationPolicies.RoleValidationUser,
            attribute.Policy);
    }
}
