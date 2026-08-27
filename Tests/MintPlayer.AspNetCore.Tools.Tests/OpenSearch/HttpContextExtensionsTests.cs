using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.AspNetCore.OpenSearch.Data;
using MintPlayer.AspNetCore.OpenSearch.Extensions;
using Xunit;

namespace MintPlayer.AspNetCore.Tools.Tests.OpenSearch;

public class HttpContextExtensionsTests
{
    private sealed class RecordingExecutor : IActionResultExecutor<ObjectResult>
    {
        public int Calls { get; private set; }
        public ActionContext? Context { get; private set; }
        public ObjectResult? Result { get; private set; }

        public Task ExecuteAsync(ActionContext context, ObjectResult result)
        {
            Calls++;
            Context = context;
            Result = result;
            return Task.CompletedTask;
        }
    }

    private static (DefaultHttpContext Context, RecordingExecutor Executor) CreateContext()
    {
        var executor = new RecordingExecutor();
        var services = new ServiceCollection();
        services.AddSingleton<IActionResultExecutor<ObjectResult>>(executor);

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        return (context, executor);
    }

    [Fact]
    public async Task WriteModelAsync_ExecutesAnObjectResultCarryingTheModel()
    {
        var (context, executor) = CreateContext();
        var model = new OpenSearchDescription { ShortName = "X" };

        await context.WriteModelAsync(model);

        Assert.Equal(1, executor.Calls);
        Assert.Same(model, executor.Result!.Value);
    }

    /// <summary>
    /// <c>DeclaredType = typeof(TModel)</c> is the whole reason the OSDX formatter is selected:
    /// content negotiation asks each formatter about <c>DeclaredType</c>, and the formatter's
    /// <c>CanWriteType</c> is an exact type equality against <c>OpenSearchDescription</c>.
    /// </summary>
    [Fact]
    public async Task WriteModelAsync_SetsDeclaredTypeToTheGenericArgument()
    {
        var (context, executor) = CreateContext();

        await context.WriteModelAsync(new OpenSearchDescription());

        Assert.Equal(typeof(OpenSearchDescription), executor.Result!.DeclaredType);
    }

    /// <summary>
    /// The static generic argument wins over the runtime type — writing the same instance as
    /// <c>object</c> hands the formatter <c>typeof(object)</c>, which it refuses.
    /// </summary>
    [Fact]
    public async Task WriteModelAsync_DeclaredTypeFollowsStaticTypeNotRuntimeType()
    {
        var (context, executor) = CreateContext();
        object model = new OpenSearchDescription();

        await context.WriteModelAsync(model);

        Assert.Equal(typeof(object), executor.Result!.DeclaredType);
    }

    [Fact]
    public async Task WriteModelAsync_ObjectArrayModel_DeclaredTypeIsObjectArray()
    {
        var (context, executor) = CreateContext();

        await context.WriteModelAsync(new object[] { "a", new[] { "b" } });

        Assert.Equal(typeof(object[]), executor.Result!.DeclaredType);
    }

    [Fact]
    public async Task WriteModelAsync_PassesTheSameHttpContextToTheExecutor()
    {
        var (context, executor) = CreateContext();

        await context.WriteModelAsync(new OpenSearchDescription());

        Assert.Same(context, executor.Context!.HttpContext);
    }

    [Fact]
    public async Task WriteModelAsync_ActionDescriptor_IsAnEmptyPlaceholder()
    {
        var (context, executor) = CreateContext();

        await context.WriteModelAsync(new OpenSearchDescription());

        var descriptor = executor.Context!.ActionDescriptor;
        Assert.NotNull(descriptor);
        Assert.Null(descriptor.DisplayName);
        Assert.Null(descriptor.AttributeRouteInfo);
    }

    [Fact]
    public void WriteModelAsync_NullContext_ThrowsArgumentNullException()
    {
        HttpContext? context = null;

        // Written as a statement-bodied Action: the guard throws synchronously, before any Task
        // exists, so this is not an async assertion.
        Action act = () => { _ = context!.WriteModelAsync(new OpenSearchDescription()); };

        var ex = Assert.Throws<ArgumentNullException>(act);

        Assert.Equal("context", ex.ParamName);
    }

    /// <summary>
    /// A null model is accepted: <c>new ObjectResult(null)</c> is legal, so the private
    /// <c>result == null</c> guard never fires from this entry point either.
    /// </summary>
    [Fact]
    public async Task WriteModelAsync_NullModel_StillExecutesAResult()
    {
        var (context, executor) = CreateContext();

        await context.WriteModelAsync<OpenSearchDescription?>(null);

        Assert.Equal(1, executor.Calls);
        Assert.Null(executor.Result!.Value);
    }

    [Fact]
    public async Task WriteModelAsync_MissingExecutor_ThrowsInvalidOperationException()
    {
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.WriteModelAsync(new OpenSearchDescription()));
    }

    /// <summary>
    /// Route values present on the request survive into the <see cref="ActionContext"/>.
    /// </summary>
    [Fact]
    public async Task WriteModelAsync_ExistingRouteData_IsForwarded()
    {
        var (context, executor) = CreateContext();
        context.Request.RouteValues["searchTerms"] = "abc";

        await context.WriteModelAsync(new OpenSearchDescription());

        Assert.Equal("abc", executor.Context!.RouteData.Values["searchTerms"]);
    }

    /// <summary>
    /// Pins that the <c>?? new RouteData()</c> fallback in <c>ExecuteResultAsync</c> is dead code:
    /// <c>HttpContext.GetRouteData()</c> never returns null — with no routing feature at all it
    /// still hands back an empty <see cref="RouteData"/>. Reported as a new finding for M9; the
    /// line cannot be covered.
    /// </summary>
    [Fact]
    public async Task WriteModelAsync_NoRoutingFeature_GetRouteDataIsAlreadyNonNull_KnownGap()
    {
        var (context, executor) = CreateContext();

        Assert.NotNull(context.GetRouteData());

        await context.WriteModelAsync(new OpenSearchDescription());

        Assert.Empty(executor.Context!.RouteData.Values);
    }
}
