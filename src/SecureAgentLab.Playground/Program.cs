using SecureAgentLab.Playground.Advanced;
using SecureAgentLab.Playground.Components;
using SecureAgentLab.Playground.Lab;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5080");
builder.WebHost.UseStaticWebAssets();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddScoped<LabSession>();
builder.Services.AddSingleton(new AdvancedProcessRunner(AdvancedProcessRunner.FindRepository(builder.Environment.ContentRootPath), typeof(LabSession).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyConfigurationAttribute), false).Cast<System.Reflection.AssemblyConfigurationAttribute>().Single().Configuration));
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddHttpClient<ILabModel, OpenAiLabModel>().ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
var app = builder.Build();
// Local educational app: reject non-loopback callers and foreign Host headers, even if binding is overridden.
app.Use(async (context, next) =>
{
    if (context.Connection.RemoteIpAddress is not { } address || !System.Net.IPAddress.IsLoopback(address)
        || context.Request.Host.Host is not ("127.0.0.1" or "localhost" or "::1"))
    {
        context.Response.StatusCode = 403;
        return;
    }
    await next(context);
});
app.MapStaticAssets();
app.UseAntiforgery();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();



