using Impinj.OctaneSdk;
using Microsoft.AspNetCore.Diagnostics;
using RfidReaderService.Configuration;
using RfidReaderService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.Configure<ReaderOptions>(builder.Configuration.GetSection(ReaderOptions.SectionName));
builder.Services.AddSingleton<ImpinjReaderManager>();
builder.Services.AddHostedService<ReaderAutoConnectHostedService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler(handler =>
{
    handler.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var (statusCode, message) = exception switch
        {
            OctaneSdkException ex => (StatusCodes.Status502BadGateway, ex.Message),
            InvalidOperationException ex => (StatusCodes.Status409Conflict, ex.Message),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred."),
        };

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(new { error = message });
    });
});

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
