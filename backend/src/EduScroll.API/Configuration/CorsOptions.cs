namespace EduScroll.API.Configuration;

public sealed class CorsOptions
{
    public const string SectionName = "Cors";
    public const string PolicyName = "EduScrollExplicitCors";

    public string[] AllowedOrigins { get; init; } = [];

    public bool AllowCredentials { get; init; }
}
