using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RoleValidation.Application.Applications;
using RoleValidation.Application.Exports;
using RoleValidation.Application.RoleValidation;
using RoleValidation.Application.Users;
using RoleValidation.Core.Features.RoleValidation;
using RoleValidation.Core.Features.SourceMappings;
using RoleValidation.Infrastructure.ApplicationUsers;
using RoleValidation.Web.Authentication;
using RoleValidation.Web.Models.ApplicationUsers;

namespace RoleValidation.Web.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ApplicationUsersController : Controller
{
    private static readonly int[] AllowedPageSizes = [25, 50, 100, 200];

    private readonly IApplicationReader _applicationReader;
    private readonly LoadApplicationUserHandler _handler;
    private readonly IApplicationUserWorkbookExporter _workbookExporter;
    private readonly ILogger<ApplicationUsersController> _logger;

    public ApplicationUsersController(
        IApplicationReader applicationReader,
        LoadApplicationUserHandler handler,
        IApplicationUserWorkbookExporter workbookExporter,
        ILogger<ApplicationUsersController> logger)
    {
        _applicationReader = applicationReader;
        _handler = handler;
        _workbookExporter = workbookExporter;
        _logger = logger;
    }

    [HttpGet]
    [Authorize(Policy = RoleValidationAuthorizationPolicies.AdminRead)]
    public async Task<IActionResult> Index(
        int? applicationId,
        string? search,
        int[]? mappedRoleIds,
        MappedRoleSelectionMode mappedRoleSelectionMode,
        EmployeeStatusType[]? employeeStatus,
        FilterSelectionMode? employeeStatusSelectionMode,
        SourceRoleResolutionType[]? resolutionType,
        FilterSelectionMode? resolutionSelectionMode,
        bool[]? isAudited,
        FilterSelectionMode? auditedSelectionMode,
        ApplicationUserSortField? sortBy = null,
        ApplicationUserSortDirection sortDirection =
            ApplicationUserSortDirection.Ascending,
        int page = 1,
        int pageSize = LoadApplicationUserRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = NormalizePageSize(pageSize);
        int[] selectedRoleIds = NormalizeRoleIds(mappedRoleIds);
        EmployeeStatusType[] selectedEmployeeStatuses =
            NormalizeValues(employeeStatus);
        SourceRoleResolutionType[] selectedResolutionTypes =
            NormalizeValues(resolutionType);
        bool[] selectedAuditedStatuses = NormalizeValues(isAudited);
        FilterSelectionMode employeeMode = ResolveSelectionMode(
            employeeStatusSelectionMode,
            selectedEmployeeStatuses,
            FilterSelectionMode.All);
        FilterSelectionMode resolutionMode = ResolveSelectionMode(
            resolutionSelectionMode,
            selectedResolutionTypes,
            FilterSelectionMode.All);
        FilterSelectionMode auditedMode = ResolveSelectionMode(
            auditedSelectionMode,
            selectedAuditedStatuses,
            FilterSelectionMode.Selected);

        if (!auditedSelectionMode.HasValue &&
            selectedAuditedStatuses.Length == 0)
        {
            selectedAuditedStatuses = [true];
        }

        employeeMode = NormalizeEmptySelection(
            employeeMode,
            selectedEmployeeStatuses);
        resolutionMode = NormalizeEmptySelection(
            resolutionMode,
            selectedResolutionTypes);
        auditedMode = NormalizeEmptySelection(
            auditedMode,
            selectedAuditedStatuses);

        IReadOnlyList<ApplicationSummary> applications =
            await _applicationReader.GetActiveAsync(cancellationToken);

        int? selectedId = applicationId
            ?? applications.FirstOrDefault()?.ApplicationId;
        ApplicationSummary? selected = applications.FirstOrDefault(
            application => application.ApplicationId == selectedId);

        if (!selectedId.HasValue)
        {
            return View(CreateModel(
                applications,
                null,
                null,
                search,
                selectedRoleIds,
                mappedRoleSelectionMode,
                selectedEmployeeStatuses,
                employeeMode,
                selectedResolutionTypes,
                resolutionMode,
                selectedAuditedStatuses,
                auditedMode,
                pageSize,
                sortBy: sortBy,
                sortDirection: sortDirection));
        }

        if (selected is null)
        {
            return View(CreateModel(
                applications,
                selectedId,
                null,
                search,
                selectedRoleIds,
                mappedRoleSelectionMode,
                selectedEmployeeStatuses,
                employeeMode,
                selectedResolutionTypes,
                resolutionMode,
                selectedAuditedStatuses,
                auditedMode,
                pageSize,
                errorMessage: "The selected application was not found.",
                sortBy: sortBy,
                sortDirection: sortDirection));
        }

        try
        {
            var request = new LoadApplicationUserRequest(
                applicationId: selectedId.Value,
                search,
                selectedRoleIds,
                mappedRoleSelectionMode,
                employeeStatus: null,
                resolutionType: null,
                page,
                pageSize,
                isAudited: null,
                employeeStatuses: selectedEmployeeStatuses,
                employeeStatusSelectionMode: employeeMode,
                resolutionTypes: selectedResolutionTypes,
                resolutionSelectionMode: resolutionMode,
                auditedStatuses: selectedAuditedStatuses,
                auditedSelectionMode: auditedMode,
                sortBy: sortBy,
                sortDirection: sortDirection);

            LoadApplicationUserResult result =
                await _handler.HandleAsync(request, cancellationToken);

            if (result.Status == LoadApplicationUserStatus.ApplicationNotFound)
            {
                return View(CreateModel(
                    applications,
                    selectedId,
                    selected.ApplicationName,
                    search,
                    selectedRoleIds,
                    mappedRoleSelectionMode,
                    selectedEmployeeStatuses,
                    employeeMode,
                    selectedResolutionTypes,
                    resolutionMode,
                    selectedAuditedStatuses,
                    auditedMode,
                    pageSize,
                    errorMessage: "The selected application was not found.",
                    sortBy: sortBy,
                    sortDirection: sortDirection));
            }

            return View(CreateModel(
                applications,
                selectedId,
                selected.ApplicationName,
                request.Search,
                request.MappedRoleIds,
                request.MappedRoleSelectionMode,
                request.EmployeeStatuses,
                request.EmployeeStatusSelectionMode,
                request.ResolutionTypes,
                request.ResolutionSelectionMode,
                request.AuditedStatuses,
                request.AuditedSelectionMode,
                pageSize,
                result,
                sortBy: request.SortBy,
                sortDirection: request.SortDirection));
        }
        catch (ApplicationUserProviderNotFoundException exception)
        {
            _logger.LogWarning(
                exception,
                "No application-user provider for {ApplicationId}",
                selectedId);

            return View(CreateModel(
                applications,
                selectedId,
                selected.ApplicationName,
                search,
                selectedRoleIds,
                mappedRoleSelectionMode,
                selectedEmployeeStatuses,
                employeeMode,
                selectedResolutionTypes,
                resolutionMode,
                selectedAuditedStatuses,
                auditedMode,
                pageSize,
                errorMessage:
                    "This application does not have a verified data provider.",
                sortBy: sortBy,
                sortDirection: sortDirection));
        }
        catch (OperationCanceledException)
            when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unable to load users for application {ApplicationId}",
                selectedId);
            Response.StatusCode = StatusCodes.Status500InternalServerError;

            return View(CreateModel(
                applications,
                selectedId,
                selected.ApplicationName,
                search,
                selectedRoleIds,
                mappedRoleSelectionMode,
                selectedEmployeeStatuses,
                employeeMode,
                selectedResolutionTypes,
                resolutionMode,
                selectedAuditedStatuses,
                auditedMode,
                pageSize,
                errorMessage:
                    "Unable to load application users. Please try again or check the configuration.",
                sortBy: sortBy,
                sortDirection: sortDirection));
        }
    }

    [HttpGet]
    [Authorize(Policy = RoleValidationAuthorizationPolicies.AdminRead)]
    public async Task<IActionResult> Export(
        int applicationId,
        string? search,
        int[]? mappedRoleIds,
        MappedRoleSelectionMode mappedRoleSelectionMode,
        EmployeeStatusType[]? employeeStatus,
        FilterSelectionMode? employeeStatusSelectionMode,
        SourceRoleResolutionType[]? resolutionType,
        FilterSelectionMode? resolutionSelectionMode,
        bool[]? isAudited,
        FilterSelectionMode? auditedSelectionMode,
        ApplicationUserSortField? sortBy = null,
        ApplicationUserSortDirection sortDirection =
            ApplicationUserSortDirection.Ascending,
        CancellationToken cancellationToken = default)
    {
        try
        {
            EmployeeStatusType[] selectedEmployeeStatuses =
                NormalizeValues(employeeStatus);
            SourceRoleResolutionType[] selectedResolutionTypes =
                NormalizeValues(resolutionType);
            bool[] selectedAuditedStatuses = NormalizeValues(isAudited);
            FilterSelectionMode employeeMode = NormalizeEmptySelection(
                ResolveSelectionMode(
                    employeeStatusSelectionMode,
                    selectedEmployeeStatuses,
                    FilterSelectionMode.All),
                selectedEmployeeStatuses);
            FilterSelectionMode resolutionMode = NormalizeEmptySelection(
                ResolveSelectionMode(
                    resolutionSelectionMode,
                    selectedResolutionTypes,
                    FilterSelectionMode.All),
                selectedResolutionTypes);
            FilterSelectionMode auditedMode = ResolveSelectionMode(
                auditedSelectionMode,
                selectedAuditedStatuses,
                FilterSelectionMode.Selected);

            if (!auditedSelectionMode.HasValue &&
                selectedAuditedStatuses.Length == 0)
            {
                selectedAuditedStatuses = [true];
            }

            auditedMode = NormalizeEmptySelection(
                auditedMode,
                selectedAuditedStatuses);

            IReadOnlyList<ApplicationSummary> activeApplications =
                await _applicationReader.GetActiveAsync(cancellationToken);
            ApplicationSummary? application = activeApplications
                .FirstOrDefault(item => item.ApplicationId == applicationId);

            if (application is null)
            {
                return NotFound();
            }

            var request = new LoadApplicationUserRequest(
                applicationId,
                search,
                NormalizeRoleIds(mappedRoleIds),
                mappedRoleSelectionMode,
                employeeStatus: null,
                resolutionType: null,
                pageNumber: 1,
                pageSize: LoadApplicationUserRequest.MaximumPageSize,
                includeAll: true,
                isAudited: null,
                employeeStatuses: selectedEmployeeStatuses,
                employeeStatusSelectionMode: employeeMode,
                resolutionTypes: selectedResolutionTypes,
                resolutionSelectionMode: resolutionMode,
                auditedStatuses: selectedAuditedStatuses,
                auditedSelectionMode: auditedMode,
                sortBy: sortBy,
                sortDirection: sortDirection);

            LoadApplicationUserResult result =
                await _handler.HandleAsync(request, cancellationToken);

            if (result.Status != LoadApplicationUserStatus.Success)
            {
                return NotFound();
            }

            string exportedBy = User.Identity?.Name
                ?? "Unauthenticated development user";
            DateTimeOffset exportedAt = DateTimeOffset.UtcNow;
            ApplicationUserExportProfile exportProfile =
                ApplicationUserExportProfile.ForApplication(
                    application.ApplicationCode);
            byte[] workbook = _workbookExporter.Export(
                application.ApplicationName,
                exportProfile,
                exportedAt,
                exportedBy,
                ApplicationUserExportDeduplicator.Deduplicate(
                    result.Users,
                    exportProfile,
                    exportedAt));

            string timestamp = exportedAt.ToString(
                "yyyyMMdd-HHmmss",
                CultureInfo.InvariantCulture);
            string fileName =
                $"{CreateSafeFileName(application.ApplicationName)}-" +
                $"{timestamp}.xlsx";

            return File(
                workbook,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }
        catch (OperationCanceledException)
            when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unable to export users for application {ApplicationId}",
                applicationId);

            return Problem(
                detail: "Unable to prepare the Excel file. Please try again or check the configuration.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Error()
    {
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        return View(
            "Index",
            CreateModel(
                [],
                null,
                null,
                null,
                [],
                MappedRoleSelectionMode.All,
                [],
                FilterSelectionMode.All,
                [],
                FilterSelectionMode.All,
                [true],
                FilterSelectionMode.Selected,
                LoadApplicationUserRequest.DefaultPageSize,
                errorMessage: "The request could not be processed."));
    }

    private static ApplicationUserListViewModel CreateModel(
        IReadOnlyList<ApplicationSummary> applications,
        int? selectedApplicationId,
        string? selectedApplicationName,
        string? search,
        IReadOnlyList<int> selectedMappedRoleIds,
        MappedRoleSelectionMode mappedRoleSelectionMode,
        IReadOnlyList<EmployeeStatusType> selectedEmployeeStatuses,
        FilterSelectionMode employeeStatusSelectionMode,
        IReadOnlyList<SourceRoleResolutionType> selectedResolutionTypes,
        FilterSelectionMode resolutionSelectionMode,
        IReadOnlyList<bool> selectedAuditedStatuses,
        FilterSelectionMode auditedSelectionMode,
        int pageSize,
        LoadApplicationUserResult? result = null,
        string? errorMessage = null,
        ApplicationUserSortField? sortBy = null,
        ApplicationUserSortDirection sortDirection =
            ApplicationUserSortDirection.Ascending)
    {
        DateTime todayUtc = DateTime.UtcNow.Date;
        IReadOnlyList<ApplicationUserRowViewModel> rows =
            result?.Users
                .Select(user => Map(user, todayUtc))
                .ToList()
            ?? [];

        return new ApplicationUserListViewModel
        {
            Applications = applications
                .Select(application => new ApplicationOptionViewModel(
                    application.ApplicationId,
                    application.ApplicationName))
                .ToList(),
            SelectedApplicationId = selectedApplicationId,
            SelectedApplicationName = selectedApplicationName,
            Search = search,
            SelectedMappedRoleIds = selectedMappedRoleIds,
            MappedRoleSelectionMode = mappedRoleSelectionMode,
            AvailableMappedRoles = result?.AvailableMappedRoles ?? [],
            SelectedEmployeeStatuses = selectedEmployeeStatuses,
            EmployeeStatusSelectionMode = employeeStatusSelectionMode,
            EmployeeStatusFilter = selectedEmployeeStatuses.Count == 1
                ? selectedEmployeeStatuses[0]
                : null,
            SelectedAuditedStatuses = selectedAuditedStatuses,
            AuditedSelectionMode = auditedSelectionMode,
            IsAuditedFilter = selectedAuditedStatuses.Count == 1
                ? selectedAuditedStatuses[0]
                : null,
            SelectedResolutionTypes = selectedResolutionTypes,
            ResolutionSelectionMode = resolutionSelectionMode,
            ResolutionTypeFilter = selectedResolutionTypes.Count == 1
                ? selectedResolutionTypes[0]
                : null,
            SortBy = sortBy,
            SortDirection = sortDirection,
            Users = rows,
            ErrorMessage = errorMessage,
            TotalCount = result?.TotalCount ?? 0,
            PageNumber = result?.PageNumber ?? 1,
            PageSize = result?.PageSize ?? pageSize,
            TotalPages = result?.TotalPages ?? 1,
            ResolvedCount = result?.ResolvedCount ?? 0,
            NeedsMappingCount = result?.NeedsMappingCount ?? 0,
            InactiveCount = result?.InactiveCount ?? 0
        };
    }

    private static ApplicationUserRowViewModel Map(
        ApplicationUserView user,
        DateTime todayUtc)
    {
        int? daysSinceLastLogin = user.LastLoginAt.HasValue
            ? Math.Max(
                0,
                (todayUtc - user.LastLoginAt.Value.ToUniversalTime().Date).Days)
            : null;

        return new ApplicationUserRowViewModel(
            user.EmployeeNo,
            user.UserName,
            user.EmployeeName,
            user.Email,
            user.Position,
            user.Department,
            user.SourceRoleKey,
            user.SourceRoleDisplayName,
            user.RoleName,
            daysSinceLastLogin,
            user.IsAudited,
            user.EmployeeStatus?.StatusType ?? EmployeeStatusType.Unknown,
            user.ResolutionType);
    }

    private static int NormalizePageSize(int pageSize)
    {
        return AllowedPageSizes.Contains(pageSize)
            ? pageSize
            : LoadApplicationUserRequest.DefaultPageSize;
    }

    private static int[] NormalizeRoleIds(int[]? mappedRoleIds)
    {
        return (mappedRoleIds ?? [])
            .Distinct()
            .ToArray();
    }

    private static T[] NormalizeValues<T>(T[]? values)
    {
        return (values ?? [])
            .Distinct()
            .ToArray();
    }

    private static FilterSelectionMode ResolveSelectionMode<T>(
        FilterSelectionMode? requestedMode,
        IReadOnlyCollection<T> selectedValues,
        FilterSelectionMode defaultMode)
    {
        return requestedMode ??
            (selectedValues.Count > 0
                ? FilterSelectionMode.Selected
                : defaultMode);
    }

    private static FilterSelectionMode NormalizeEmptySelection<T>(
        FilterSelectionMode mode,
        IReadOnlyCollection<T> selectedValues)
    {
        return mode == FilterSelectionMode.Selected &&
               selectedValues.Count == 0
            ? FilterSelectionMode.None
            : mode;
    }

    private static string CreateSafeFileName(string value)
    {
        string result = new(
            value
                .Where(character =>
                    char.IsLetterOrDigit(character) ||
                    character is '-' or '_')
                .ToArray());

        return string.IsNullOrWhiteSpace(result)
            ? "application-users"
            : result;
    }
}
