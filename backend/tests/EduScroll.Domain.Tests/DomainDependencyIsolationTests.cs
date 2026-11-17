using EduScroll.Domain.Exceptions;

namespace EduScroll.Domain.Tests;

public sealed class DomainDependencyIsolationTests
{
    [Fact]
    public void DomainAssembly_ShouldHaveZeroDependenciesOnOtherLayersOrFrameworks()
    {
        var domainAssembly = typeof(DomainException).Assembly;
        var referencedNames = domainAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        Assert.DoesNotContain(referencedNames, n => n!.StartsWith("EduScroll.", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referencedNames, n => n!.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referencedNames, n => n!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referencedNames, n => n!.StartsWith("Microsoft.Extensions", StringComparison.OrdinalIgnoreCase));
    }
}
