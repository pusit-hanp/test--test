using System.Text;
using RoleValidation.Application.Applications;
using RoleValidation.Application.Email;

namespace RoleValidation.Web.Email;

public sealed class ConfiguredEmailContentSource : IEmailContentSource
{
    private const string ApplicationNameToken = "{ApplicationName}";
    private const string OwnerEmployeeNoToken = "{OwnerEmployeeNo}";
    private readonly IApplicationReader _applicationReader;
    private readonly EmailContentOptions _content;

    public ConfiguredEmailContentSource(
        IApplicationReader applicationReader,
        EmailOptions options)
    {
        _applicationReader = applicationReader ??
            throw new ArgumentNullException(nameof(applicationReader));
        ArgumentNullException.ThrowIfNull(options);
        _content = options.Content ??
            throw new ArgumentException(
                "Email Content configuration is required.",
                nameof(options));
    }

    public async Task<EmailContentResult> GetAsync(
        EmailDeliveryWorkItem workItem,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(workItem);

        ApplicationSummary? application =
            await _applicationReader.FindByIdAsync(
                workItem.ApplicationId,
                cancellationToken);
        if (application is null)
        {
            return EmailContentResult.NotConfigured();
        }

        return EmailContentResult.Configured(
            Render(_content.SubjectTemplate, application, workItem),
            Render(_content.BodyTemplate, application, workItem));
    }

    private static string Render(
        string template,
        ApplicationSummary application,
        EmailDeliveryWorkItem workItem)
    {
        var result = new StringBuilder(template.Length);
        ReadOnlySpan<char> remaining = template.AsSpan();

        while (!remaining.IsEmpty)
        {
            if (remaining.StartsWith(
                    ApplicationNameToken,
                    StringComparison.Ordinal))
            {
                result.Append(application.ApplicationName);
                remaining = remaining[ApplicationNameToken.Length..];
                continue;
            }

            if (remaining.StartsWith(
                    OwnerEmployeeNoToken,
                    StringComparison.Ordinal))
            {
                result.Append(workItem.OwnerEmployeeNo);
                remaining = remaining[OwnerEmployeeNoToken.Length..];
                continue;
            }

            result.Append(remaining[0]);
            remaining = remaining[1..];
        }

        return result.ToString();
    }
}
