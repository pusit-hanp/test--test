namespace RoleValidation.Web.Tests.Views;

public sealed class Phase2ManagementViewContractTests
{
    [Fact]
    public void Applications_Should_RenderServerProjectionWithoutRegistrationAction()
    {
        string view = Read("Views", "Applications", "Index.cshtml");

        Assert.Contains("foreach (ApplicationAdministrationRow", view);
        Assert.Contains("DependencyCounts.CanDeactivate", view);
        Assert.Contains("ActiveRoles", view);
        Assert.Contains("ActiveOwnerAssignments", view);
        Assert.Contains("ActiveSourceMappings", view);
        Assert.Contains("readonly", view);
        Assert.Contains("data-trim-required", view);
        Assert.Contains("EditorApplicationId", view);
        Assert.Contains("@(application.IsActive ? \"Deactivate\" : \"Activate\")", view);
        Assert.Contains(
            "disabled=\"@(application.IsActive && !application.DependencyCounts.CanDeactivate)\"",
            view);
        Assert.DoesNotContain("role=\"dialog\"", view);
        Assert.DoesNotContain("aria-modal=\"true\"", view);
        Assert.DoesNotContain("Add Application", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PULL_LIST", view);
        Assert.DoesNotContain("ERSN", view);
    }

    [Fact]
    public void ValidationRoles_Should_ExposeAddManageReactivateAndServerFeedback()
    {
        string view = Read("Views", "ValidationRoles", "Index.cshtml");

        Assert.Contains("Add validation role", view);
        Assert.Contains("Manage", view);
        Assert.Contains("Reactivate", view);
        Assert.Contains("ROLE_ID", view);
        Assert.Contains("role=\"alert\"", view);
        Assert.Contains("data-trim-required", view);
        Assert.Contains("EditorRoleId", view);
        Assert.Contains("name=\"roleId\"", view);
        Assert.DoesNotContain("role=\"dialog\"", view);
        Assert.DoesNotContain("aria-modal=\"true\"", view);
        Assert.DoesNotContain(">Owners<", view);
        Assert.DoesNotContain(">Mappings<", view);
        Assert.DoesNotContain(">Last changed<", view);
    }

    [Fact]
    public void SharedLayout_Should_ExposeRoleVBoundaryAndApprovedNavigation()
    {
        string layout = Read("Views", "Shared", "_Phase2ManagementLayout.cshtml");

        Assert.Contains("RoleV configuration", layout);
        Assert.Contains("Legacy source", layout);
        Assert.Contains("read only", layout, StringComparison.OrdinalIgnoreCase);
        string navigation = Read("Views", "Shared", "_ManagementNavigation.cshtml");
        Assert.Contains("PartialAsync(\"_ManagementNavigation\")", layout);
        Assert.Contains("PartialAsync(\"_ManagementTopbar\")", layout);
        Assert.Contains("asp-controller=\"ApplicationUsers\"", navigation);
        Assert.Contains("asp-controller=\"Applications\"", navigation);
        Assert.Contains("asp-controller=\"ValidationRoles\"", navigation);
        Assert.Contains("asp-controller=\"DeliveryFiles\"", navigation);
        Assert.Contains("asp-controller=\"EmailSchedules\"", navigation);
        Assert.Contains(
            "pageName == \"Annual delivery\" || pageName == \"Run detail\"",
            navigation);
        Assert.DoesNotContain(
            "Annual delivery <small>Unavailable</small>",
            navigation);
        Assert.Contains("~/js/phase2-management.js", layout);
        Assert.DoesNotContain("cdn", layout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManagementAssets_Should_PreserveAccessibleResponsivePreferences()
    {
        string css = Read("wwwroot", "css", "phase2-management.css")
            + Read("wwwroot", "css", "management-shell.css");
        string applicationsScript = Read(
            "wwwroot",
            "js",
            "application-management.js");
        string rolesScript = Read(
            "wwwroot",
            "js",
            "validation-role-management.js");
        string sharedScript = Read(
            "wwwroot",
            "js",
            "phase2-management.js");
        string scripts = sharedScript + applicationsScript + rolesScript;

        Assert.Contains("#eef3f4", css);
        Assert.Contains("#172b36", css);
        Assert.Contains("#102c3e", css);
        Assert.Contains("#117f81", css);
        Assert.Contains("overflow-x: auto", css);
        Assert.Contains(":focus-visible", css);
        Assert.Contains("prefers-reduced-motion", css);
        Assert.Contains("data-management-theme=\"dark\"", css);
        Assert.Contains("prefers-color-scheme: dark", css);
        Assert.Contains("localStorage", scripts);
        Assert.Contains("try", scripts);
        Assert.Contains("catch", scripts);
        Assert.Contains("aria-hidden", scripts);
        Assert.Contains("focus()", scripts);
        Assert.Contains("setCustomValidity", sharedScript);
        Assert.Contains("data-trim-required", sharedScript);
        Assert.Contains("openDrawer(initialDrawer, initialTrigger)", sharedScript);
        Assert.DoesNotContain("trapFocus", sharedScript);
    }

    [Fact]
    public void ManagementPageAnimation_Should_NotRetainTransformAfterEntering()
    {
        string css = Read("wwwroot", "css", "phase2-management.css");

        Assert.Contains("animation: management-enter 160ms ease;", css);
        Assert.DoesNotContain(
            "animation: management-enter 160ms ease both;",
            css);
    }

    private static string Read(params string[] pathParts)
    {
        return File.ReadAllText(Path.Combine(
            [AppContext.BaseDirectory, .. pathParts]));
    }
}
