namespace RoleValidation.Web.Tests.AuthorizedUsers;

public sealed class AuthorizedUserManagementViewContractTests
{
    [Fact]
    public void View_Should_KeepApprovedSecurityRegisterAndStableActions()
    {
        string view = Read("Views", "AuthorizedUsers", "Index.cshtml");

        Assert.Contains("Security / RoleV allow-list", view);
        Assert.Contains("Access safeguards", view);
        Assert.Contains("Employee</th>", view);
        Assert.Contains("Employee no.</th>", view);
        Assert.Contains("Access role</th>", view);
        Assert.Contains("Status</th>", view);
        Assert.Contains("Last login</th>", view);
        Assert.Contains("Last changed</th>", view);
        Assert.Contains("Action</th>", view);
        Assert.Contains("Never recorded", view);
        Assert.Contains("authorized-user-editor-new", view);
        Assert.Contains("authorized-user-editor-@user.AuthorizedUserId", view);
        Assert.Contains("authorized-user-action-@user.AuthorizedUserId", view);
        Assert.Contains("authorized-user-error-new", view);
        Assert.Contains("asp-action=\"Search\"", view);
        Assert.Contains("asp-action=\"Add\"", view);
        Assert.Contains("asp-action=\"Change\"", view);
        Assert.Contains("asp-action=\"Deactivate\"", view);
        Assert.Contains("asp-action=\"Reactivate\"", view);
        Assert.Contains("@Html.AntiForgeryToken()", view);
        Assert.Contains("data-authorized-user-candidate", view);
        Assert.Contains("data-management-role-filter", view);
        Assert.DoesNotContain("Delete", view);
        Assert.DoesNotContain("aria-modal=\"true\"", view);
    }

    [Fact]
    public void View_Should_UseActivePairInsteadOfOfferingReactivateForHistory()
    {
        string view = Read("Views", "AuthorizedUsers", "Index.cshtml");

        Assert.Contains("Active as @activeAuthorization.AccessRole.Value", view);
        Assert.Contains("data-authorized-user-drawer-transition", view);
    }

    [Fact]
    public void View_Should_AllowInactivePairSelectionAndDescribeInPlaceReactivation()
    {
        string view = Read("Views", "AuthorizedUsers", "Index.cshtml");

        Assert.Contains("Model.FindActiveAuthorization", view);
        Assert.Contains("reactivates record #", view);
        Assert.Contains("same authorization record", view);
        Assert.DoesNotContain("new active authorization is created", view);
        Assert.DoesNotContain("activePair is null && inactive is null", view);
    }

    [Fact]
    public void View_Should_BlockCurrentActorInAddSearch()
    {
        string view = Read("Views", "AuthorizedUsers", "Index.cshtml");

        Assert.Contains("data-authorized-user-self-blocked", view);
    }

    [Fact]
    public void View_Should_StateImmediateChangesAndNoFourEyesWorkflow()
    {
        string view = Read("Views", "AuthorizedUsers", "Index.cshtml");

        Assert.Contains("Changes take effect directly.", view);
        Assert.Contains("There is no four-eyes workflow.", view);
    }

    [Fact]
    public void SharedLayout_Should_ShowAuthorizedUsersOnlyToLocalIt()
    {
        string layout = Read("Views", "Shared", "_ManagementNavigation.cshtml");

        Assert.Contains("User.IsInRole(\"Local_IT_Admin\")", layout);
        Assert.Contains("asp-controller=\"AuthorizedUsers\"", layout);
        Assert.Contains("pageName == \"Authorized users\"", layout);
        Assert.DoesNotContain(
            "Authorized users <small>Unavailable</small>",
            layout);
    }

    [Fact]
    public void PageCss_Should_KeepAuthorizedTableAndMobileBoundaryLocal()
    {
        string css = Read("wwwroot", "css", "phase2-management.css");

        Assert.Contains(".management-authorized-user-table", css);
        Assert.Contains("overflow-x: auto", css);
        Assert.Contains("@media (max-width: 560px)", css);
    }

    private static string Read(params string[] pathParts) =>
        File.ReadAllText(Path.Combine([AppContext.BaseDirectory, .. pathParts]));
}
