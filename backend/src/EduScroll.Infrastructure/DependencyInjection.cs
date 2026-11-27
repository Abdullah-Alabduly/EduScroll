using EduScroll.Application.AiAnalysis.Models;
using EduScroll.Application.AiAnalysis.Ports;
using EduScroll.Application.Authentication.Ports;
using EduScroll.Application.Health;
using EduScroll.Application.Sources.Models;
using EduScroll.Application.Sources.Ports;
using EduScroll.Application.VideoDiscovery.Models;
using EduScroll.Application.VideoDiscovery.Ports;
using EduScroll.Infrastructure.AiAnalysis;
using EduScroll.Infrastructure.Authentication;
using EduScroll.Infrastructure.Configuration;
using EduScroll.Infrastructure.Identity;
using EduScroll.Infrastructure.Persistence;
using EduScroll.Infrastructure.VideoDiscovery;
using EduScroll.Infrastructure.WebExtraction;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace EduScroll.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers Infrastructure layer services: SQL Server EF Core persistence,
    /// ASP.NET Core Identity, JWT Bearer authentication, and readiness probes.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);

        services.AddOptionsWithValidateOnStart<DatabaseOptions>()
            .BindConfiguration(DatabaseOptions.SectionName);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>());

        services.AddOptionsWithValidateOnStart<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>());

        services.AddOptionsWithValidateOnStart<SourceIngestionOptions>()
            .BindConfiguration(SourceIngestionOptions.SectionName);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<SourceIngestionOptions>, SourceIngestionOptionsValidator>());

        services.TryAddSingleton(serviceProvider =>
        {
            var opts = serviceProvider.GetRequiredService<IOptions<SourceIngestionOptions>>().Value;
            return new SourceIngestionLimits(
                MinExtractedTextLength: opts.MinExtractedTextLength,
                MaxExtractedTextLength: opts.MaxExtractedTextLength,
                PreviewMaxLength: opts.PreviewMaxLength,
                DefaultPageSize: opts.DefaultPageSize,
                MaxPageSize: opts.MaxPageSize);
        });

        services.AddOptionsWithValidateOnStart<AiProviderOptions>()
            .BindConfiguration(AiProviderOptions.SectionName);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<AiProviderOptions>, AiProviderOptionsValidator>());

        services.TryAddSingleton(serviceProvider =>
        {
            var opts = serviceProvider.GetRequiredService<IOptions<AiProviderOptions>>().Value;
            return new AiAnalysisLimits(
                MinInputCharacters: opts.MinInputCharacters,
                MaxInputCharacters: opts.MaxInputCharacters,
                MaxSchemaRepairAttempts: opts.MaxSchemaRepairAttempts,
                MaxSummaryLength: opts.MaxSummaryLength,
                MaxTopics: opts.MaxTopics,
                MaxSubtopicsPerTopic: opts.MaxSubtopicsPerTopic,
                MaxConcepts: opts.MaxConcepts,
                MaxKeywords: opts.MaxKeywords,
                SingleLanguageMinSearchQueries: opts.SingleLanguageMinSearchQueries,
                SingleLanguageMaxSearchQueries: opts.SingleLanguageMaxSearchQueries,
                DualLanguageMinSearchQueriesPerLanguage: opts.DualLanguageMinSearchQueriesPerLanguage,
                DualLanguageMaxSearchQueriesPerLanguage: opts.DualLanguageMaxSearchQueriesPerLanguage);
        });

        services.AddOptionsWithValidateOnStart<YouTubeOptions>()
            .BindConfiguration(YouTubeOptions.SectionName);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<YouTubeOptions>, YouTubeOptionsValidator>());

        services.TryAddSingleton(serviceProvider =>
        {
            var opts = serviceProvider.GetRequiredService<IOptions<YouTubeOptions>>().Value;
            return new VideoDiscoveryLimits(
                MetadataBatchSize: opts.MetadataBatchSize,
                MaxVideosPerChannelPerSync: opts.MaxVideosPerChannelPerSync);
        });

        if (!services.Any(sd => sd.ServiceType == typeof(DbContextOptions<ApplicationDbContext>)))
        {
            services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
            {
                var dbOptions = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
                options.UseSqlServer(dbOptions.DefaultConnection, sqlOptions =>
                {
                    sqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                    sqlOptions.CommandTimeout(30);
                    sqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(5),
                        errorNumbersToAdd: null);
                });
            });
        }

        if (!services.Any(sd => sd.ServiceType == typeof(UserManager<ApplicationUser>)))
        {
            services.AddIdentityCore<ApplicationUser>(options =>
                {
                    options.Password.RequiredLength = 8;
                    options.Password.RequireDigit = true;
                    options.Password.RequireLowercase = true;
                    options.Password.RequireUppercase = true;
                    options.Password.RequireNonAlphanumeric = true;
                    options.Password.RequiredUniqueChars = 6;

                    options.User.RequireUniqueEmail = true;
                    options.User.AllowedUserNameCharacters =
                        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-";

                    options.Lockout.AllowedForNewUsers = true;
                    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                    options.Lockout.MaxFailedAccessAttempts = 5;
                })
                .AddRoles<IdentityRole<Guid>>()
                .AddEntityFrameworkStores<ApplicationDbContext>();
        }

        if (!services.Any(sd => sd.ServiceType == typeof(JwtBearerHandler)))
        {
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
                })
                .AddJwtBearer();
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearerOptions>());

        services.AddAuthorization();

        services.TryAddScoped<IUserIdentityStore, IdentityUserStore>();
        services.TryAddScoped<IRefreshTokenStore, RefreshTokenStore>();
        services.TryAddSingleton<ITokenService, JwtTokenService>();
        services.TryAddScoped<IDatabaseReadinessProbe, SqlServerReadinessProbe>();

        services.TryAddScoped<ISourceRepository, SourceRepository>();
        services.TryAddSingleton<IDnsResolver, SystemDnsResolver>();
        services.TryAddSingleton<IWebHttpTransport, SecureSocketsHttpTransport>();
        services.TryAddSingleton<HtmlArticleExtractor>();
        services.TryAddScoped<IWebContentFetcher, SecureWebContentFetcher>();

        services.TryAddScoped<ISourceAnalysisRepository, SourceAnalysisRepository>();
        services.TryAddSingleton<IAiHttpTransport, OllamaHttpTransport>();
        services.TryAddScoped<IAiAnalysisProvider, OllamaAiAnalysisProvider>();

        services.TryAddScoped<IWhitelistedChannelRepository, WhitelistedChannelRepository>();
        services.TryAddScoped<IVideoCatalogRepository, VideoCatalogRepository>();

        if (!services.Any(sd => sd.ServiceType == typeof(IYouTubeRssFeedClient)))
        {
            services.AddHttpClient<IYouTubeRssFeedClient, YouTubeRssFeedClient>(client =>
                {
                    // Per-attempt timeout is enforced via linked CancellationTokenSource inside the client.
                    client.Timeout = Timeout.InfiniteTimeSpan;
                })
                .ConfigurePrimaryHttpMessageHandler(CreateYouTubeSocketsHttpHandler);
        }

        if (!services.Any(sd => sd.ServiceType == typeof(IYouTubeVideoMetadataClient)))
        {
            services.AddHttpClient<IYouTubeVideoMetadataClient, YouTubeVideoMetadataClient>(client =>
                {
                    client.Timeout = Timeout.InfiniteTimeSpan;
                })
                .ConfigurePrimaryHttpMessageHandler(CreateYouTubeSocketsHttpHandler);
        }

        return services;
    }

    private static SocketsHttpHandler CreateYouTubeSocketsHttpHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    };
}
