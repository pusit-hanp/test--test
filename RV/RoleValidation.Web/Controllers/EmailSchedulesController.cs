using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using RoleValidation.Application.Administration;
using RoleValidation.Application.Email;
using RoleValidation.Core.Features.Email;
using RoleValidation.Web.Authentication;
using RoleValidation.Web.Configuration;
using RoleValidation.Web.Email;
using RoleValidation.Web.Models.Email;

namespace RoleValidation.Web.Controllers;

[Authorize(Policy =
    RoleValidationAuthorizationPolicies.LocalItAdministration)]
[TypeFilter(typeof(EmailConfigurationGuard))]
public sealed class EmailSchedulesController : Controller
{
    private const string ProcessingUnavailableCode =
        "EMAIL_PROCESSING_NOT_AVAILABLE";
    private static readonly TimeSpan BangkokOffset = TimeSpan.FromHours(7);

    private readonly IServiceProvider _services;
    private readonly IServiceProviderIsService _serviceAvailability;
    private readonly IWebHostEnvironment _environment;
    private readonly EmailProcessingCapability _processingCapability;
    private readonly EmailOptions _emailOptions;

    public EmailSchedulesController(
        IServiceProvider services,
        IServiceProviderIsService serviceAvailability,
        IWebHostEnvironment environment,
        EmailProcessingCapability processingCapability,
        EmailOptions emailOptions)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _serviceAvailability = serviceAvailability ??
            throw new ArgumentNullException(nameof(serviceAvailability));
        _environment = environment ??
            throw new ArgumentNullException(nameof(environment));
        _processingCapability = processingCapability ??
            throw new ArgumentNullException(nameof(processingCapability));
        _emailOptions = emailOptions ??
            throw new ArgumentNullException(nameof(emailOptions));
    }

    [HttpGet]
    [Authorize(Policy =
        RoleValidationAuthorizationPolicies.LocalItAdministration)]
    public async Task<IActionResult> Index(
        int? applicationId,
        CancellationToken cancellationToken = default)
    {
        if (!AreAvailable(
                typeof(IEmailManagementReader),
                typeof(EmailConfigurationSnapshot)))
        {
            return NotFound();
        }

        IEmailManagementReader reader = Resolve<IEmailManagementReader>();
        EmailConfigurationSnapshot configuration =
            Resolve<EmailConfigurationSnapshot>();
        IReadOnlyList<EmailApplicationOverview> applications =
            await reader.GetEmailApplicationOverviewsAsync(cancellationToken);
        EmailApplicationOverview? selected = applicationId.HasValue
            ? applications.FirstOrDefault(application =>
                application.ApplicationId == applicationId.Value)
            : applications.FirstOrDefault(application =>
                  application.Readiness.IsReady) ?? applications.FirstOrDefault();

        if (applicationId.HasValue && selected is null)
        {
            return PlainText(
                "APPLICATION_NOT_FOUND",
                StatusCodes.Status404NotFound);
        }

        IReadOnlyList<EmailRunSummary> recentRuns = selected is null
            ? []
            : await reader.GetRecentEmailRunsAsync(
                selected.ApplicationId,
                20,
                cancellationToken);
        string? safeRedirectEmployeeNo =
            string.Equals(
                configuration.RecipientMode,
                "SAFE_REDIRECT",
                StringComparison.Ordinal)
                ? NormalizeOptional(_emailOptions.SafeRedirectEmployeeNo)
                : null;
        var model = new EmailScheduleManagementViewModel
        {
            Applications = applications,
            SelectedApplication = selected,
            RecentRuns = recentRuns,
            Configuration = configuration,
            EnvironmentName = _environment.EnvironmentName,
            SafeRedirectEmployeeNo = safeRedirectEmployeeNo,
            ProcessingAvailable = _processingCapability.IsEnabled,
            SuccessMessage = TempData["ManagementSuccess"] as string,
            ErrorMessage = TempData["ManagementError"] as string,
            FocusTarget = TempData["FocusTarget"] as string,
            NextRunDate = TempData["NextRunDate"] as string ??
                selected?.Schedule?.NextRunAt.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture),
            NextRunTime = TempData["NextRunTime"] as string ??
                selected?.Schedule?.NextRunAt.ToString(
                    "HH:mm",
                    CultureInfo.InvariantCulture)
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy =
        RoleValidationAuthorizationPolicies.LocalItAdministration)]
    public async Task<IActionResult> Save(
        int applicationId,
        string? nextRunDate,
        string? nextRunTime,
        CancellationToken cancellationToken = default)
    {
        if (!AreAvailable(typeof(EmailScheduleAdministrationHandler)))
        {
            return NotFound();
        }

        string? actorEmployeeNo = GetActorEmployeeNo();
        if (actorEmployeeNo is null)
        {
            return Unauthorized();
        }

        if (applicationId <= 0)
        {
            return PlainText(
                "APPLICATION_NOT_FOUND",
                StatusCodes.Status404NotFound);
        }

        if (!TryCreateOccurrence(
                nextRunDate,
                nextRunTime,
                out DateTimeOffset occurrence))
        {
            TempData["ManagementError"] =
                "Schedule date and time must use yyyy-MM-dd and HH:mm for " +
                "Bangkok +07:00. 29 February is not accepted.";
            TempData["NextRunDate"] = nextRunDate ?? string.Empty;
            TempData["NextRunTime"] = nextRunTime ?? string.Empty;
            TempData["FocusTarget"] = "schedule-error";
            return RedirectToApplication(applicationId);
        }

        EmailScheduleAdministrationHandler handler =
            Resolve<EmailScheduleAdministrationHandler>();
        AdministrationResult result = await handler.SaveAsync(
            new SaveEmailScheduleCommand(
                applicationId,
                occurrence,
                actorEmployeeNo),
            cancellationToken);
        ApplyAdministrationResult(
            result,
            "Annual occurrence saved. New schedules remain inactive.",
            "schedule-error");
        return RedirectToApplication(applicationId);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy =
        RoleValidationAuthorizationPolicies.LocalItAdministration)]
    public async Task<IActionResult> SetActive(
        int applicationId,
        int emailScheduleId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        if (!AreAvailable(typeof(EmailScheduleAdministrationHandler)) ||
            isActive && !AreAvailable(typeof(IEmailManagementReader)))
        {
            return NotFound();
        }

        string? actorEmployeeNo = GetActorEmployeeNo();
        if (actorEmployeeNo is null)
        {
            return Unauthorized();
        }

        if (applicationId <= 0)
        {
            return PlainText(
                "APPLICATION_NOT_FOUND",
                StatusCodes.Status404NotFound);
        }

        if (emailScheduleId <= 0)
        {
            return PlainText(
                "EMAIL_SCHEDULE_NOT_FOUND",
                StatusCodes.Status404NotFound);
        }

        if (isActive)
        {
            if (!_processingCapability.IsEnabled)
            {
                return PlainText(
                    ProcessingUnavailableCode,
                    StatusCodes.Status409Conflict);
            }

            IEmailManagementReader reader = Resolve<IEmailManagementReader>();
            IReadOnlyList<EmailApplicationOverview> applications =
                await reader.GetEmailApplicationOverviewsAsync(cancellationToken);
            EmailApplicationOverview? application = applications
                .FirstOrDefault(row => row.ApplicationId == applicationId);
            if (application is null)
            {
                return PlainText(
                    "APPLICATION_NOT_FOUND",
                    StatusCodes.Status404NotFound);
            }

            if (application.Schedule?.EmailScheduleId != emailScheduleId)
            {
                return PlainText(
                    "EMAIL_SCHEDULE_NOT_FOUND",
                    StatusCodes.Status404NotFound);
            }

            if (!application.Readiness.IsReady)
            {
                return PlainText(
                    application.Readiness.ErrorCode!,
                    StatusCodes.Status409Conflict);
            }
        }

        EmailScheduleAdministrationHandler handler =
            Resolve<EmailScheduleAdministrationHandler>();
        AdministrationResult result = await handler.SetActiveAsync(
            new SetEmailScheduleActiveCommand(
                applicationId,
                emailScheduleId,
                isActive,
                actorEmployeeNo),
            cancellationToken);
        ApplyAdministrationResult(
            result,
            isActive
                ? "Annual schedule activated."
                : "Annual schedule deactivated.",
            "schedule-error");
        return RedirectToApplication(applicationId);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy =
        RoleValidationAuthorizationPolicies.LocalItAdministration)]
    public async Task<IActionResult> RunNow(
        int applicationId,
        CancellationToken cancellationToken = default)
    {
        if (!AreAvailable(
                typeof(IEmailManagementReader),
                typeof(CreateRunNowHandler),
                typeof(EmailConfigurationSnapshot)))
        {
            return NotFound();
        }

        if (!_processingCapability.IsEnabled)
        {
            return PlainText(
                ProcessingUnavailableCode,
                StatusCodes.Status409Conflict);
        }

        string? actorEmployeeNo = GetActorEmployeeNo();
        if (actorEmployeeNo is null)
        {
            return Unauthorized();
        }

        IEmailManagementReader reader = Resolve<IEmailManagementReader>();
        IReadOnlyList<EmailApplicationOverview> applications =
            await reader.GetEmailApplicationOverviewsAsync(cancellationToken);
        EmailApplicationOverview? application = applications
            .FirstOrDefault(row => row.ApplicationId == applicationId);
        if (application is null)
        {
            return PlainText(
                "APPLICATION_NOT_FOUND",
                StatusCodes.Status404NotFound);
        }

        if (!application.Readiness.IsReady)
        {
            return PlainText(
                application.Readiness.ErrorCode!,
                StatusCodes.Status409Conflict);
        }

        if (application.ActiveEmailRunId.HasValue)
        {
            return PlainText(
                "EMAIL_ACTIVE_RUN_EXISTS",
                StatusCodes.Status409Conflict);
        }

        CreateRunNowHandler handler = Resolve<CreateRunNowHandler>();
        EmailConfigurationSnapshot snapshot =
            Resolve<EmailConfigurationSnapshot>();
        CreateEmailRunResult result = await handler.CreateAsync(
            applicationId,
            actorEmployeeNo,
            snapshot,
            cancellationToken);
        if (result.Outcome == EmailRunCreationOutcome.Created &&
            result.EmailRunId is long emailRunId && emailRunId > 0)
        {
            TempData["ManagementSuccess"] =
                $"Run {emailRunId} created.";
            TempData["FocusTarget"] = $"run-{emailRunId}";
            return RedirectToAction(
                "Index",
                "EmailRuns",
                new { id = emailRunId });
        }

        string errorCode = result.ErrorCode ?? "EMAIL_RUN_NOT_CREATED";
        TempData["ManagementError"] = errorCode;
        TempData["FocusTarget"] = "run-now-error";
        return RedirectToApplication(applicationId);
    }

    private bool AreAvailable(params Type[] serviceTypes) =>
        serviceTypes.All(_serviceAvailability.IsService);

    private T Resolve<T>() where T : notnull =>
        _services.GetRequiredService<T>();

    private string? GetActorEmployeeNo()
    {
        string? value = User.FindFirstValue(
            RoleValidationAuthenticationDefaults.EmployeeNoClaimType);
        return NormalizeOptional(value);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool TryCreateOccurrence(
        string? nextRunDate,
        string? nextRunTime,
        out DateTimeOffset occurrence)
    {
        occurrence = default;
        if (!DateOnly.TryParseExact(
                nextRunDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly date) ||
            !TimeOnly.TryParseExact(
                nextRunTime,
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out TimeOnly time))
        {
            return false;
        }

        var candidate = new DateTimeOffset(
            date.ToDateTime(time, DateTimeKind.Unspecified),
            BangkokOffset);
        try
        {
            occurrence = AnnualSchedule.Create(candidate, isActive: false)
                .NextRunAt;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private void ApplyAdministrationResult(
        AdministrationResult result,
        string successMessage,
        string errorFocus)
    {
        if (result.Succeeded)
        {
            TempData["ManagementSuccess"] = successMessage;
            TempData["FocusTarget"] = "schedule-editor";
            return;
        }

        TempData["ManagementError"] =
            result.ErrorCode ?? "EMAIL_ADMINISTRATION_ERROR";
        TempData["FocusTarget"] = errorFocus;
    }

    private RedirectToActionResult RedirectToApplication(int applicationId) =>
        RedirectToAction(nameof(Index), new { applicationId });

    private static ContentResult PlainText(string content, int statusCode) =>
        new()
        {
            Content = content,
            ContentType = "text/plain; charset=utf-8",
            StatusCode = statusCode
        };
}
