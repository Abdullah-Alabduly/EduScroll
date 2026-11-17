using System.Diagnostics;
using EduScroll.API.Configuration;
using EduScroll.API.ErrorHandling;
using EduScroll.API.Health;
using EduScroll.API.Middleware;
using EduScroll.Application;
using EduScroll.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Layer composition
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Strongly typed, startup-validated configuration
builder.Services
    .AddOptionsWithValidateOnStart<ApiMetadataOptions>()
    .BindConfiguration(ApiMetadataOptions.SectionName)
    .ValidateDataAnnotations();

builder.Services
    .AddOptionsWithValidateOnStart<CorsOptions>()
    .BindConfiguration(CorsOptions.SectionName);
builder.Services.AddSingleton<IValidateOptions<CorsOptions>, CorsOptionsValidator>();

// Explicit CORS policy (deny-by-default when AllowedOrigins is empty)
var corsOptions = builder.Configuration
    .GetSection(CorsOptions.SectionName)
    .Get<CorsOptions>() ?? new CorsOptions();

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsOptions.PolicyName, policy =>
    {
        if (corsOptions.AllowedOrigins.Length > 0)
        {
            policy.WithOrigins(corsOptions.AllowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();

            if (corsOptions.AllowCredentials)
            {
                policy.AllowCredentials();
            }
        }
    });
});

// Standardized RFC 9457 Problem Details & global exception handling
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        var correlationId = context.HttpContext.Items[CorrelationIdMiddleware.ItemKey] as string
            ?? context.HttpContext.TraceIdentifier;
        var traceId = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;

        context.ProblemDetails.Extensions.TryAdd("traceId", traceId);
        context.ProblemDetails.Extensions.TryAdd("correlationId", correlationId);
    };
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Controllers & OpenAPI (v1)
builder.Services.AddControllers();
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, context, _) =>
    {
        var metadata = context.ApplicationServices
            .GetRequiredService<IOptions<ApiMetadataOptions>>().Value;

        document.Info.Title = metadata.Title;
        document.Info.Version = metadata.Version;
        document.Info.Description = metadata.Description;
        return Task.CompletedTask;
    });

    options.AddSchemaTransformer((schema, context, _) =>
    {
        if (context.JsonTypeInfo.Type == typeof(ProblemDetails))
        {
            schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
            schema.Properties.TryAdd("code", new OpenApiSchema
            {
                Type = JsonSchemaType.String | JsonSchemaType.Null,
                Description = "Machine-readable application or domain error code."
            });
            schema.Properties.TryAdd("traceId", new OpenApiSchema
            {
                Type = JsonSchemaType.String | JsonSchemaType.Null,
                Description = "Distributed trace identifier for request diagnostics."
            });
            schema.Properties.TryAdd("correlationId", new OpenApiSchema
            {
                Type = JsonSchemaType.String | JsonSchemaType.Null,
                Description = "Correlation identifier echoed via X-Correlation-Id."
            });
        }

        return Task.CompletedTask;
    });
});

// Health checks (Sprint 1 scope only; DB readiness check will be added in Sprint 2)
builder.Services.AddHealthChecks()
    .AddCheck<SelfLivenessCheck>("self_liveness", tags: ["live"])
    .AddCheck<FoundationReadinessCheck>("foundation_readiness", tags: ["ready"]);

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

var httpsRedirectionEnabled = app.Configuration.GetValue("HttpsRedirection:Enabled", true);
if (httpsRedirectionEnabled && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}

app.UseCors(CorsOptions.PolicyName);

var apiMetadata = app.Services.GetRequiredService<IOptions<ApiMetadataOptions>>().Value;
if (app.Environment.IsDevelopment() && apiMetadata.EnableOpenApi)
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", $"{apiMetadata.Title} {apiMetadata.Version}");
        options.RoutePrefix = "swagger";
    });
}

app.MapControllers();

app.Run();

public partial class Program;
