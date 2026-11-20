using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Skail.Platform.Runtime;
using Skail.Platform.Runtime.Hosting;
using SkailPaymentWebhook;
using SkailPaymentWebhook.Demo;
using SkailPaymentWebhook.Skail;

[assembly: VisibleToSkailPlatform]

// usage: dotnet run [-- released | amount | paid | missing]
//   no argument: one payment per path; a path: only that payment
var argument = args.FirstOrDefault(a => !a.StartsWith('-'));
IReadOnlyList<DemoPayment> payments = argument is null ? DemoPayments.All
    : DemoPayments.Find(argument) is { } one ? [one]
    : [];
if (payments.Count == 0)
{
    Console.Error.WriteLine($"Unknown argument '{argument}'. Use {string.Join(", ", DemoPayments.All.Select(p => p.Path))}.");
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

var builder = Host.CreateApplicationBuilder(args);

// keep the console for the demo: only warnings and errors from the host and the libraries
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.AddSingleton<SimulatedStore>();
builder.Services.AddSingleton(SkailApiOptions.FromEnvironment());
builder.Services.AddHttpClient<SkailApiClient>(http => http.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddSingleton(payments);
builder.Services.AddHostedService<PaymentGateway>();

// hosts the worker that executes PaymentFunctions; reads the SKAIL_* variables
builder.UseSkail();

await builder.Build().RunAsync();
return 0;
