namespace RoleValidation.Web.Tests.SourceRoleMappings;

public sealed class SourceRoleMappingManagementViewContractTests
{
    [Fact]
    public void View_Should_KeepLockedSourceMappingBoundaryAndExactColumns()
    {
        string view = Read("Views", "SourceRoleMappings", "Index.cshtml");

        Assert.Contains("Model.CanManage", view);
        Assert.Contains("asp-action=\"Add\"", view);
        Assert.Contains("asp-action=\"Replace\"", view);
        Assert.Contains("asp-action=\"Deactivate\"", view);
        Assert.Contains("@Html.AntiForgeryToken()", view);
        Assert.Contains("name=\"search\"", view);
        Assert.Contains("name=\"status\"", view);
        Assert.Contains("name=\"targetRoleId\"", view);
        Assert.Contains(
            "foreach (ValidationRole role in Model.FilterRoles)",
            view);
        Assert.Contains("Source key", view);
        Assert.Contains("Legacy display", view);
        Assert.Contains("Validation role", view);
        Assert.Contains("Status", view);
        Assert.Contains("Action", view);
        Assert.Contains("disabled", view);
        Assert.Contains("Read only", view);
        Assert.Contains(
            "id=\"mapping-action-@mapping.SourceRoleMappingId\"",
            view);
        Assert.Contains("data-source-key", view);
        Assert.Contains("data-trim-required", view);
        Assert.Contains("Activate a Validation Role before adding a source mapping.", view);
        Assert.Contains("Read from the legacy source", view);
        Assert.DoesNotContain("name=\"sourceDisplayName\"", view);
        Assert.DoesNotContain("Last changed", view);
        Assert.DoesNotContain("Changed by", view);
        Assert.DoesNotContain("fetch(", view);
    }

    [Fact]
    public void SharedLayout_Should_LinkSourceMappingsAsAvailablePage()
    {
        string layout = Read("Views", "Shared", "_ManagementNavigation.cshtml");

        Assert.Contains("asp-controller=\"SourceRoleMappings\"", layout);
        Assert.Contains("pageName == \"Source mappings\"", layout);
        Assert.DoesNotContain(
            "Source mappings <small>Unavailable</small>",
            layout);
    }

    [Fact]
    public void PageScript_Should_OnlyValidateSourceKeyLocally()
    {
        string script = Read(
            "wwwroot", "js", "source-role-mapping-management.js");

        Assert.Contains("data-source-role-mapping-page", script);
        Assert.Contains("data-source-mapping-form", script);
        Assert.Contains("data-source-key", script);
        Assert.Contains("trim()", script);
        Assert.DoesNotContain("fetch(", script);
        Assert.DoesNotContain("innerHTML", script);
    }

    private static string Read(params string[] pathParts)
    {
        return File.ReadAllText(Path.Combine(
            [AppContext.BaseDirectory, .. pathParts]));
    }
}
