using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RoleValidation.Application.Administration;
using RoleValidation.Web.Authentication;
using RoleValidation.Web.Models.Applications;

namespace RoleValidation.Web.Controllers;

public sealed class ApplicationsController : Controller
{
    private readonly ApplicationAdministrationHandler _handler;

    public ApplicationsController(ApplicationAdministrationHandler handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    [HttpGet]
    [Authorize(Policy = RoleValidationAuthorizationPolicies.AdminRead)]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ApplicationAdministrationRow> applications =
            await _handler.GetApplicationsAsync(cancellationToken);
        var model = new ApplicationManagementViewModel
        {
            Applications = applications,
            CanManage = User.IsInRole("Local_IT_Admin"),
            SuccessMessage = TempData["ManagementSuccess"] as string,
            ErrorMessage = TempData["ManagementError"] as string,
            FocusTarget = TempData["FocusTarget"] as string,
            EditorApplicationId = ReadTempDataInt("EditorApplicationId"),
            EditorApplicationName = TempData["EditorApplicationName"] as string
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy =
        RoleValidationAuthorizationPolicies.LocalItAdministration)]
    public async Task<IActionResult> Rename(
        int applicationId,
        string? applicationName,
        CancellationToken cancellationToken = default)
    {
        string normalizedName = applicationName?.Trim() ?? string.Empty;
        string? actorEmployeeNo = GetActorEmployeeNo();
        if (applicationId <= 0 || normalizedName.Length == 0)
        {
            TempData["ManagementError"] =
                "Application display name is required after trim.";
            PreserveEditor(applicationId, normalizedName);
            return RedirectToApplication(applicationId, reopenEditor: true);
        }

        if (actorEmployeeNo is null)
        {
            TempData["ManagementError"] =
                "Current employee identity is unavailable.";
            PreserveEditor(applicationId, normalizedName);
            return RedirectToApplication(applicationId, reopenEditor: true);
        }

        AdministrationResult result = await _handler.RenameApplicationAsync(
            new RenameApplicationCommand(
                applicationId,
                normalizedName,
                actorEmployeeNo),
            cancellationToken);
        ApplyResult(
            result,
            successMessage: "Application display name saved.");

        bool applicationNotFound =
            result.ErrorCode == "APPLICATION_NOT_FOUND";
        if (!result.Succeeded && !applicationNotFound)
        {
            PreserveEditor(applicationId, normalizedName);
        }

        return RedirectToApplication(
            applicationNotFound ? 0 : applicationId,
            reopenEditor: !result.Succeeded && !applicationNotFound);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy =
        RoleValidationAuthorizationPolicies.LocalItAdministration)]
    public async Task<IActionResult> SetActive(
        int applicationId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            TempData["ManagementError"] = "Application status is invalid.";
            return RedirectToApplication(applicationId, reopenEditor: true);
        }

        string? actorEmployeeNo = GetActorEmployeeNo();
        if (applicationId <= 0)
        {
            TempData["ManagementError"] = "Application ID is invalid.";
            return RedirectToApplication(applicationId, reopenEditor: true);
        }

        if (actorEmployeeNo is null)
        {
            TempData["ManagementError"] =
                "Current employee identity is unavailable.";
            return RedirectToApplication(applicationId, reopenEditor: true);
        }

        AdministrationResult result = await _handler.SetApplicationActiveAsync(
            new SetApplicationActiveCommand(
                applicationId,
                isActive,
                actorEmployeeNo),
            cancellationToken);
        ApplyResult(
            result,
            isActive
                ? "Application activated."
                : "Application deactivated.");

        return RedirectToApplication(
            result.ErrorCode == "APPLICATION_NOT_FOUND" ? 0 : applicationId);
    }

    private string? GetActorEmployeeNo()
    {
        string? value = User.FindFirstValue(
            RoleValidationAuthenticationDefaults.EmployeeNoClaimType);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private void ApplyResult(
        AdministrationResult result,
        string successMessage)
    {
        if (result.Succeeded)
        {
            TempData["ManagementSuccess"] = successMessage;
            TempData["FocusTarget"] = result.EntityId is int entityId
                ? $"application-{entityId}"
                : "page-heading";
            return;
        }

        TempData["ManagementError"] = result.ErrorCode switch
        {
            "APPLICATION_NAME_DUPLICATE" =>
                "APPLICATION_NAME_DUPLICATE: Another Application already uses " +
                "that display name after trim and case folding.",
            "APPLICATION_HAS_ACTIVE_DEPENDENCIES" =>
                "APPLICATION_HAS_ACTIVE_DEPENDENCIES: Deactivate active Roles, " +
                "Owner assignments, or Source Mappings first.",
            "APPLICATION_NOT_FOUND" =>
                "APPLICATION_NOT_FOUND: The Application was not found.",
            _ => $"{result.ErrorCode ?? "ADMINISTRATION_ERROR"}: " +
                 "The Application change was not saved."
        };
    }

    private int? ReadTempDataInt(string key)
    {
        object? value = TempData[key];
        return value switch
        {
            int integer => integer,
            string text when int.TryParse(text, out int parsed) => parsed,
            _ => null
        };
    }

    private void PreserveEditor(int applicationId, string applicationName)
    {
        TempData["EditorApplicationId"] = applicationId;
        TempData["EditorApplicationName"] = applicationName;
    }

    private RedirectToActionResult RedirectToApplication(
        int applicationId,
        bool reopenEditor = false)
    {
        return RedirectToAction(
            nameof(Index),
            controllerName: null,
            routeValues: null,
            fragment: applicationId > 0
                ? reopenEditor
                    ? $"application-editor-{applicationId}"
                    : $"application-{applicationId}"
                : "page-heading");
    }
}
