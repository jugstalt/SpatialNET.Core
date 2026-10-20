var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "SpatialNET Playground");

app.Run();
