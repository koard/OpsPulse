using Alerting.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHttpClient();
builder.Services.AddHostedService<AlertEvaluationWorker>();

var host = builder.Build();
host.Run();
