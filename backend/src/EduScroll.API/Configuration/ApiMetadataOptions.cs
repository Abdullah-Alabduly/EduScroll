using System.ComponentModel.DataAnnotations;

namespace EduScroll.API.Configuration;

public sealed class ApiMetadataOptions
{
    public const string SectionName = "Api";

    [Required(AllowEmptyStrings = false)]
    public string Title { get; init; } = "EduScroll API";

    [Required(AllowEmptyStrings = false)]
    public string Version { get; init; } = "v1";

    [Required(AllowEmptyStrings = false)]
    public string Description { get; init; } =
        "Backend API foundation for EduScroll educational short-video discovery.";

    public bool EnableOpenApi { get; init; }
}
