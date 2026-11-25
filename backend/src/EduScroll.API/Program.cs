using System.Diagnostics;
using EduScroll.API.Configuration;
using EduScroll.API.ErrorHandling;
using EduScroll.API.Health;
using EduScroll.API.Middleware;
using EduScroll.Application;
using EduScroll.Infrastructure;
using Microsoft.AspNetCore.Authorization;
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

// Explicit CORS policy (deny-by-default when AllowedOrigins is empty;
// in Development, also allow dynamic localhost/127.0.0.1 ports used by Flutter Web).
var corsOptions = builder.Configuration
    .GetSection(CorsOptions.SectionName)
    .Get<CorsOptions>() ?? new CorsOptions();
var isDevelopmentEnv = builder.Environment.IsDevelopment();

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsOptions.PolicyName, policy =>
    {
        if (corsOptions.AllowedOrigins.Length > 0 || isDevelopmentEnv)
        {
            if (isDevelopmentEnv)
            {
                var explicitOrigins = new HashSet<string>(
                    corsOptions.AllowedOrigins,
                    StringComparer.OrdinalIgnoreCase);

                policy.SetIsOriginAllowed(origin =>
                {
                    if (explicitOrigins.Contains(origin))
                    {
                        return true;
                    }

                    return Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
                           (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
                           (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(uri.Host, "127.0.0.1", StringComparison.Ordinal));
                });
            }
            else
            {
                policy.WithOrigins(corsOptions.AllowedOrigins);
            }

            policy.AllowAnyHeader()
                .AllowAnyMethod()
                .WithExposedHeaders(CorrelationIdMiddleware.HeaderName);

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

        context.ProblemDetails.Instance ??= context.HttpContext.Request.Path.Value;
        context.ProblemDetails.Extensions.TryAdd("traceId", traceId);
        context.ProblemDetails.Extensions.TryAdd("correlationId", correlationId);

        if (!context.ProblemDetails.Extensions.ContainsKey("code"))
        {
            var defaultCode = context.ProblemDetails.Status switch
            {
                StatusCodes.Status400BadRequest => "VALIDATION_FAILED",
                StatusCodes.Status401Unauthorized => "UNAUTHORIZED",
                StatusCodes.Status403Forbidden => "ACCESS_FORBIDDEN",
                StatusCodes.Status404NotFound => "RESOURCE_NOT_FOUND",
                StatusCodes.Status405MethodNotAllowed => "METHOD_NOT_ALLOWED",
                StatusCodes.Status415UnsupportedMediaType => "UNSUPPORTED_MEDIA_TYPE",
                StatusCodes.Status503ServiceUnavailable => "SERVICE_UNAVAILABLE",
                _ => null
            };

            if (defaultCode is not null)
            {
                context.ProblemDetails.Extensions["code"] = defaultCode;
            }
        }
    };
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Controllers & OpenAPI (v1)
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var normalizedErrors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var (rawKey, entry) in context.ModelState)
            {
                if (entry.Errors.Count == 0)
                {
                    continue;
                }

                var isBodySyntaxKey = string.IsNullOrEmpty(rawKey) || rawKey == "$";
                var key = isBodySyntaxKey
                    ? "request"
                    : char.ToLowerInvariant(rawKey[0]) + rawKey[1..];

                if (!normalizedErrors.TryGetValue(key, out var messages))
                {
                    messages = [];
                    normalizedErrors[key] = messages;
                }

                foreach (var error in entry.Errors)
                {
                    var message = isBodySyntaxKey
                        ? "The request body contains invalid or malformed JSON."
                        : string.IsNullOrWhiteSpace(error.ErrorMessage)
                            ? "The input value is invalid."
                            : error.ErrorMessage;

                    if (!messages.Contains(message, StringComparer.Ordinal))
                    {
                        messages.Add(message);
                    }
                }
            }

            var details = new ValidationProblemDetails(
                normalizedErrors.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray(), StringComparer.Ordinal))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                Instance = context.HttpContext.Request.Path.Value
            };

            var correlationId = context.HttpContext.Items[CorrelationIdMiddleware.ItemKey] as string
                ?? context.HttpContext.TraceIdentifier;
            var traceId = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;

            details.Extensions["code"] = "VALIDATION_FAILED";
            details.Extensions["traceId"] = traceId;
            details.Extensions["correlationId"] = correlationId;

            return new BadRequestObjectResult(details)
            {
                ContentTypes = { "application/problem+json" }
            };
        };
    });
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, context, _) =>
    {
        var metadata = context.ApplicationServices
            .GetRequiredService<IOptions<ApiMetadataOptions>>().Value;

        document.Info.Title = metadata.Title;
        document.Info.Version = metadata.Version;
        document.Info.Description = metadata.Description;

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Enter your JWT Bearer access token."
        };

        // Attach Bearer security requirement using the document-bound scheme reference
        // so OpenAPI serializes [{"Bearer": []}] rather than an empty object.
        foreach (var description in context.DescriptionGroups.SelectMany(g => g.Items))
        {
            var endpointMetadata = description.ActionDescriptor.EndpointMetadata;
            var hasAuthorize = endpointMetadata.OfType<IAuthorizeData>().Any();
            var allowsAnonymous = endpointMetadata.OfType<IAllowAnonymous>().Any();

            if (!hasAuthorize || allowsAnonymous ||
                string.IsNullOrWhiteSpace(description.RelativePath) ||
                string.IsNullOrWhiteSpace(description.HttpMethod))
            {
                continue;
            }

            var pathKey = "/" + description.RelativePath.TrimStart('/');
            if (document.Paths.TryGetValue(pathKey, out var pathItem) &&
                pathItem.Operations is not null &&
                pathItem.Operations.TryGetValue(new HttpMethod(description.HttpMethod), out var operation))
            {
                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference("Bearer", document)] = []
                });
            }
        }

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

// Health checks (liveness remains isolated from database readiness)
builder.Services.AddHealthChecks()
    .AddCheck<SelfLivenessCheck>("self_liveness", tags: ["live"])
    .AddCheck<FoundationReadinessCheck>("foundation_readiness", tags: ["ready"])
    .AddCheck<DatabaseReadinessCheck>("database_readiness", tags: ["ready"]);

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

app.UseAuthentication();
app.UseAuthorization();

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
