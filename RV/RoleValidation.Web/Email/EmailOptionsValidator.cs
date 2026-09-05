using Microsoft.Extensions.Options;
using RoleValidation.Infrastructure.Email;
using System.Text;

namespace RoleValidation.Web.Email;

public sealed class EmailOptionsValidator : IValidateOptions<EmailOptions>
{
    private const int MaximumSubjectBytes = 250;
    private const int PreparingSafetyMarginSeconds = 60;
    private const string DevelopmentEnvironment = "Development";
    private const string QaEnvironment = "QA";
    private const string ProductionEnvironment = "Production";
    private const string HybridDataSource = "Hybrid";
    private const string OracleDataSource = "Oracle";
    private const string FakeTransport = "Fake";
    private const string ApiEmailTransport = "ApiEmail";
    private const string SafeRedirectRecipient = "SafeRedirect";
    private const string RoleOwnerRecipient = "RoleOwner";
    private static readonly HashSet<string> AllowedContentTokens =
        ["ApplicationName", "OwnerEmployeeNo"];

    private readonly string _environmentName;
    private readonly string? _dataSource;

    public EmailOptionsValidator(
        string environmentName,
        string? dataSource)
    {
        _environmentName = environmentName;
        _dataSource = dataSource;
    }

    public ValidateOptionsResult Validate(
        string? name,
        EmailOptions options) => Validate(options, out _);

    internal ValidateOptionsResult Validate(
        EmailOptions options,
        out IReadOnlyList<string> configurationKeys)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        var keys = new List<string>();
        void Reject(string key, string failure)
        {
            keys.Add(key);
            failures.Add(failure);
        }

        if (!IsApprovedEnvironment(_environmentName))
        {
            Reject("ASPNETCORE_ENVIRONMENT",
                "Environment must be Development, QA, or Production.");
        }

        if (_dataSource is not HybridDataSource and not OracleDataSource)
        {
            Reject("RoleValidation:DataSource", "RoleValidation:DataSource must be Hybrid or Oracle.");
        }

        if (options.TransportMode is
            not FakeTransport and
            not ApiEmailTransport)
        {
            Reject("Email:TransportMode", "TransportMode must be Fake or ApiEmail.");
        }

        if (options.RecipientMode is
            not SafeRedirectRecipient and
            not RoleOwnerRecipient)
        {
            Reject("Email:RecipientMode", "RecipientMode must be SafeRedirect or RoleOwner.");
        }

        if (options.RecipientMode == SafeRedirectRecipient &&
            !IsValidEmployeeNo(options.SafeRedirectEmployeeNo))
        {
            Reject("Email:SafeRedirectEmployeeNo",
                "SafeRedirectEmployeeNo must be a valid employee number in SafeRedirect mode.");
        }

        if (options.RecipientMode == RoleOwnerRecipient &&
            (_dataSource != OracleDataSource ||
                _environmentName != ProductionEnvironment))
        {
            Reject("Email:RecipientMode",
                "RoleOwner recipient mode requires Oracle in Production.");
        }

        if (_dataSource == OracleDataSource &&
            _environmentName != DevelopmentEnvironment &&
            !IsValidOracleArtifactRoot(options.ArtifactRootPath))
        {
            Reject("Email:ArtifactRootPath",
                "ArtifactRootPath must be an absolute canonical printable ASCII path of at most 884 characters for Oracle.");
        }

        if (!IsApprovedEnvironmentCombination(options))
        {
            Reject("Email:TransportMode / Email:RecipientMode",
                "Email configuration does not match the approved " +
                "environment matrix.");
        }

        if (options.PreSubmitRetry is null ||
            options.PreSubmitRetry.MaxAttempts != 3 ||
            options.PreSubmitRetry.DelayMinutes is null ||
            !options.PreSubmitRetry.DelayMinutes.SequenceEqual([5, 15]))
        {
            Reject("Email:PreSubmitRetry",
                "PreSubmitRetry must use MaxAttempts 3 and DelayMinutes [5,15].");
        }

        if (options.PreparingStaleMinutes <= 0)
        {
            Reject("Email:PreparingStaleMinutes", "PreparingStaleMinutes must be greater than zero.");
        }

        if (options.TransportMode == ApiEmailTransport &&
            options.ApiEmail is { TimeoutSeconds: >= 1 and <= 120 } &&
            (long)options.PreparingStaleMinutes * 60 <=
                options.ApiEmail.TimeoutSeconds + PreparingSafetyMarginSeconds)
        {
            Reject("Email:PreparingStaleMinutes",
                "PreparingStaleMinutes must exceed ApiEmail TimeoutSeconds " +
                "by more than the one-minute worker safety margin.");
        }

        if (options.TransportMode == ApiEmailTransport &&
            !TryCreateApiEmailSettings(
                options,
                _environmentName,
                out _,
                out string? failure,
                out string configurationKey))
        {
            Reject(configurationKey, failure!);
        }

        if (!TryValidateContent(options.Content, out string? contentFailure))
        {
            Reject("Email:Content", contentFailure!);
            if (string.IsNullOrWhiteSpace(options.Content?.SubjectTemplate))
            {
                keys.Add("Email:Content:SubjectTemplate");
            }
            if (string.IsNullOrWhiteSpace(options.Content?.BodyTemplate))
            {
                keys.Add("Email:Content:BodyTemplate");
            }
        }

        configurationKeys = keys.Distinct(StringComparer.Ordinal).ToArray();
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    internal static bool TryValidateContent(
        EmailContentOptions? content,
        out string? failure)
    {
        failure = null;
        if (content is null ||
            string.IsNullOrWhiteSpace(content.SubjectTemplate) ||
            string.IsNullOrWhiteSpace(content.BodyTemplate))
        {
            failure = "Email Content subject and body templates are required.";
            return false;
        }

        if (Encoding.UTF8.GetByteCount(content.SubjectTemplate) >
            MaximumSubjectBytes)
        {
            failure = "Email Content subject template exceeds 250 UTF-8 bytes.";
            return false;
        }

        return TryValidateTemplate(
                content.SubjectTemplate,
                out failure) &&
            TryValidateTemplate(content.BodyTemplate, out failure);
    }

    private static bool TryValidateTemplate(
        string template,
        out string? failure)
    {
        failure = null;
        for (int index = 0; index < template.Length; index++)
        {
            if (template[index] == '}')
            {
                failure = "Email Content contains malformed placeholders.";
                return false;
            }

            if (template[index] != '{')
            {
                continue;
            }

            int close = template.IndexOf('}', index + 1);
            if (close < 0 || close == index + 1)
            {
                failure = "Email Content contains malformed placeholders.";
                return false;
            }

            string token = template[(index + 1)..close];
            if (token.Contains('{') ||
                !AllowedContentTokens.Contains(token))
            {
                failure =
                    $"Email Content placeholder '{{{token}}}' is not supported.";
                return false;
            }

            index = close;
        }

        return true;
    }

    internal static bool TryCreateApiEmailSettings(
        EmailOptions options,
        string environmentName,
        out ApiEmailTransportSettings? settings,
        out string? failure) =>
        TryCreateApiEmailSettings(options, environmentName, out settings, out failure, out _);

    private static bool TryCreateApiEmailSettings(
        EmailOptions options,
        string environmentName,
        out ApiEmailTransportSettings? settings,
        out string? failure,
        out string configurationKey)
    {
        settings = null;
        failure = null;
        configurationKey = "Email:ApiEmail";
        ApiEmailClientOptions? client = options.ApiEmail;
        if (client is null)
        {
            failure = "ApiEmail client configuration is required.";
            return false;
        }

        if (client.Route != "/API/v2/EmailCenterRequest")
        {
            configurationKey = "Email:ApiEmail:Route";
            failure = "ApiEmail route must be /API/v2/EmailCenterRequest.";
            return false;
        }

        if (client.ApplicationName != "RoleValidation")
        {
            configurationKey = "Email:ApiEmail:ApplicationName";
            failure = "ApiEmail ApplicationName must be RoleValidation.";
            return false;
        }

        if (client.TimeoutSeconds is < 1 or > 120)
        {
            configurationKey = "Email:ApiEmail:TimeoutSeconds";
            failure = "ApiEmail TimeoutSeconds must be between 1 and 120.";
            return false;
        }

        if (!Uri.TryCreate(client.BaseUrl, UriKind.Absolute, out Uri? baseUri) ||
            baseUri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(baseUri.UserInfo) ||
            !string.IsNullOrEmpty(baseUri.Query) ||
            !string.IsNullOrEmpty(baseUri.Fragment))
        {
            configurationKey = "Email:ApiEmail:BaseUrl";
            failure = "ApiEmail BaseUrl must be an absolute HTTP or HTTPS URL without credentials, query or fragment.";
            return false;
        }

        bool isDevelopmentLoopbackHttp =
            environmentName == DevelopmentEnvironment &&
            baseUri.Scheme == Uri.UriSchemeHttp &&
            baseUri.IsLoopback;
        if (baseUri.Scheme != Uri.UriSchemeHttps &&
            !isDevelopmentLoopbackHttp)
        {
            configurationKey = "Email:ApiEmail:BaseUrl";
            failure =
                "ApiEmail BaseUrl must use HTTPS. Development may use HTTP only for a loopback URL.";
            return false;
        }

        try
        {
            string endpoint =
                baseUri.AbsoluteUri.TrimEnd('/') + client.Route;
            settings = new ApiEmailTransportSettings(
                endpoint,
                client.BearerToken,
                client.ApplicationName);
            return true;
        }
        catch (ArgumentException exception)
        {
            configurationKey = exception.ParamName == "bearerToken"
                ? "Email:ApiEmail:BearerToken"
                : "Email:ApiEmail";
            failure = exception.ParamName == "bearerToken"
                ? "An issued ApiEmail bearer token is required. Do not configure the JWT signing key."
                : $"ApiEmail client configuration is invalid: {exception.Message}";
            return false;
        }
    }

    private static bool IsApprovedEnvironment(string environmentName)
    {
        return environmentName is
            DevelopmentEnvironment or
            QaEnvironment or
            ProductionEnvironment;
    }

    private static bool IsValidEmployeeNo(string? value) =>
        value is not null &&
        value.Length is >= 1 and <= 8 &&
        value.All(character =>
            character is >= 'A' and <= 'Z' or
                >= 'a' and <= 'z' or
                >= '0' and <= '9' or '_');

    private bool IsApprovedEnvironmentCombination(EmailOptions options)
    {
        return _environmentName switch
        {
            DevelopmentEnvironment =>
                (_dataSource == HybridDataSource ||
                    _dataSource == OracleDataSource) &&
                options.TransportMode == FakeTransport &&
                options.RecipientMode == SafeRedirectRecipient,
            QaEnvironment =>
                _dataSource == OracleDataSource &&
                options.TransportMode == ApiEmailTransport &&
                options.RecipientMode == SafeRedirectRecipient,
            ProductionEnvironment =>
                _dataSource == OracleDataSource &&
                options.TransportMode == ApiEmailTransport &&
                options.RecipientMode == RoleOwnerRecipient,
            _ => false
        };
    }

    private static bool IsValidOracleArtifactRoot(string? value)
    {
        try
        {
            // The constructor validates syntax only and performs no file I/O.
            _ = new FileSystemEmailArtifactStore(value!);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (PathTooLongException)
        {
            return false;
        }
    }
}
