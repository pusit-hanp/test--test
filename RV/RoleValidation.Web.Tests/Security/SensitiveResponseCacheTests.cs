using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using RoleValidation.Web.Controllers;

namespace RoleValidation.Web.Tests.Security;

public sealed class SensitiveResponseCacheTests
{
    [Theory]
    [InlineData(typeof(ApplicationUsersController), nameof(ApplicationUsersController.Export))]
    [InlineData(typeof(DeliveryFilesController), nameof(DeliveryFilesController.DownloadZip))]
    [InlineData(typeof(DeliveryFilesController), nameof(DeliveryFilesController.DownloadWorkbook))]
    public async Task SensitiveFileResponse_Should_PreventCaching(
        Type controllerType,
        string actionName)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMvcCore();
        using ServiceProvider provider = services.BuildServiceProvider();
        using var responseBody = new MemoryStream();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = provider
        };
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Response.Body = responseBody;
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());

        // MVC lets action-level cache settings override controller settings.
        // Exercise the effective production filter before executing a real file result.
        ResponseCacheAttribute? cache = controllerType.GetMethod(actionName)!
            .GetCustomAttribute<ResponseCacheAttribute>(inherit: true)
            ?? controllerType.GetCustomAttribute<ResponseCacheAttribute>(inherit: true);
        if (cache is not null)
        {
            IFilterMetadata filter = cache.CreateInstance(provider);
            var executingContext = new ActionExecutingContext(
                actionContext,
                [filter],
                new Dictionary<string, object?>(),
                new object());
            Assert.IsAssignableFrom<IActionFilter>(filter)
                .OnActionExecuting(executingContext);
        }

        byte[] content = [1, 2, 3];
        var result = new FileContentResult(content, "application/octet-stream")
        {
            FileDownloadName = "access-review.xlsx"
        };
        await result.ExecuteResultAsync(actionContext);

        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
        Assert.Equal(content, responseBody.ToArray());
        Assert.Equal("no-store,no-cache", httpContext.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-cache", httpContext.Response.Headers.Pragma.ToString());
    }
}
