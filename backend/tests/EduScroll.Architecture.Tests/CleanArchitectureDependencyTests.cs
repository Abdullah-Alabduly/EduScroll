using System.Reflection;
using System.Xml.Linq;
using EduScroll.API.Controllers;
using EduScroll.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using ApplicationMarker = EduScroll.Application.DependencyInjection;
using InfrastructureMarker = EduScroll.Infrastructure.DependencyInjection;

namespace EduScroll.Architecture.Tests;

public sealed class CleanArchitectureDependencyTests
{
    private const string DomainAssemblyName = "EduScroll.Domain";
    private const string ApplicationAssemblyName = "EduScroll.Application";
    private const string InfrastructureAssemblyName = "EduScroll.Infrastructure";
    private const string ApiAssemblyName = "EduScroll.API";

    [Fact]
    public void Domain_ShouldNotReference_Application_Infrastructure_Api_Or_FrameworkPackages()
    {
        var referencedAssemblies = typeof(DomainException).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain(ApplicationAssemblyName, referencedAssemblies);
        Assert.DoesNotContain(InfrastructureAssemblyName, referencedAssemblies);
        Assert.DoesNotContain(ApiAssemblyName, referencedAssemblies);
        Assert.DoesNotContain(referencedAssemblies, n => n!.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("System.Net.Http", referencedAssemblies);
        Assert.DoesNotContain("System.Text.Json", referencedAssemblies);

        var projectReferences = GetCsprojProjectReferences(DomainAssemblyName);
        Assert.Empty(projectReferences);
    }

    [Fact]
    public void Application_ShouldNotReference_Infrastructure_Or_Api()
    {
        var referencedAssemblies = typeof(ApplicationMarker).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain(InfrastructureAssemblyName, referencedAssemblies);
        Assert.DoesNotContain(ApiAssemblyName, referencedAssemblies);

        var projectReferences = GetCsprojProjectReferences(ApplicationAssemblyName);
        Assert.Equal([DomainAssemblyName], projectReferences);
    }

    [Fact]
    public void Infrastructure_ShouldNotReference_Api()
    {
        var referencedAssemblies = typeof(InfrastructureMarker).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain(ApiAssemblyName, referencedAssemblies);

        var projectReferences = GetCsprojProjectReferences(InfrastructureAssemblyName);
        Assert.DoesNotContain(ApiAssemblyName, projectReferences, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(DomainAssemblyName, projectReferences, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(ApplicationAssemblyName, projectReferences, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApiControllersAndComponents_ShouldNotDependOnInfrastructureTypes()
    {
        var apiAssembly = typeof(HealthController).Assembly;
        var controllerTypes = apiAssembly
            .GetTypes()
            .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t))
            .ToArray();

        Assert.NotEmpty(controllerTypes);

        var nonCompositionApiTypes = apiAssembly
            .GetTypes()
            .Where(t => t.IsClass &&
                        !t.IsAbstract &&
                        t.Name != "Program" &&
                        !t.Name.StartsWith('<'))
            .ToArray();

        foreach (var apiType in nonCompositionApiTypes)
        {
            foreach (var ctor in apiType.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                foreach (var param in ctor.GetParameters())
                {
                    AssertTypeGraphDoesNotReferenceAssembly(
                        param.ParameterType,
                        InfrastructureAssemblyName,
                        $"{apiType.Name} constructor parameter '{param.Name}'");
                }
            }

            foreach (var method in apiType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                AssertTypeGraphDoesNotReferenceAssembly(
                    method.ReturnType,
                    InfrastructureAssemblyName,
                    $"{apiType.Name}.{method.Name} return type");

                foreach (var param in method.GetParameters())
                {
                    AssertTypeGraphDoesNotReferenceAssembly(
                        param.ParameterType,
                        InfrastructureAssemblyName,
                        $"{apiType.Name}.{method.Name} parameter '{param.Name}'");
                }
            }
        }
    }

    [Fact]
    public void SolutionSourceProjects_ShouldHaveNoCircularDependencies()
    {
        var projects = new[]
        {
            DomainAssemblyName,
            ApplicationAssemblyName,
            InfrastructureAssemblyName,
            ApiAssemblyName
        };

        var graph = projects.ToDictionary(
            p => p,
            GetCsprojProjectReferences,
            StringComparer.OrdinalIgnoreCase);

        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in projects)
        {
            AssertNoCycle(project, graph, state);
        }
    }

    private static void AssertTypeGraphDoesNotReferenceAssembly(
        Type type,
        string forbiddenAssemblyName,
        string context)
    {
        var visited = new HashSet<Type>();
        var queue = new Queue<Type>();
        queue.Enqueue(type);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current))
            {
                continue;
            }

            Assert.False(
                string.Equals(current.Assembly.GetName().Name, forbiddenAssemblyName, StringComparison.OrdinalIgnoreCase),
                $"{context} must not depend on {forbiddenAssemblyName} type '{current.FullName}'.");

            if (current.HasElementType && current.GetElementType() is { } elementType)
            {
                queue.Enqueue(elementType);
            }

            if (current.IsGenericType)
            {
                foreach (var arg in current.GetGenericArguments())
                {
                    queue.Enqueue(arg);
                }
            }
        }
    }

    private static void AssertNoCycle(
        string node,
        IReadOnlyDictionary<string, string[]> graph,
        IDictionary<string, int> state)
    {
        if (state.TryGetValue(node, out var currentState))
        {
            Assert.NotEqual(1, currentState);
            if (currentState == 2)
            {
                return;
            }
        }

        state[node] = 1;

        if (graph.TryGetValue(node, out var neighbors))
        {
            foreach (var neighbor in neighbors)
            {
                AssertNoCycle(neighbor, graph, state);
            }
        }

        state[node] = 2;
    }

    private static string[] GetCsprojProjectReferences(string projectName)
    {
        var srcDirectory = FindBackendSrcDirectory();
        var csprojPath = Path.Combine(srcDirectory, projectName, $"{projectName}.csproj");
        Assert.True(File.Exists(csprojPath), $"Expected project file not found: {csprojPath}");

        var doc = XDocument.Load(csprojPath);
        return doc.Descendants("ProjectReference")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => Path.GetFileNameWithoutExtension(v!))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string FindBackendSrcDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(candidate) &&
                Directory.Exists(Path.Combine(candidate, DomainAssemblyName)))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate 'backend/src' directory from test base directory.");
    }
}
