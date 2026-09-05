using Moq;
using RoleValidation.Application.Applications;
using RoleValidation.Application.Email;
using RoleValidation.Web.Email;

namespace RoleValidation.Web.Tests.Email;

public sealed class ConfiguredEmailContentSourceTests
{
    [Fact]
    public async Task GetAsync_Should_RenderApprovedApplicationAndOwnerTokens()
    {
        var applications = new Mock<IApplicationReader>(MockBehavior.Strict);
        applications.Setup(item => item.FindByIdAsync(
                17,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationSummary(17, "ERSN", "eRSN"));
        var source = new ConfiguredEmailContentSource(
            applications.Object,
            Options());

        EmailContentResult result = await source.GetAsync(WorkItem());

        Assert.True(result.IsConfigured);
        Assert.Equal(
            "[RoleValidation] Annual access review - eRSN",
            result.Subject);
        Assert.Equal(
            "Please review the attached workbook for eRSN.\n" +
            "Intended owner: C1000001",
            result.Body);
    }

    [Fact]
    public async Task GetAsync_Should_NotInterpretTokensIntroducedByApplicationData()
    {
        var applications = new Mock<IApplicationReader>(MockBehavior.Strict);
        applications.Setup(item => item.FindByIdAsync(
                17,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationSummary(
                17,
                "ERSN",
                "eRSN {OwnerEmployeeNo}"));
        var source = new ConfiguredEmailContentSource(
            applications.Object,
            Options());

        EmailContentResult result = await source.GetAsync(WorkItem());

        Assert.Equal(
            "Please review the attached workbook for " +
            "eRSN {OwnerEmployeeNo}.\n" +
            "Intended owner: C1000001",
            result.Body);
    }

    [Fact]
    public async Task GetAsync_Should_FailClosedWhenApplicationIsMissing()
    {
        var applications = new Mock<IApplicationReader>(MockBehavior.Strict);
        applications.Setup(item => item.FindByIdAsync(
                17,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationSummary?)null);
        var source = new ConfiguredEmailContentSource(
            applications.Object,
            Options());

        EmailContentResult result = await source.GetAsync(WorkItem());

        Assert.False(result.IsConfigured);
        Assert.Equal("EMAIL_CONTENT_NOT_CONFIGURED", result.ErrorCode);
    }

    private static EmailOptions Options() => new()
    {
        Content = new EmailContentOptions
        {
            SubjectTemplate =
                "[RoleValidation] Annual access review - {ApplicationName}",
            BodyTemplate =
                "Please review the attached workbook for {ApplicationName}.\n" +
                "Intended owner: {OwnerEmployeeNo}"
        }
    };

    private static EmailDeliveryWorkItem WorkItem() => new(
        emailDeliveryId: 71,
        emailRunId: 901,
        applicationId: 17,
        ownerEmployeeNo: "C1000001",
        effectiveEmployeeNo: "C1008267",
        dataSource: "ORACLE",
        transportMode: "API_EMAIL",
        recipientMode: "SAFE_REDIRECT",
        attemptCount: 1,
        lastAttemptAt: new DateTimeOffset(
            2026, 9, 2, 1, 2, 3, TimeSpan.Zero),
        workbookExportedBy: "SYSTEM");
}
