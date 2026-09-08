using EntraAuth.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddEntraApiAuthentication(builder.Configuration);
builder.Services.AddEntraApiAuthorization(builder.Configuration);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();

app.MapGet("/orders", () => Results.Ok(new { items = Array.Empty<object>() }))
    .RequireAuthorization("Orders.Read");

app.MapPost("/orders", () => Results.Created("/orders/1", new { id = 1 }))
    .RequireAuthorization("Orders.Write");

app.Run();
