var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { service = "Ez.Handball.Api", status = "ok" }));

app.Run();
