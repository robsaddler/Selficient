using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using NetArchTest.Rules;
using TestResult = NetArchTest.Rules.TestResult;
using Selficient.Application.Abstractions;
using Selficient.Domain.Primitives;
using Selficient.Infrastructure.Composition;
using Selficient.Web.Composition;

namespace Selficient.Architecture.Tests;

/// <summary>
/// The dependency rule from the build plan, section 2.2. Any violation fails the build.
/// </summary>
public sealed class DependencyRuleTests
{
    private static readonly Assembly Domain = typeof(Fault).Assembly;
    private static readonly Assembly Application = typeof(IApplicationMarker).Assembly;
    private static readonly Assembly Infrastructure = typeof(InfrastructureServiceCollectionExtensions).Assembly;
    private static readonly Assembly Web = typeof(WebApplicationExtensions).Assembly;

    [Fact]
    public void Domain_references_nothing_in_the_solution_and_no_io_or_hosting()
    {
        TestResult result = Types.InAssembly(Domain)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Selficient.Application",
                "Selficient.Infrastructure",
                "Selficient.Web",
                "System.Net",
                "System.IO",
                "Microsoft.Extensions",
                "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Application_does_not_reference_infrastructure_or_web()
    {
        TestResult result = Types.InAssembly(Application)
            .ShouldNot()
            .HaveDependencyOnAny("Selficient.Infrastructure", "Selficient.Web", "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Infrastructure_does_not_reference_web()
    {
        TestResult result = Types.InAssembly(Infrastructure)
            .ShouldNot()
            .HaveDependencyOnAny("Selficient.Web", "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Web_touches_infrastructure_only_from_the_composition_root()
    {
        TestResult result = Types.InAssembly(Web)
            .That()
            .ResideInNamespaceStartingWith("Selficient.Web")
            .ShouldNot()
            .HaveDependencyOn("Selficient.Infrastructure")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Coverage_exclusions_appear_nowhere_but_documented_composition_roots()
    {
        IEnumerable<string> offenders =
            from assembly in new[] { Domain, Application, Infrastructure, Web }
            from type in assembly.GetTypes()
            from member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Cast<MemberInfo>().Prepend(type)
            where member.GetCustomAttribute<ExcludeFromCodeCoverageAttribute>() is not null
            where !(type.Namespace ?? string.Empty).EndsWith(".Composition", StringComparison.Ordinal)
            select $"{type.FullName}.{member.Name}";

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Public_domain_types_are_immutable_records_or_static()
    {
        IEnumerable<string> mutable =
            from type in Domain.GetTypes()
            where type.IsPublic && !type.IsAbstract && !type.IsInterface && !type.IsEnum
            where !(type.IsAbstract && type.IsSealed)
            from property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            where property.SetMethod is { IsPublic: true } setter && !IsInitOnly(setter)
            select $"{type.FullName}.{property.Name}";

        mutable.ShouldBeEmpty();
    }

    private static bool IsInitOnly(MethodInfo setter) =>
        setter.ReturnParameter.GetRequiredCustomModifiers()
            .Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");

    private static string Describe(TestResult result) =>
        result.IsSuccessful
            ? "ok"
            : "Violations: " + string.Join(", ", result.FailingTypeNames ?? []);
}
