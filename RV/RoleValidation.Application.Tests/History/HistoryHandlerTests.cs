using Moq;
using RoleValidation.Application.History;

namespace RoleValidation.Application.Tests.History;

public sealed class HistoryHandlerTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData(" ALL ", null)]
    [InlineData(" success ", "SUCCESS")]
    [InlineData(" DENIED ", "DENIED")]
    public async Task LoginSearchWithoutIdentifiers_ShouldLoadRecentEvents(
        string? filter,
        string? expectedResult)
    {
        var reader = new RecordingHistoryReader();
        var handler = new LoadLoginHistoryHandler(reader);

        LoginHistoryResult result = await handler.HandleAsync(
            new LoginHistoryQuery(
                EmployeeNo: "  ",
                CorrelationId: null,
                Result: filter),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Page);
        Assert.Equal(new LoginHistoryQuery(null, null, expectedResult, 1, 50),
            reader.LastLoginQuery);
    }

    [Theory]
    [InlineData("123456789", null, null, "LOGIN_EMPLOYEE_NO_TOO_LONG")]
    [InlineData(null, "123456789012345678901234567890123456789012345678901234567890123456789", null, "LOGIN_CORRELATION_ID_TOO_LONG")]
    [InlineData(null, null, "pending", "LOGIN_RESULT_INVALID")]
    public async Task InvalidLoginFilters_ShouldFailWithoutReading(
        string? employeeNo, string? correlationId, string? filter, string errorCode)
    {
        var reader = new Mock<IHistoryReader>(MockBehavior.Strict);
        var handler = new LoadLoginHistoryHandler(reader.Object);

        LoginHistoryResult result = await handler.HandleAsync(
            new LoginHistoryQuery(employeeNo, correlationId, filter));

        Assert.False(result.Succeeded);
        Assert.Equal(errorCode, result.ErrorCode);
        Assert.Null(result.Page);
        reader.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LoginSearch_ShouldTrimExactFiltersAndFixPagingContract()
    {
        var reader = new RecordingHistoryReader();
        var handler = new LoadLoginHistoryHandler(reader);

        LoginHistoryResult result = await handler.HandleAsync(
            new LoginHistoryQuery(
                EmployeeNo: " C1008267 ",
                CorrelationId: " trace-17 ",
                Result: " SUCCESS ",
                PageNumber: 0,
                PageSize: 200),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(
            new LoginHistoryQuery(
                "C1008267",
                "trace-17",
                "SUCCESS",
                PageNumber: 1,
                PageSize: 50),
            reader.LastLoginQuery);
    }

    [Fact]
    public async Task ChangeHistory_ShouldTrimExactFiltersAndOpenNewestPage()
    {
        var reader = new RecordingHistoryReader();
        var handler = new LoadChangeHistoryHandler(reader);

        ChangeHistoryPage result = await handler.HandleAsync(
            new ChangeHistoryQuery(
                Search: " 3001 ",
                EntityType: " ValidationRole ",
                Action: " Rename ",
                PageNumber: -4,
                PageSize: 100),
            CancellationToken.None);

        Assert.Equal(1, result.PageNumber);
        Assert.Equal(
            new ChangeHistoryQuery(
                "3001",
                "ValidationRole",
                "Rename",
                PageNumber: 1,
                PageSize: 50),
            reader.LastChangeQuery);
    }

    private sealed class RecordingHistoryReader : IHistoryReader
    {
        public LoginHistoryQuery? LastLoginQuery { get; private set; }

        public ChangeHistoryQuery? LastChangeQuery { get; private set; }

        public Task<ChangeHistoryPage> ReadChangesAsync(
            ChangeHistoryQuery query,
            CancellationToken cancellationToken = default)
        {
            LastChangeQuery = query;
            return Task.FromResult(new ChangeHistoryPage(
                [],
                TotalCount: 0,
                PageNumber: query.PageNumber,
                PageSize: query.PageSize));
        }

        public Task<LoginHistoryPage> ReadLoginsAsync(
            LoginHistoryQuery query,
            CancellationToken cancellationToken = default)
        {
            LastLoginQuery = query;
            return Task.FromResult(new LoginHistoryPage(
                [],
                TotalCount: 0,
                PageNumber: query.PageNumber,
                PageSize: query.PageSize));
        }

        public Task<ChangeHistoryDetail?> ReadChangeDetailAsync(
            long changeHistoryId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ChangeHistoryDetail?>(null);
    }
}
