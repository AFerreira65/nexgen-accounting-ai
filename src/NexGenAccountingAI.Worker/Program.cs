using NexGenAccountingAI.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Services.AddHostedService<FoundationWorker>();

var host = builder.Build();
await host.RunAsync().ConfigureAwait(false);
