using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RoleValidation.Application.Administration;
using RoleValidation.Application.Authorization;
using RoleValidation.Application.RoleOwners;
using RoleValidation.Application.SourceMappings;
using RoleValidation.Core.Features.Applications;
using RoleValidation.Web.Authentication;
using RoleValidation.Web.Controllers;
using RoleValidation.Web.Models.Applications;

namespace RoleValidation.Web.Tests.Applications;

public sealed class ApplicationsControllerTests
{
    [Fact]
    public async Task Index_Should_RenderServerRowsAndDependencyGuardWithoutAssumingSeven()
    {
        var store = new RecordingAdministrationStore
        {
            Applications =
            [
                new(
                    17,
                    "WAREHOUSE",
                    "Warehouse",
                    true,
                    new ApplicationDependencyCounts(2, 1, 3)),
                new(
                    18,
                    "PORTAL",
                    "Portal",
                    false,
                    new ApplicationDependencyCounts(0, 0, 0))
            ]
        };
        ApplicationsController controller = CreateController(
            store,
            "Local_IT_Admin");

        IActionResult action = await controller.Index(CancellationToken.None);

        ViewResult view = Assert.IsType<ViewResult>(action);
        ApplicationManagementViewModel model =
            Assert.IsType<ApplicationManagementViewModel>(view.Model);
        Assert.Equal(2, model.RegisteredCount);
        Assert.Equal(1, model.ActiveCount);
        Assert.True(model.CanManage);
        Assert.False(model.Applications[0].DependencyCounts.CanDeactivate);
        Assert.True(model.Applications[1].DependencyCounts.CanDeactivate);
    }

    [Fact]
    public async Task Admin_Should_BeAllowedToReadButDeniedToApplicationMutations()
    {
        using ServiceProvider services = BuildAuthorizationServices();
        IAuthorizationService authorization = services
            .GetRequiredService<IAuthorizationService>();
        ClaimsPrincipal admin = CreatePrincipal("Admin");

        Assert.True((await authorization.AuthorizeAsync(
            admin,
            null,
            GetPolicy(nameof(ApplicationsController.Index)))).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(
            admin,
            null,
            GetPolicy(nameof(ApplicationsController.Rename)))).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(
            admin,
            null,
            GetPolicy(nameof(ApplicationsController.SetActive)))).Succeeded);
    }

    [Theory]
    [InlineData(nameof(ApplicationsController.Rename))]
    [InlineData(nameof(ApplicationsController.SetActive))]
    public void Mutation_Should_RequirePostAntiforgeryAndLocalItAdministration(
        string actionName)
    {
        MethodInfo action = typeof(ApplicationsController)
            .GetMethod(actionName)!;

        Assert.Equal(
            RoleValidationAuthorizationPolicies.LocalItAdministration,
            action.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.NotNull(action.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(
            action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    [Fact]
    public async Task Rename_Should_TrimValuesUseEmployeeClaimAndRedirectToUpdatedRow()
    {
        var store = new RecordingAdministrationStore();
        ApplicationsController controller = CreateController(
            store,
            "Local_IT_Admin",
            employeeNo: " C1008267 ");

        IActionResult action = await controller.Rename(
            17,
            "  Warehouse Portal  ",
            CancellationToken.None);

        Assert.Equal(
            new RenameApplicationCommand(
                17,
                "Warehouse Portal",
                "C1008267"),
            store.RenameCommand);
        RedirectToActionResult redirect =
            Assert.IsType<RedirectToActionResult>(action);
        Assert.Equal(nameof(ApplicationsController.Index), redirect.ActionName);
        Assert.Equal("application-17", redirect.Fragment);
    }

    [Fact]
    public async Task Rename_Should_PreserveSubmittedNameAndReopenEditorOnStableFailure()
    {
        var store = new RecordingAdministrationStore
        {
            RenameResult = new AdministrationResult(
                false,
                "APPLICATION_NAME_DUPLICATE",
                null)
        };
        ApplicationsController controller = CreateController(
            store,
            "Local_IT_Admin");

        IActionResult action = await controller.Rename(
            17,
            "  Submitted name  ",
            CancellationToken.None);

        RedirectToActionResult redirect =
            Assert.IsType<RedirectToActionResult>(action);
        Assert.Equal("application-editor-17", redirect.Fragment);
        Assert.Equal(17, controller.TempData["EditorApplicationId"]);
        Assert.Equal(
            "Submitted name",
            controller.TempData["EditorApplicationName"]);
    }

    [Fact]
    public async Task Rename_Should_ReturnStablePrgErrorWhenApplicationIsUnknown()
    {
        var store = new RecordingAdministrationStore
        {
            RenameResult = new AdministrationResult(
                false,
                "APPLICATION_NOT_FOUND",
                null)
        };
        ApplicationsController controller = CreateController(
            store,
            "Local_IT_Admin");

        IActionResult action = await controller.Rename(
            999,
            "Submitted name",
            CancellationToken.None);

        RedirectToActionResult redirect =
            Assert.IsType<RedirectToActionResult>(action);
        Assert.Equal("page-heading", redirect.Fragment);
        Assert.Equal(
            "APPLICATION_NOT_FOUND: The Application was not found.",
            controller.TempData["ManagementError"]);
    }

    [Fact]
    public async Task SetActive_ShouldRejectInvalidModelBindingWithoutMutation()
    {
        var store = new RecordingAdministrationStore();
        ApplicationsController controller = CreateController(store, "Local_IT_Admin");
        controller.ModelState.AddModelError("isActive", "The value 'value' is not valid.");

        IActionResult action = await controller.SetActive(17, false);

        Assert.IsType<RedirectToActionResult>(action);
        Assert.Null(store.SetActiveCommand);
        Assert.NotNull(controller.TempData["ManagementError"]);
        Assert.Null(controller.TempData["ManagementSuccess"]);
    }

    [Fact]
    public async Task SetActive_Should_SurfaceStableDependencyErrorWithClearCopy()
    {
        var store = new RecordingAdministrationStore
        {
            SetActiveResult = new AdministrationResult(
                false,
                "APPLICATION_HAS_ACTIVE_DEPENDENCIES",
                17)
        };
        ApplicationsController controller = CreateController(
            store,
            "Local_IT_Admin");

        IActionResult action = await controller.SetActive(
            17,
            isActive: false,
            CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(action);
        string error = Assert.IsType<string>(
            controller.TempData["ManagementError"]);
        Assert.Contains("APPLICATION_HAS_ACTIVE_DEPENDENCIES", error);
        Assert.Contains("active Roles, Owner assignments, or Source Mappings", error);
    }

    [Fact]
    public async Task SetActive_Should_ReturnStablePrgErrorWhenApplicationIsUnknown()
    {
        var store = new RecordingAdministrationStore
        {
            SetActiveResult = new AdministrationResult(
                false,
                "APPLICATION_NOT_FOUND",
                null)
        };
        ApplicationsController controller = CreateController(
            store,
            "Local_IT_Admin");

        IActionResult action = await controller.SetActive(
            999,
            isActive: false,
            CancellationToken.None);

        RedirectToActionResult redirect =
            Assert.IsType<RedirectToActionResult>(action);
        Assert.Equal("page-heading", redirect.Fragment);
        Assert.Equal(
            "APPLICATION_NOT_FOUND: The Application was not found.",
            controller.TempData["ManagementError"]);
    }

    private static ApplicationsController CreateController(
        RecordingAdministrationStore store,
        string role,
        string employeeNo = "C1008267")
    {
        var controller = new ApplicationsController(
            new ApplicationAdministrationHandler(store));
        var httpContext = new DefaultHttpContext
        {
            User = CreatePrincipal(role, employeeNo)
        };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
        controller.TempData = new TempDataDictionary(
            httpContext,
            Mock.Of<ITempDataProvider>());
        return controller;
    }

    private static string GetPolicy(string actionName) =>
        typeof(ApplicationsController)
            .GetMethod(actionName)!
            .GetCustomAttribute<AuthorizeAttribute>()!
            .Policy!;

    private static ServiceProvider BuildAuthorizationServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRoleValidationAuthorization();
        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal CreatePrincipal(
        string role,
        string employeeNo = "C1008267")
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "person.user"),
                new Claim(ClaimTypes.Role, role),
                new Claim(
                    RoleValidationAuthenticationDefaults.EmployeeNoClaimType,
                    employeeNo)
            ],
            RoleValidationAuthenticationDefaults.CookieScheme,
            ClaimTypes.Name,
            ClaimTypes.Role));
    }

    private sealed class RecordingAdministrationStore
        : IRoleValidationAdministrationStore
    {
        public IReadOnlyList<ApplicationAdministrationRow> Applications
            { get; init; } = [];

        public AdministrationResult SetActiveResult { get; init; } =
            new(true, null, 17);

        public AdministrationResult? RenameResult { get; init; }

        public RenameApplicationCommand? RenameCommand { get; private set; }

        public SetApplicationActiveCommand? SetActiveCommand { get; private set; }

        public Task<IReadOnlyList<ApplicationAdministrationRow>>
            GetApplicationsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Applications);

        public Task<AdministrationResult> RenameApplicationAsync(
            RenameApplicationCommand command,
            CancellationToken cancellationToken = default)
        {
            RenameCommand = command;
            return Task.FromResult(RenameResult ?? new AdministrationResult(
                true,
                null,
                command.ApplicationId));
        }

        public Task<AdministrationResult> SetApplicationActiveAsync(
            SetApplicationActiveCommand command,
            CancellationToken cancellationToken = default)
        {
            SetActiveCommand = command;
            return Task.FromResult(SetActiveResult);
        }

        public Task<AdministrationResult> SaveValidationRoleAsync(
            SaveValidationRoleCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdministrationResult> DeactivateValidationRoleAsync(
            DeactivateValidationRoleCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdministrationResult> AssignOwnerAsync(
            AssignRoleOwnerCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdministrationResult> ReassignOwnerAsync(
            ReassignRoleOwnerCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdministrationResult> DeactivateOwnerAsync(
            DeactivateRoleOwnerCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdministrationResult> AddSourceMappingAsync(
            AddSourceMappingCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdministrationResult> ReplaceSourceMappingAsync(
            ReplaceSourceMappingCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdministrationResult> DeactivateSourceMappingAsync(
            DeactivateSourceMappingCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AuthorizedUserAdministrationRow>>
            GetAuthorizedUsersAsync(
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdministrationResult> AddAuthorizedUserAsync(
            AddAuthorizedUserCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdministrationResult> ChangeAuthorizedUserAsync(
            ChangeAuthorizedUserCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdministrationResult> DeactivateAuthorizedUserAsync(
            DeactivateAuthorizedUserCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdministrationResult> ReactivateAuthorizedUserAsync(
            ReactivateAuthorizedUserCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
