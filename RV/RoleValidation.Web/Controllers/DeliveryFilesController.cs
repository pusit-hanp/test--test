using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using RoleValidation.Application.Email;
using RoleValidation.Core.Features.Email;
using RoleValidation.Web.Authentication;
using RoleValidation.Web.Email;
using RoleValidation.Web.Models.Email;

namespace RoleValidation.Web.Controllers;

[Authorize(Policy = RoleValidationAuthorizationPolicies.AdminRead)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[TypeFilter(typeof(EmailConfigurationGuard))]
public sealed class DeliveryFilesController : Controller
{
    private const int RecentRunLimit = 20;

    private const string WorkbookContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly IServiceProvider _services;
    private readonly IServiceProviderIsService _serviceAvailability;

    public DeliveryFilesController(
        IServiceProvider services,
        IServiceProviderIsService serviceAvailability)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _serviceAvailability = serviceAvailability ??
            throw new ArgumentNullException(nameof(serviceAvailability));
    }

    [HttpGet]
    [Authorize(Policy = RoleValidationAuthorizationPolicies.AdminRead)]
    public async Task<IActionResult> Index(
        long? id,
        CancellationToken cancellationToken = default,
        int? applicationId = null)
    {
        if (!ModelState.IsValid)
        {
            return NotFound();
        }

        if (id is null)
        {
            if (!AreAvailable(typeof(IEmailManagementReader)))
            {
                return View(new DeliveryFileManagementViewModel());
            }

            return await CreateRecentRunsViewAsync(
                applicationId,
                cancellationToken);
        }

        if (id <= 0 || !AreAvailable(typeof(IEmailManagementReader)))
        {
            return NotFound();
        }

        IEmailManagementReader reader = Resolve<IEmailManagementReader>();
        EmailRunDetail? detail = await reader.GetEmailRunDetailAsync(
            id.Value,
            cancellationToken);
        if (!IsExactRun(detail, id.Value))
        {
            return NotFound();
        }

        return View(CreateModel(detail!));
    }

    private async Task<IActionResult> CreateRecentRunsViewAsync(
        int? applicationId,
        CancellationToken cancellationToken)
    {
        if (applicationId <= 0)
        {
            return NotFound();
        }

        IEmailManagementReader reader = Resolve<IEmailManagementReader>();
        IReadOnlyList<EmailApplicationOverview> overviews =
            await reader.GetEmailApplicationOverviewsAsync(cancellationToken);
        EmailApplicationOverview? selected = applicationId.HasValue
            ? overviews.SingleOrDefault(application =>
                application.ApplicationId == applicationId.Value)
            : overviews.FirstOrDefault();
        if (applicationId.HasValue && selected is null)
        {
            return NotFound();
        }

        IReadOnlyList<EmailRunSummary> runs = selected is null
            ? []
            : await reader.GetRecentEmailRunsAsync(
                selected.ApplicationId,
                RecentRunLimit,
                cancellationToken);
        return View(new DeliveryFileManagementViewModel
        {
            Applications = overviews
                .Select(application =>
                    new DeliveryFileApplicationOptionViewModel
                    {
                        ApplicationId = application.ApplicationId,
                        ApplicationCode = application.ApplicationCode,
                        ApplicationName = application.ApplicationName
                    })
                .ToArray(),
            SelectedApplicationId = selected?.ApplicationId,
            RecentRuns = selected is null
                ? []
                : runs
                    .Where(run =>
                        run.ApplicationId == selected.ApplicationId)
                    .Select(run => new DeliveryFileRunRowViewModel
                    {
                        EmailRunId = run.EmailRunId,
                        TriggerIdentifier = TriggerIdentifier(run.TriggerType),
                        StatusIdentifier = RunStatusIdentifier(run.Status),
                        CreatedAt = run.CreatedAt,
                        TotalCount = run.TotalCount,
                        HasZipRecord = run.ZipFileName is not null
                    })
                    .ToArray()
        });
    }

    [HttpGet]
    [Authorize(Policy = RoleValidationAuthorizationPolicies.AdminRead)]
    public async Task<IActionResult> DownloadZip(
        long id,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0 || !AreAvailable(
                typeof(IEmailManagementReader),
                typeof(IEmailArtifactStore)))
        {
            return NotFound();
        }

        IEmailManagementReader reader = Resolve<IEmailManagementReader>();
        EmailRunDetail? detail = await reader.GetEmailRunDetailAsync(
            id,
            cancellationToken);
        if (!IsExactRun(detail, id))
        {
            return NotFound();
        }

        EmailArtifactMetadata? artifact;
        try
        {
            artifact = await reader.GetEmailRunZipArtifactAsync(
                id,
                cancellationToken);
        }
        catch (InvalidDataException)
        {
            return NotFound();
        }

        if (artifact is null)
        {
            return NotFound();
        }

        IEmailArtifactStore store = Resolve<IEmailArtifactStore>();
        byte[]? content;
        try
        {
            content = await store.ReadRunZipAsync(
                id,
                artifact,
                cancellationToken);
        }
        catch (Exception exception) when (IsUnreadableArtifact(exception))
        {
            return NotFound();
        }

        if (content is null || content.Length == 0)
        {
            return NotFound();
        }

        return File(
            content,
            "application/zip",
            $"role-validation-run-{id}.zip");
    }

    [HttpGet]
    [Authorize(Policy = RoleValidationAuthorizationPolicies.AdminRead)]
    public async Task<IActionResult> DownloadWorkbook(
        long id,
        long emailDeliveryId,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0 || emailDeliveryId <= 0 || !AreAvailable(
                typeof(IEmailManagementReader),
                typeof(IEmailArtifactStore)))
        {
            return NotFound();
        }

        IEmailManagementReader reader = Resolve<IEmailManagementReader>();
        EmailRunDetail? detail = await reader.GetEmailRunDetailAsync(
            id,
            cancellationToken);
        if (!ContainsDelivery(detail, id, emailDeliveryId))
        {
            return NotFound();
        }

        EmailArtifactMetadata? artifact;
        try
        {
            artifact = await reader.GetEmailDeliveryWorkbookArtifactAsync(
                id,
                emailDeliveryId,
                cancellationToken);
        }
        catch (InvalidDataException)
        {
            return NotFound();
        }

        if (artifact is null)
        {
            return NotFound();
        }

        IEmailArtifactStore store = Resolve<IEmailArtifactStore>();
        byte[]? content;
        try
        {
            content = await store.ReadOwnerWorkbookAsync(
                id,
                emailDeliveryId,
                artifact,
                cancellationToken);
        }
        catch (Exception exception) when (IsUnreadableArtifact(exception))
        {
            return NotFound();
        }

        if (content is null || content.Length == 0)
        {
            return NotFound();
        }

        return File(
            content,
            WorkbookContentType,
            $"role-validation-run-{id}-delivery-{emailDeliveryId}.xlsx");
    }

    private DeliveryFileManagementViewModel CreateModel(EmailRunDetail detail)
    {
        EmailRunSummary summary = detail.Summary;
        return new DeliveryFileManagementViewModel
        {
            Run = new DeliveryFileRunSnapshotViewModel
            {
                EmailRunId = summary.EmailRunId,
                DataSource = summary.Configuration.DataSource,
                TransportMode = summary.Configuration.TransportMode,
                RecipientPolicy = summary.Configuration.RecipientMode,
                StatusIdentifier = RunStatusIdentifier(summary.Status)
            },
            HasZipRecord = summary.ZipFileName is not null,
            Deliveries = detail.Deliveries
                .Select(delivery => new EmailDeliveryFileRowViewModel
                {
                    EmailDeliveryId = delivery.EmailDeliveryId,
                    IntendedOwnerEmployeeNo = delivery.OwnerEmployeeNo,
                    EffectiveEmployeeNo = delivery.EffectiveEmployeeNo,
                    StatusIdentifier = DeliveryStatusIdentifier(delivery.Status),
                    AttemptCount = delivery.AttemptCount,
                    HasWorkbookRecord = delivery.WorkbookFileName is not null
                })
                .ToArray()
        };
    }

    private bool AreAvailable(params Type[] serviceTypes) =>
        serviceTypes.All(_serviceAvailability.IsService);

    private T Resolve<T>() where T : notnull =>
        _services.GetRequiredService<T>();

    private static bool IsExactRun(EmailRunDetail? detail, long emailRunId) =>
        detail is not null && detail.Summary.EmailRunId == emailRunId;

    private static bool ContainsDelivery(
        EmailRunDetail? detail,
        long emailRunId,
        long emailDeliveryId) =>
        IsExactRun(detail, emailRunId) &&
        detail!.Deliveries.Any(delivery =>
            delivery.EmailDeliveryId == emailDeliveryId);

    private static bool IsUnreadableArtifact(Exception exception) =>
        exception is InvalidDataException or
            IOException or
            UnauthorizedAccessException;

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

    private static string TriggerIdentifier(EmailRunTriggerType trigger) =>
        trigger switch
        {
            EmailRunTriggerType.Scheduled => "SCHEDULED",
            EmailRunTriggerType.RunNow => "RUN_NOW",
            _ => throw new ArgumentOutOfRangeException(nameof(trigger))
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
}
