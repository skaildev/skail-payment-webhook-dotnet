using Skail.Platform.Runtime;
using Skail.Platform.Runtime.Hosting;
using SkailPaymentWebhook;
using SkailPaymentWebhook.Demo;
using SkailPaymentWebhook.Skail;

[assembly: VisibleToSkailPlatform]

// usage: dotnet run
//   then open http://localhost:8080
if (args.Any(a => !a.StartsWith('-')))
{
    Console.Error.WriteLine("This demo takes no arguments.");
    return 1;
}

DotEnv.Load();
string[] required = ["SKAIL_WORKLOAD", "SKAIL_NAMESPACE", "SKAIL_KEY", "SKAIL_SIDECAR"];
var missing = required.Where(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))).ToArray();
if (missing.Length > 0)
{
    Console.Error.WriteLine($"Missing {string.Join(", ", missing)}. Copy .env.example to .env and fill it in.");
    return 1;
}

var builder = WebApplication.CreateBuilder(args);

// keep the console for the demo: only warnings and errors from the host and the libraries
builder.Logging.SetMinimumLevel(LogLevel.Warning);

// the page: localhost only, unless DEMO_LISTEN says otherwise (the container listens on all interfaces)
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("DEMO_LISTEN") ?? "http://localhost:8080");

builder.Services.AddSingleton<Timeline>();
builder.Services.AddSingleton<SimulatedStore>();
builder.Services.AddSingleton<PaymentDesk>();
builder.Services.AddSingleton(SkailApiOptions.FromEnvironment());
builder.Services.AddHttpClient<SkailApiClient>(http => http.Timeout = TimeSpan.FromSeconds(10));

// hosts the worker that executes PaymentFunctions; reads the SKAIL_* variables
builder.UseSkail();

var app = builder.Build();
var desk = app.Services.GetRequiredService<PaymentDesk>();

var page = Path.Combine(AppContext.BaseDirectory, "Page", "index.html");
app.MapGet("/", () => Results.File(page, "text/html; charset=utf-8"));
app.MapGet("/api/state", () => Results.Json(desk.View()));
app.MapPost("/api/payments/{path}", async (string path, CancellationToken ct) =>
    await desk.SendAsync(path, ct) ? Results.Ok() : Results.Conflict());
app.MapPost("/api/payments/{path}/again", async (string path, CancellationToken ct) =>
    await desk.SendAgainAsync(path, ct) ? Results.Ok() : Results.Conflict());
app.MapPost("/api/reset", () =>
{
    desk.Reset();
    return Results.Ok();
});

app.Lifetime.ApplicationStarted.Register(() =>
    Console.WriteLine("Open http://localhost:8080 to send payments and resend them. Ctrl+C exits."));

await app.RunAsync();
return 0;
