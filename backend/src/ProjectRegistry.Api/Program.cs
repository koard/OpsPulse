using Platform.Domain;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());
});

var app = builder.Build();

app.UseCors();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "project-registry" }));

app.MapGet("/projects", () => Results.Ok(DemoData.Projects));

app.MapGet("/projects/{id}", IResult (string id) =>
{
    var project = DemoData.Projects.FirstOrDefault(candidate =>
        string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));

    if (project is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(project);
});

app.Run();
