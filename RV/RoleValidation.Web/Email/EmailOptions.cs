namespace RoleValidation.Web.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string TransportMode { get; set; } = string.Empty;

    public string RecipientMode { get; set; } = string.Empty;

    public string SafeRedirectEmployeeNo { get; set; } = string.Empty;

    public string ArtifactRootPath { get; set; } = string.Empty;

    public int PreparingStaleMinutes { get; set; } = 30;

    public EmailContentOptions Content { get; set; } = new();

    public PreSubmitRetryOptions PreSubmitRetry { get; set; } = new();

    public ApiEmailClientOptions ApiEmail { get; set; } = new();
}

public sealed class EmailContentOptions
{
    public string SubjectTemplate { get; set; } = string.Empty;

    public string BodyTemplate { get; set; } = string.Empty;
}

public sealed class PreSubmitRetryOptions
{
    public int MaxAttempts { get; set; }

    public int[] DelayMinutes { get; set; } = [];
}

public sealed class ApiEmailClientOptions
{
    public string BaseUrl { get; set; } = string.Empty;

    public string Route { get; set; } = "/API/v2/EmailCenterRequest";

    public string ApplicationName { get; set; } = "RoleValidation";

    public string BearerToken { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 30;
}
