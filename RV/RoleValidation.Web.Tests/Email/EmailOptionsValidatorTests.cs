using Microsoft.Extensions.Options;
using RoleValidation.Web.Email;

namespace RoleValidation.Web.Tests.Email;

public sealed class EmailOptionsValidatorTests
{
    [Theory]
    [InlineData("Development", "Hybrid", "Fake", "SafeRedirect", true)]
    [InlineData("Development", "Hybrid", "ApiEmail", "SafeRedirect", false)]
    [InlineData("Development", "Oracle", "Fake", "SafeRedirect", true)]
    [InlineData("QA", "Oracle", "ApiEmail", "SafeRedirect", true)]
    [InlineData("Production", "Oracle", "ApiEmail", "RoleOwner", true)]
    [InlineData("Development", "Oracle", "ApiEmail", "RoleOwner", false)]
    [InlineData("QA", "Oracle", "ApiEmail", "RoleOwner", false)]
    [InlineData("Production", "Hybrid", "ApiEmail", "RoleOwner", false)]
    [InlineData("QA", "Hybrid", "Fake", "SafeRedirect", false)]
    [InlineData("QA", "Oracle", "Fake", "SafeRedirect", false)]
    [InlineData("Production", "Oracle", "Fake", "SafeRedirect", false)]
    [InlineData("Production", "Oracle", "ApiEmail", "SafeRedirect", false)]
    [InlineData("Development", "Oracle", "ApiEmail", "SafeRedirect", false)]
    public void Validate_Should_EnforceEnvironmentAndIndependentSafetyAxes(
        string environmentName,
        string dataSource,
        string transport,
        string recipient,
        bool valid)
    {
        EmailOptions options = CreateValidOptions(transport, recipient);
        var validator = new EmailOptionsValidator(
            environmentName,
            dataSource);

        ValidateOptionsResult result = validator.Validate(
            EmailOptions.SectionName,
            options);

        Assert.Equal(valid, result.Succeeded);
    }

    [Theory]
    [InlineData("development", "Hybrid", "Fake", "SafeRedirect")]
    [InlineData("Staging", "Hybrid", "Fake", "SafeRedirect")]
    [InlineData("Development", "hybrid", "Fake", "SafeRedirect")]
    [InlineData("Development", "Hybrid", "fake", "SafeRedirect")]
    [InlineData("Development", "Hybrid", "Smtp", "SafeRedirect")]
    [InlineData("Development", "Hybrid", "Fake", "safeRedirect")]
    [InlineData("Development", "Hybrid", "Fake", "Owner")]
    public void Validate_Should_FailClosedForUnapprovedExactValues(
        string environmentName,
        string dataSource,
        string transport,
        string recipient)
    {
        EmailOptions options = CreateValidOptions(transport, recipient);
        var validator = new EmailOptionsValidator(
            environmentName,
            dataSource);

        ValidateOptionsResult result = validator.Validate(
            EmailOptions.SectionName,
            options);

        Assert.True(result.Failed);
        Assert.NotEmpty(result.Failures);
    }

    [Fact]
    public void Validate_Should_AcceptConfiguredSafeRedirectEmployee()
    {
        EmailOptions options = CreateValidOptions("Fake", "SafeRedirect");
        options.SafeRedirectEmployeeNo = "C2001234";
        var validator = new EmailOptionsValidator(
            "Development",
            "Hybrid");

        ValidateOptionsResult result = validator.Validate(
            EmailOptions.SectionName,
            options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("C2001234 ")]
    [InlineData("C200 234")]
    [InlineData("C200-234")]
    [InlineData("C20012345")]
    public void Validate_Should_RejectInvalidSafeRedirectEmployee(
        string employeeNo)
    {
        EmailOptions options = CreateValidOptions("Fake", "SafeRedirect");
        options.SafeRedirectEmployeeNo = employeeNo;
        var validator = new EmailOptionsValidator(
            "Development",
            "Hybrid");

        ValidateOptionsResult result = validator.Validate(
            EmailOptions.SectionName,
            options);

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData(2, new[] { 5, 15 })]
    [InlineData(4, new[] { 5, 15 })]
    [InlineData(3, new[] { 5 })]
    [InlineData(3, new[] { 15, 5 })]
    [InlineData(3, new[] { 5, 15, 30 })]
    public void Validate_Should_RequireExactRetryPolicy(
        int maxAttempts,
        int[] delayMinutes)
    {
        EmailOptions options = CreateValidOptions("Fake", "SafeRedirect");
        options.PreSubmitRetry.MaxAttempts = maxAttempts;
        options.PreSubmitRetry.DelayMinutes = delayMinutes;
        var validator = new EmailOptionsValidator(
            "Development",
            "Hybrid");

        ValidateOptionsResult result = validator.Validate(
            EmailOptions.SectionName,
            options);

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_Should_RejectNonPositivePreparingStaleMinutes(
        int preparingStaleMinutes)
    {
        EmailOptions options = CreateValidOptions("Fake", "SafeRedirect");
        options.PreparingStaleMinutes = preparingStaleMinutes;
        var validator = new EmailOptionsValidator(
            "Development",
            "Hybrid");

        ValidateOptionsResult result = validator.Validate(
            EmailOptions.SectionName,
            options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains(
                "PreparingStaleMinutes",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("QA", "SafeRedirect", "http://api-email.example")]
    [InlineData("QA", "SafeRedirect", "http://localhost:5100")]
    [InlineData("Production", "RoleOwner", "http://api-email.example")]
    [InlineData("Production", "RoleOwner", "http://127.0.0.1:5100")]
    public void Validate_Should_RejectHttpApiEmailOutsideDevelopment(
        string environmentName,
        string recipientMode,
        string baseUrl)
    {
        EmailOptions options = CreateValidOptions("ApiEmail", recipientMode);
        options.ApiEmail.BaseUrl = baseUrl;
        var validator = new EmailOptionsValidator(
            environmentName,
            "Oracle");

        ValidateOptionsResult result = validator.Validate(
            EmailOptions.SectionName,
            options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("HTTPS", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Hybrid")]
    [InlineData("Oracle")]
    public void Validate_Should_RejectApiEmailInDevelopment(string dataSource)
    {
        EmailOptions options = CreateValidOptions(
            "ApiEmail",
            "SafeRedirect");
        var validator = new EmailOptionsValidator(
            "Development",
            dataSource);

        ValidateOptionsResult result = validator.Validate(
            EmailOptions.SectionName,
            options);

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void Validate_Should_KeepPreparingStaleWindowBeyondApiEmailTimeoutAndScanMargin(
        int preparingStaleMinutes,
        bool expectedValid)
    {
        EmailOptions options = CreateValidOptions(
            "ApiEmail",
            "SafeRedirect");
        options.ApiEmail.TimeoutSeconds = 60;
        options.PreparingStaleMinutes = preparingStaleMinutes;
        var validator = new EmailOptionsValidator(
            "QA",
            "Oracle");

        ValidateOptionsResult result = validator.Validate(
            EmailOptions.SectionName,
            options);

        Assert.Equal(expectedValid, result.Succeeded);
        if (!expectedValid)
        {
            Assert.Contains(
                result.Failures!,
                failure => failure.Contains(
                    "PreparingStaleMinutes",
                    StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Validate_Should_FailClosedWhenEmailSectionIsMissing()
    {
        var validator = new EmailOptionsValidator(
            "Development",
            "Hybrid");

        ValidateOptionsResult result = validator.Validate(
            EmailOptions.SectionName,
            new EmailOptions());

        Assert.True(result.Failed);
        Assert.NotEmpty(result.Failures);
    }

    [Fact]
    public void Validate_Should_FailClosedWhenRetryDelaysAreNull()
    {
        EmailOptions options = CreateValidOptions("Fake", "SafeRedirect");
        options.PreSubmitRetry.DelayMinutes = null!;
        var validator = new EmailOptionsValidator(
            "Development",
            "Hybrid");

        ValidateOptionsResult result = validator.Validate(
            EmailOptions.SectionName,
            options);

        Assert.True(result.Failed);
        Assert.NotEmpty(result.Failures);
    }

    [Theory]
    [InlineData("", "body")]
    [InlineData("subject", "")]
    [InlineData("{EmployeeEmail}", "body")]
    [InlineData("{ApplicationName", "body")]
    public void Validate_Should_RejectMissingOrUnsupportedEmailContent(
        string subjectTemplate,
        string bodyTemplate)
    {
        EmailOptions options = CreateValidOptions("Fake", "SafeRedirect");
        options.Content = new EmailContentOptions
        {
            SubjectTemplate = subjectTemplate,
            BodyTemplate = bodyTemplate
        };
        var validator = new EmailOptionsValidator("Development", "Hybrid");

        ValidateOptionsResult result = validator.Validate(
            EmailOptions.SectionName,
            options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("Email Content", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(InvalidOracleArtifactRoots))]
    public void Validate_Should_RejectInvalidOracleArtifactRootThroughOptionsValidation(
        string artifactRoot)
    {
        EmailOptions options = CreateValidOptions("ApiEmail", "RoleOwner");
        options.ArtifactRootPath = artifactRoot;
        var validator = new EmailOptionsValidator("Production", "Oracle");

        ValidateOptionsResult result = validator.Validate(
            EmailOptions.SectionName,
            options);

        Assert.True(result.Failed);
        Assert.NotEmpty(result.Failures);
    }

    [Fact]
    public void Validate_Should_AllowCanonicalOracleArtifactRootWithTrailingSeparator()
    {
        EmailOptions options = CreateValidOptions("ApiEmail", "RoleOwner");
        options.ArtifactRootPath = CanonicalArtifactRoot() +
            Path.DirectorySeparatorChar;
        var validator = new EmailOptionsValidator("Production", "Oracle");

        ValidateOptionsResult result = validator.Validate(
            EmailOptions.SectionName,
            options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_Should_IgnoreArtifactRootOptionForHybrid()
    {
        EmailOptions options = CreateValidOptions("Fake", "SafeRedirect");
        options.ArtifactRootPath = "not a local artifact root";
        var validator = new EmailOptionsValidator("Development", "Hybrid");

        ValidateOptionsResult result = validator.Validate(
            EmailOptions.SectionName,
            options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_Should_AllowDevelopmentOracleFakeWithoutArtifactRoot()
    {
        EmailOptions options = CreateValidOptions("Fake", "SafeRedirect");
        options.ArtifactRootPath = string.Empty;
        var validator = new EmailOptionsValidator("Development", "Oracle");

        ValidateOptionsResult result = validator.Validate(
            EmailOptions.SectionName,
            options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_Should_RejectOracleArtifactRootWithSurroundingWhitespace()
    {
        EmailOptions options = CreateValidOptions("ApiEmail", "RoleOwner");
        options.ArtifactRootPath = " " + CanonicalArtifactRoot() + " ";
        var validator = new EmailOptionsValidator("Production", "Oracle");

        ValidateOptionsResult result = validator.Validate(
            EmailOptions.SectionName,
            options);

        Assert.True(result.Failed);
    }

    [Fact]
    public void Validate_Should_RejectOracleRootThatExceedsOwnerWorkbookStoragePathLimit()
    {
        string root = CanonicalArtifactRootOfLength(885);
        EmailOptions options = CreateValidOptions("ApiEmail", "RoleOwner");
        options.ArtifactRootPath = root;
        var validator = new EmailOptionsValidator("Production", "Oracle");

        ValidateOptionsResult result = validator.Validate(
            EmailOptions.SectionName,
            options);

        Assert.Equal(1001, root.Length + 116);
        Assert.True(result.Failed);
        string failure = Assert.Single(result.Failures);
        Assert.Contains("884", failure, StringComparison.Ordinal);
        Assert.Contains("canonical", failure, StringComparison.Ordinal);
        Assert.Contains("printable ASCII", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_Should_AcceptMaximumOracleRootThatReservesOwnerWorkbookSuffix()
    {
        string root = CanonicalArtifactRootOfLength(884);
        EmailOptions options = CreateValidOptions("ApiEmail", "RoleOwner");
        options.ArtifactRootPath = root;
        var validator = new EmailOptionsValidator("Production", "Oracle");

        ValidateOptionsResult result = validator.Validate(
            EmailOptions.SectionName,
            options);

        Assert.Equal(1000, root.Length + 116);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_Should_NotCreateUniqueOracleArtifactRoot()
    {
        string root = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "RoleValidationEmailArtifacts",
            Guid.NewGuid().ToString("N")));
        EmailOptions options = CreateValidOptions("ApiEmail", "RoleOwner");
        options.ArtifactRootPath = root;
        var validator = new EmailOptionsValidator("Production", "Oracle");

        Assert.False(Directory.Exists(root));
        ValidateOptionsResult result = validator.Validate(
            EmailOptions.SectionName,
            options);

        Assert.True(result.Succeeded);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void Validate_Should_AcceptCanonicalUncArtifactRootOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        EmailOptions options = CreateValidOptions("ApiEmail", "RoleOwner");
        options.ArtifactRootPath = Path.GetFullPath(
            "\\\\example.invalid\\rolevalidation-artifacts\\");
        var validator = new EmailOptionsValidator("Production", "Oracle");

        ValidateOptionsResult result = validator.Validate(
            EmailOptions.SectionName,
            options);

        Assert.True(result.Succeeded);
    }

    public static IEnumerable<object[]> InvalidOracleArtifactRoots()
    {
        yield return [string.Empty];
        yield return ["relative-artifacts"];
        yield return [Path.Combine(CanonicalArtifactRoot(), "..", "other")];
        yield return [Path.Combine(Path.GetTempPath(), "email\u00a0artifacts")];
        yield return [CanonicalArtifactRoot() + "\0"];
        yield return [CanonicalArtifactRoot() + "\t"];
        yield return [CanonicalArtifactRoot() + "\n"];
        yield return [CanonicalArtifactRoot() + "\x7f"];
        yield return [Path.Combine(CanonicalArtifactRoot(), ".", "artifacts")];
        yield return [CanonicalArtifactRoot() +
            Path.DirectorySeparatorChar +
            Path.DirectorySeparatorChar +
            "artifacts"];
    }

    private static EmailOptions CreateValidOptions(
        string transport,
        string recipient)
    {
        return new EmailOptions
        {
            TransportMode = transport,
            RecipientMode = recipient,
            SafeRedirectEmployeeNo = "C2001234",
            ArtifactRootPath = CanonicalArtifactRoot(),
            Content = ValidContent(),
            PreSubmitRetry = new PreSubmitRetryOptions
            {
                MaxAttempts = 3,
                DelayMinutes = [5, 15]
            },
            ApiEmail = new ApiEmailClientOptions
            {
                BaseUrl = "https://api-email.example",
                Route = "/API/v2/EmailCenterRequest",
                ApplicationName = "RoleValidation",
                BearerToken = "issued-machine-token",
                TimeoutSeconds = 30
            }
        };
    }

    private static EmailContentOptions ValidContent() => new()
    {
        SubjectTemplate =
            "[RoleValidation] Annual access review - {ApplicationName}",
        BodyTemplate =
            "Please review the attached workbook for {ApplicationName}.\n" +
            "Intended owner: {OwnerEmployeeNo}"
    };

    private static string CanonicalArtifactRoot() => Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "RoleValidationEmailArtifacts"));

    private static string CanonicalArtifactRootOfLength(int length)
    {
        string prefix = Path.GetPathRoot(Path.GetTempPath())!;
        var root = new System.Text.StringBuilder(prefix);
        while (root.Length < length)
        {
            int separatorLength = root.Length == prefix.Length ? 0 : 1;
            int segmentLength = Math.Min(
                200,
                length - root.Length - separatorLength);
            if (separatorLength == 1)
            {
                root.Append(Path.DirectorySeparatorChar);
            }

            root.Append('a', segmentLength);
        }

        string result = root.ToString();
        Assert.Equal(length, result.Length);
        Assert.Equal(result, Path.GetFullPath(result));
        return result;
    }
}
