using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
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
public sealed class EmailRunsController : Controller
{
    private const string ProcessingUnavailableCode =
        "EMAIL_PROCESSING_NOT_AVAILABLE";

    private readonly IServiceProvider _services;
    private readonly IServiceProviderIsService _serviceAvailability;
    private readonly EmailProcessingCapability _processingCapability;

    public EmailRunsController(
        IServiceProvider services,
        IServiceProviderIsService serviceAvailability,
        EmailProcessingCapability processingCapability)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _serviceAvailability = serviceAvailability ??
            throw new ArgumentNullException(nameof(serviceAvailability));
        _processingCapability = processingCapability ??
            throw new ArgumentNullException(nameof(processingCapability));
    }

    [HttpGet]
    [Authorize(Policy =
        RoleValidationAuthorizationPolicies.LocalItAdministration)]
    public async Task<IActionResult> Index(
        long id,
        CancellationToken cancellationToken = default)
    {
        if (!AreAvailable(typeof(IEmailManagementReader)))
        {
            return NotFound();
        }

        if (id <= 0)
        {
            return BadRequest();
        }

        IEmailManagementReader reader = Resolve<IEmailManagementReader>();
        EmailRunDetail? detail = await reader.GetEmailRunDetailAsync(
            id,
            cancellationToken);
        if (!IsExactRun(detail, id))
        {
            return NotFound();
        }

        return View(CreateModel(
            detail!,
            TempData["ManagementSuccess"] as string,
            TempData["ManagementError"] as string,
            TempData["FocusTarget"] as string));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy =
        RoleValidationAuthorizationPolicies.LocalItAdministration)]
    public Task<IActionResult> ConfirmAccepted(
        long emailRunId,
        long emailDeliveryId,
        int expectedAttemptCount,
        string? externalRequestId,
        CancellationToken cancellationToken = default) =>
        ResolveUnknownAsync(
            emailRunId,
            emailDeliveryId,
            expectedAttemptCount,
            EmailUnknownResolutionAction.ConfirmAccepted,
            externalRequestId,
            cancellationToken);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy =
        RoleValidationAuthorizationPolicies.LocalItAdministration)]
    public Task<IActionResult> ConfirmNotAccepted(
        long emailRunId,
        long emailDeliveryId,
        int expectedAttemptCount,
        CancellationToken cancellationToken = default) =>
        ResolveUnknownAsync(
            emailRunId,
            emailDeliveryId,
            expectedAttemptCount,
            EmailUnknownResolutionAction.ConfirmNotAccepted,
            externalRequestId: null,
            cancellationToken);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy =
        RoleValidationAuthorizationPolicies.LocalItAdministration)]
    public Task<IActionResult> Cancel(
        long emailRunId,
        long emailDeliveryId,
        int expectedAttemptCount,
        CancellationToken cancellationToken = default) =>
        ResolveUnknownAsync(
            emailRunId,
            emailDeliveryId,
            expectedAttemptCount,
            EmailUnknownResolutionAction.Cancel,
            externalRequestId: null,
            cancellationToken);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy =
        RoleValidationAuthorizationPolicies.LocalItAdministration)]
    public async Task<IActionResult> Retry(
        long emailRunId,
        long emailDeliveryId,
        int expectedAttemptCount,
        CancellationToken cancellationToken = default)
    {
        if (!AreAvailable(
                typeof(IEmailManagementReader),
                typeof(ResolveUnknownDeliveryHandler)))
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

        if (!IsValidMutationRequest(
                emailRunId,
                emailDeliveryId,
                expectedAttemptCount))
        {
            return BadRequest();
        }

        IEmailManagementReader reader = Resolve<IEmailManagementReader>();
        EmailRunDetail? detail = await reader.GetEmailRunDetailAsync(
            emailRunId,
            cancellationToken);
        if (!ContainsDelivery(detail, emailRunId, emailDeliveryId))
        {
            return NotFound();
        }

        ResolveUnknownDeliveryHandler handler =
            Resolve<ResolveUnknownDeliveryHandler>();
        RetryEmailDeliveryResult result = await handler.RetryAsync(
            new RetryEmailDeliveryCommand(
                emailDeliveryId,
                expectedAttemptCount,
                actorEmployeeNo),
            cancellationToken);
        if (result.Outcome == RetryEmailDeliveryOutcome.NotFound)
        {
            return NotFound();
        }

        if (result.EmailRunId != emailRunId)
        {
            return NotFound();
        }

        string focusTarget = DeliveryFocus(emailDeliveryId);
        switch (result.Outcome)
        {
            case RetryEmailDeliveryOutcome.Applied:
                TempData["ManagementSuccess"] = "Delivery retry queued.";
                TempData["FocusTarget"] = focusTarget;
                return RedirectToRun(emailRunId);
            case RetryEmailDeliveryOutcome.AlreadyMatched:
                TempData["ManagementSuccess"] =
                    "Delivery retry was already queued.";
                TempData["FocusTarget"] = focusTarget;
                return RedirectToRun(emailRunId);
            case RetryEmailDeliveryOutcome.Rejected:
                TempData["ManagementError"] = result.ErrorCode!;
                TempData["FocusTarget"] = focusTarget;
                return RedirectToRun(emailRunId);
            case RetryEmailDeliveryOutcome.ActiveRunConflict:
                EmailRunDetail? reloaded =
                    await reader.GetEmailRunDetailAsync(
                        emailRunId,
                        cancellationToken);
                if (!IsExactRun(reloaded, emailRunId))
                {
                    return NotFound();
                }

                ViewResult conflict = View(
                    nameof(Index),
                    CreateModel(
                        reloaded!,
                        successMessage: null,
                        result.ErrorCode,
                        focusTarget));
                conflict.StatusCode = StatusCodes.Status409Conflict;
                return conflict;
            default:
                throw new InvalidOperationException(
                    "Unsupported retry outcome.");
        }
    }

    private async Task<IActionResult> ResolveUnknownAsync(
        long emailRunId,
        long emailDeliveryId,
        int expectedAttemptCount,
        EmailUnknownResolutionAction action,
        string? externalRequestId,
        CancellationToken cancellationToken)
    {
        if (!AreAvailable(
                typeof(IEmailManagementReader),
                typeof(ResolveUnknownDeliveryHandler)))
        {
            return NotFound();
        }

        string? actorEmployeeNo = GetActorEmployeeNo();
        if (actorEmployeeNo is null)
        {
            return Unauthorized();
        }

        if (!IsValidMutationRequest(
                emailRunId,
                emailDeliveryId,
                expectedAttemptCount))
        {
            return BadRequest();
        }

        string? normalizedExternalRequestId = null;
        if (action == EmailUnknownResolutionAction.ConfirmAccepted &&
            !TryNormalizeExternalRequestId(
                externalRequestId,
                out normalizedExternalRequestId))
        {
            return BadRequest();
        }

        IEmailManagementReader reader = Resolve<IEmailManagementReader>();
        EmailRunDetail? detail = await reader.GetEmailRunDetailAsync(
            emailRunId,
            cancellationToken);
        if (!ContainsDelivery(detail, emailRunId, emailDeliveryId))
        {
            return NotFound();
        }

        ResolveUnknownDeliveryHandler handler =
            Resolve<ResolveUnknownDeliveryHandler>();
        ResolveUnknownDeliveryResult result = await handler.ResolveAsync(
            new ResolveUnknownDeliveryCommand(
                emailDeliveryId,
                expectedAttemptCount,
                action,
                normalizedExternalRequestId,
                actorEmployeeNo),
            cancellationToken);
        if (result.Outcome == ResolveUnknownDeliveryOutcome.NotFound)
        {
            return NotFound();
        }

        if (result.EmailRunId != emailRunId)
        {
            return NotFound();
        }

        string focusTarget = DeliveryFocus(emailDeliveryId);
        switch (result.Outcome)
        {
            case ResolveUnknownDeliveryOutcome.Applied:
                TempData["ManagementSuccess"] =
                    "Delivery resolution saved.";
                break;
            case ResolveUnknownDeliveryOutcome.AlreadyMatched:
                TempData["ManagementSuccess"] =
                    "Delivery resolution already matched.";
                break;
            case ResolveUnknownDeliveryOutcome.Rejected:
                TempData["ManagementError"] = result.ErrorCode!;
                break;
            default:
                throw new InvalidOperationException(
                    "Unsupported resolution outcome.");
        }

        TempData["FocusTarget"] = focusTarget;
        return RedirectToRun(emailRunId);
    }

    private EmailRunManagementViewModel CreateModel(
        EmailRunDetail detail,
        string? successMessage,
        string? errorMessage,
        string? focusTarget)
    {
        EmailRunSummary summary = detail.Summary;
        return new EmailRunManagementViewModel
        {
            Run = new EmailRunSnapshotViewModel
            {
                EmailRunId = summary.EmailRunId,
                ApplicationId = summary.ApplicationId,
                DataSource = summary.Configuration.DataSource,
                TransportMode = summary.Configuration.TransportMode,
                RecipientPolicy = summary.Configuration.RecipientMode,
                TriggerIdentifier = TriggerIdentifier(summary.TriggerType),
                EmailScheduleId = summary.EmailScheduleId,
                ScheduledFor = summary.ScheduledFor,
                TriggeredByEmployeeNo = summary.TriggeredByEmployeeNo,
                StatusIdentifier = RunStatusIdentifier(summary.Status),
                StartedAt = summary.StartedAt,
                CompletedAt = summary.CompletedAt,
                CreatedAt = summary.CreatedAt,
                TotalCount = summary.TotalCount,
                InFlightCount = summary.InFlightCount,
                AcceptedCount = summary.AcceptedCount,
                SimulatedCount = summary.SimulatedCount,
                FailedCount = summary.FailedCount,
                UnknownCount = summary.UnknownCount,
                CancelledCount = summary.CancelledCount,
                ErrorCode = summary.ErrorCode
            },
            Deliveries = detail.Deliveries
                .Select(delivery => new EmailDeliveryManagementRowViewModel
                {
                    EmailDeliveryId = delivery.EmailDeliveryId,
                    IntendedOwnerEmployeeNo = delivery.OwnerEmployeeNo,
                    EffectiveEmployeeNo = delivery.EffectiveEmployeeNo,
                    StatusIdentifier = DeliveryStatusIdentifier(delivery.Status),
                    AttemptCount = delivery.AttemptCount,
                    NextRetryAt = delivery.NextRetryAt,
                    LastAttemptAt = delivery.LastAttemptAt,
                    SubmitStartedAt = delivery.SubmitStartedAt,
                    AcceptedAt = delivery.AcceptedAt,
                    ExternalRequestId = delivery.ExternalRequestId,
                    HttpStatus = delivery.HttpStatus,
                    ErrorCode = delivery.ErrorCode,
                    ResolutionActionIdentifier = ResolutionIdentifier(
                        delivery.ResolutionAction),
                    ResolvedByEmployeeNo = delivery.ResolvedByEmployeeNo,
                    ResolvedAt = delivery.ResolvedAt,
                    WorkbookAvailable = delivery.WorkbookFileName is not null,
                    CanResolveUnknown =
                        delivery.Status == EmailDeliveryStatus.Unknown,
                    IsRetryCandidate =
                        delivery.Status == EmailDeliveryStatus.Failed &&
                        delivery.ResolutionAction ==
                            EmailUnknownResolutionAction.ConfirmNotAccepted &&
                        summary.Configuration.TransportMode == "API_EMAIL"
                })
                .ToArray(),
            ProcessingAvailable = _processingCapability.IsEnabled,
            SuccessMessage = successMessage,
            ErrorMessage = errorMessage,
            FocusTarget = focusTarget
        };
    }

    private bool AreAvailable(params Type[] serviceTypes) =>
        serviceTypes.All(_serviceAvailability.IsService);

    private T Resolve<T>() where T : notnull =>
        _services.GetRequiredService<T>();

    private string? GetActorEmployeeNo()
    {
        string? value = User.FindFirstValue(
            RoleValidationAuthenticationDefaults.EmployeeNoClaimType);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool IsValidMutationRequest(
        long emailRunId,
        long emailDeliveryId,
        int expectedAttemptCount) =>
        emailRunId > 0 &&
        emailDeliveryId > 0 &&
        expectedAttemptCount is >= 1 and <= 999;

    private static bool TryNormalizeExternalRequestId(
        string? value,
        out string? normalized)
    {
        normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is not null &&
            Encoding.UTF8.GetByteCount(normalized) <= 100;
    }

    private static bool IsExactRun(EmailRunDetail? detail, long emailRunId) =>
        detail is not null && detail.Summary.EmailRunId == emailRunId;

    private static bool ContainsDelivery(
        EmailRunDetail? detail,
        long emailRunId,
        long emailDeliveryId) =>
        IsExactRun(detail, emailRunId) &&
        detail!.Deliveries.Any(delivery =>
            delivery.EmailDeliveryId == emailDeliveryId);

    private static string TriggerIdentifier(EmailRunTriggerType trigger) =>
        trigger switch
        {
            EmailRunTriggerType.Scheduled => "SCHEDULED",
            EmailRunTriggerType.RunNow => "RUN_NOW",
            _ => throw new ArgumentOutOfRangeException(nameof(trigger))
        };

    private static string RunStatusIdentifier(EmailRunStatus status) =>
        status switch
        {
            EmailRunStatus.Pending => "PENDING",
            EmailRunStatus.Processing => "PROCESSING",
            EmailRunStatus.ReviewRequired => "REVIEW_REQUIRED",
            EmailRunStatus.Completed => "COMPLETED",
            EmailRunStatus.Partial => "PARTIAL",
            EmailRunStatus.Failed => "FAILED",
            EmailRunStatus.Cancelled => "CANCELLED",
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };

    private static string DeliveryStatusIdentifier(
        EmailDeliveryStatus status) => status switch
        {
            EmailDeliveryStatus.Pending => "PENDING",
            EmailDeliveryStatus.Preparing => "PREPARING",
            EmailDeliveryStatus.RetryWait => "RETRY_WAIT",
            EmailDeliveryStatus.Submitting => "SUBMITTING",
            EmailDeliveryStatus.Accepted => "ACCEPTED",
            EmailDeliveryStatus.Simulated => "SIMULATED",
            EmailDeliveryStatus.Failed => "FAILED",
            EmailDeliveryStatus.Unknown => "UNKNOWN",
            EmailDeliveryStatus.Cancelled => "CANCELLED",
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };

    private static string? ResolutionIdentifier(
        EmailUnknownResolutionAction? resolution) => resolution switch
        {
            null => null,
            EmailUnknownResolutionAction.ConfirmAccepted =>
                "CONFIRM_ACCEPTED",
            EmailUnknownResolutionAction.ConfirmNotAccepted =>
                "CONFIRM_NOT_ACCEPTED",
            EmailUnknownResolutionAction.Cancel => "CANCEL",
            _ => throw new ArgumentOutOfRangeException(nameof(resolution))
        };

    private RedirectToActionResult RedirectToRun(long emailRunId) =>
        RedirectToAction(nameof(Index), new { id = emailRunId });

    private static string DeliveryFocus(long emailDeliveryId) =>
        $"delivery-{emailDeliveryId}-actions";

    private static ContentResult PlainText(string content, int statusCode) =>
        new()
        {
            Content = content,
            ContentType = "text/plain; charset=utf-8",
            StatusCode = statusCode
        };
}
