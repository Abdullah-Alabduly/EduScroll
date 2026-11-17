using EduScroll.Application.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace EduScroll.Application.Tests;

public sealed class ApplicationFoundationTests
{
    [Fact]
    public void ApplicationAssembly_ShouldNotReferenceInfrastructureOrApi()
    {
        var appAssembly = typeof(DependencyInjection).Assembly;
        var references = appAssembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();

        Assert.DoesNotContain("EduScroll.Infrastructure", references);
        Assert.DoesNotContain("EduScroll.API", references);
        Assert.DoesNotContain(references, n => n is not null && n.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AddApplication_ShouldReturnServiceCollection_AndThrowOnNull()
    {
        IServiceCollection? nullServices = null;
        Assert.Throws<ArgumentNullException>(() => nullServices!.AddApplication());

        var services = new ServiceCollection();
        var returned = services.AddApplication();

        Assert.Same(services, returned);
        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider);
    }

    [Fact]
    public void ExpectedExceptions_ShouldCaptureKindCodeAndValidationErrors()
    {
        var errors = new Dictionary<string, string[]>
        {
            ["sourceUrl"] = ["URL must use http or https."]
        };

        var validation = new ValidationAppException("Invalid input.", errors);
        var notFound = new NotFoundAppException("Source not found.", "SOURCE_NOT_FOUND");
        var conflict = new ConflictAppException("Already exists.", "DUPLICATE_ENTRY");

        Assert.Equal(AppErrorKind.Validation, validation.Kind);
        Assert.Equal("VALIDATION_FAILED", validation.ErrorCode);
        Assert.Single(validation.Errors);

        Assert.Equal(AppErrorKind.NotFound, notFound.Kind);
        Assert.Equal("SOURCE_NOT_FOUND", notFound.ErrorCode);

        Assert.Equal(AppErrorKind.Conflict, conflict.Kind);
        Assert.Equal("DUPLICATE_ENTRY", conflict.ErrorCode);
    }
}
