namespace RoleValidation.Application.History;

public sealed class LoadLoginHistoryHandler
{
    public const int PageSize = 50;

    private readonly IHistoryReader _reader;

    public LoadLoginHistoryHandler(IHistoryReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public async Task<LoginHistoryResult> HandleAsync(
        LoginHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        string? employeeNo = Normalize(query.EmployeeNo);
        string? correlationId = Normalize(query.CorrelationId);
        string? result = Normalize(query.Result)?.ToUpperInvariant();
        if (employeeNo?.Length > 8)
        {
            return LoginHistoryResult.Failure("LOGIN_EMPLOYEE_NO_TOO_LONG");
        }
        if (correlationId?.Length > 64)
        {
            return LoginHistoryResult.Failure("LOGIN_CORRELATION_ID_TOO_LONG");
        }
        if (result == "ALL")
        {
            result = null;
        }
        if (result is not null and not "SUCCESS" and not "DENIED")
        {
            return LoginHistoryResult.Failure("LOGIN_RESULT_INVALID");
        }

        var normalized = new LoginHistoryQuery(
            employeeNo,
            correlationId,
            result,
            Math.Max(1, query.PageNumber),
            PageSize);
        LoginHistoryPage page = await _reader.ReadLoginsAsync(
            normalized,
            cancellationToken);
        return LoginHistoryResult.Success(page);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
