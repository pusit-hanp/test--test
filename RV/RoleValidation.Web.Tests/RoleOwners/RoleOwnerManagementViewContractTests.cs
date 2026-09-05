namespace RoleValidation.Web.Tests.RoleOwners;

public sealed class RoleOwnerManagementViewContractTests
{
    [Fact]
    public void View_Should_KeepSearchAndMutationsInsideLocalItBoundary()
    {
        string view = Read("Views", "RoleOwners", "Index.cshtml");

        Assert.Contains("Model.CanManage", view);
        Assert.Contains("asp-action=\"Search\"", view);
        Assert.Contains("asp-action=\"Assign\"", view);
        Assert.Contains("asp-action=\"Reassign\"", view);
        Assert.Contains("asp-action=\"Deactivate\"", view);
        Assert.Contains("@Html.AntiForgeryToken()", view);
        Assert.Contains("role=\"alert\"", view);
        Assert.Contains("role=\"status\"", view);
        Assert.Contains("data-owner-selection-form", view);
        Assert.Contains("data-owner-save", view);
        Assert.Contains("EmployeeStatus", view);
        Assert.Contains("AssignmentIsActive", view);
        Assert.Contains(
            "Model.CanManage && owner.AssignmentIsActive",
            view);
        Assert.Contains(
            "Model.Owners.Where(owner => owner.AssignmentIsActive)",
            view);
        Assert.Contains("management-chip-@status", view);
        Assert.Contains("No Role Owner assignments", view);
        Assert.DoesNotContain("No active Role Owner assignments", view);
        Assert.Contains("Already assigned", view);
        Assert.Contains("Employee Master:", view);
        Assert.Contains("<fieldset", view);
        Assert.Contains("<legend>", view);
        Assert.Contains("Model.RoleCount > 0", view);
        Assert.Contains(
            "Activate a Validation Role before assigning an owner.",
            view);
        Assert.Contains("asp-action=\"Index\"", view);
        Assert.Contains("Model.DrawerErrorTarget", view);
        Assert.Contains("id=\"owner-error-new\"", view);
        Assert.Contains("tabindex=\"-1\"", view);
        Assert.DoesNotContain("Email", view);
        Assert.DoesNotContain("aria-modal=\"true\"", view);
        Assert.DoesNotContain("checked=\"true\"", view);
    }

    [Fact]
    public void SharedLayout_Should_LinkRoleOwnersAsAvailablePage()
    {
        string layout = Read("Views", "Shared", "_ManagementNavigation.cshtml");

        Assert.Contains("asp-controller=\"RoleOwners\"", layout);
        Assert.Contains("pageName == \"Role owners\"", layout);
        Assert.DoesNotContain(
            "Role owners <small>Unavailable</small>",
            layout);
    }

    [Fact]
    public void PageScript_Should_BePageSpecificAndKeepServerValidationAuthoritative()
    {
        string script = Read("wwwroot", "js", "role-owner-management.js");

        Assert.Contains("data-owner-selection-form", script);
        Assert.Contains("data-owner-candidate", script);
        Assert.Contains("data-owner-save", script);
        Assert.Contains(":checked:not(:disabled)", script);
        Assert.DoesNotContain("fetch(", script);
        Assert.DoesNotContain("innerHTML", script);
        Assert.DoesNotContain("email", script, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(params string[] pathParts)
    {
        return File.ReadAllText(Path.Combine(
            [AppContext.BaseDirectory, .. pathParts]));
    }
}
