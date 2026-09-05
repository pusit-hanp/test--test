using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RoleValidation.Application.History;
using RoleValidation.Web.Authentication;
using RoleValidation.Web.Models.History;

namespace RoleValidation.Web.Controllers;

public sealed class HistoryController : Controller
{
    private readonly LoadChangeHistoryHandler _changeHandler;
    private readonly LoadLoginHistoryHandler _loginHandler;

    public HistoryController(
        LoadChangeHistoryHandler changeHandler,
        LoadLoginHistoryHandler loginHandler)
    {
        _changeHandler = changeHandler
            ?? throw new ArgumentNullException(nameof(changeHandler));
        _loginHandler = loginHandler
            ?? throw new ArgumentNullException(nameof(loginHandler));
    }

    [HttpGet]
    [Authorize(Policy =
        RoleValidationAuthorizationPolicies.LocalItAdministration)]
    public async Task<IActionResult> Changes(
        string? search,
        string? entityType,
        string? changeAction,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        ChangeHistoryPage historyPage = await _changeHandler.HandleAsync(
                new ChangeHistoryQuery(
                    search,
                    entityType,
                    changeAction,
                    page),
                cancellationToken);
        return View(new ChangeHistoryViewModel
        {
            Search = Normalize(search),
            EntityType = Normalize(entityType),
            Action = Normalize(changeAction),
            Rows = historyPage.Items,
            TotalCount = historyPage.TotalCount,
            PageNumber = historyPage.PageNumber,
            PageSize = historyPage.PageSize,
            TotalPages = historyPage.TotalPages
        });
    }

    [HttpGet]
    [Authorize(Policy =
        RoleValidationAuthorizationPolicies.LocalItAdministration)]
    public async Task<IActionResult> ChangeDetail(
        long id,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return NotFound();
        }

        ChangeHistoryDetail? detail =
            await _changeHandler.HandleDetailAsync(id, cancellationToken);
        return detail is null ? NotFound() : View(detail);
    }

    [HttpGet]
    [Authorize(Policy =
        RoleValidationAuthorizationPolicies.LocalItAdministration)]
    public async Task<IActionResult> Logins(
        string? employeeNo,
        string? correlationId,
        string? result,
        bool search = false,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        string? selectedResult = Normalize(result)?.ToUpperInvariant();
        LoginHistoryResult load =
            await _loginHandler.HandleAsync(
                new LoginHistoryQuery(
                    employeeNo,
                    correlationId,
                    result,
                    page),
                cancellationToken);
        LoginHistoryPage? historyPage = load.Page;
        return View(new LoginHistoryViewModel
        {
            SearchSubmitted = true,
            EmployeeNo = Normalize(employeeNo),
            CorrelationId = Normalize(correlationId),
            Result = selectedResult == "ALL" ? null : selectedResult,
            ErrorCode = load.ErrorCode,
            Rows = historyPage?.Items ?? [],
            TotalCount = historyPage?.TotalCount ?? 0,
            PageNumber = historyPage?.PageNumber ?? 1,
            PageSize = historyPage?.PageSize ?? LoadLoginHistoryHandler.PageSize,
            TotalPages = historyPage?.TotalPages ?? 1
        });
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
