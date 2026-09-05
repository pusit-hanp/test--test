using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Moq;
using RoleValidation.Core.Features.Applications;
using RoleValidation.Web.Controllers;
using RoleValidation.Web.Models.Applications;

namespace RoleValidation.Web.Tests.Applications;

public sealed class ApplicationLifecycleViewTests
{
    [Theory]
    [InlineData(false, "true")]
    [InlineData(true, "false")]
    public async Task LifecycleForm_ShouldPostExplicitBooleanForOppositeState(
        bool currentActive, string expectedPostedValue)
    {
        var model = new ApplicationManagementViewModel
        {
            CanManage = true,
            Applications = [new(17, "PORTAL", "Portal", currentActive,
                new ApplicationDependencyCounts(0, 0, 0))]
        };

        string html = await RenderAsync(model);
        System.Text.RegularExpressions.Match input = Regex.Match(
            html, "<input[^>]*name=\"isActive\"[^>]*>");

        Assert.True(input.Success, "Lifecycle state input was not rendered.");
        Assert.Contains($"value=\"{expectedPostedValue}\"", input.Value);
        string postedValue = Regex.Match(input.Value, "value=\"([^\"]*)\"").Groups[1].Value;
        Assert.True(bool.TryParse(postedValue, out bool submittedState));
        Assert.Equal(!currentActive, submittedState);
    }

    private static async Task<string> RenderAsync(ApplicationManagementViewModel model)
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(value => value.ApplicationName)
            .Returns(typeof(ApplicationsController).Assembly.GetName().Name!);
        environment.SetupGet(value => value.EnvironmentName).Returns(Environments.Development);
        environment.SetupGet(value => value.ContentRootPath).Returns(AppContext.BaseDirectory);
        environment.SetupGet(value => value.ContentRootFileProvider).Returns(new NullFileProvider());
        environment.SetupGet(value => value.WebRootPath).Returns(AppContext.BaseDirectory);
        environment.SetupGet(value => value.WebRootFileProvider).Returns(new NullFileProvider());
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(environment.Object);
        var diagnostics = new DiagnosticListener("ApplicationLifecycleViewTests");
        services.AddSingleton(diagnostics);
        services.AddSingleton<DiagnosticSource>(diagnostics);
        services.AddControllersWithViews().AddApplicationPart(typeof(ApplicationsController).Assembly);
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var routeData = new RouteData();
        routeData.Values["controller"] = "Applications";
        routeData.Values["action"] = "Index";
        var router = new Mock<IRouter>();
        router.Setup(value => value.GetVirtualPath(It.IsAny<VirtualPathContext>()))
            .Returns((VirtualPathContext context) => new VirtualPathData(router.Object,
                $"/{context.Values["controller"]}/{context.Values["action"]}"));
        routeData.Routers.Add(router.Object);
        var context = new ActionContext(httpContext, routeData, new ActionDescriptor());
        var engine = scope.ServiceProvider.GetRequiredService<IRazorViewEngine>();
        var found = engine.GetView(null, "/Views/Applications/Index.cshtml", true);
        Assert.True(found.Success);
        var viewData = new ViewDataDictionary(
            scope.ServiceProvider.GetRequiredService<IModelMetadataProvider>(),
            new ModelStateDictionary()) { Model = model };
        using var writer = new StringWriter();
        await found.View.RenderAsync(new ViewContext(context, found.View, viewData,
            new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>()),
            writer, new HtmlHelperOptions()));
        return writer.ToString();
    }
}
