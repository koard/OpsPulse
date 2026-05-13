using Platform.Domain;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());
});

var app = builder.Build();

app.UseCors();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "project-registry" }));

app.MapGet("/projects", () => Results.Ok(Array.Empty<MonitoredProject>()));

app.MapGet("/projects/{id}", IResult (string id) =>
{
    return Results.NotFound();
});

app.Run();
