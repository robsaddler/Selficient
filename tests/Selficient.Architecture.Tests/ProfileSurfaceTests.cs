using System.Reflection;
using Selficient.Application.Planning;
using Selficient.Domain.Households;

namespace Selficient.Architecture.Tests;

/// <summary>Threat model case 8: no path for user text into a sentence.</summary>
public sealed class ProfileSurfaceTests
{
    [Fact]
    public void The_profile_carries_no_free_text()
    {
        Type[] inputs = [typeof(Household), typeof(Dwelling), typeof(HouseholdProfile), typeof(GeneratePlanCommand)];

        IEnumerable<string> textual =
            from type in inputs
            from property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            where property.PropertyType == typeof(string)
            select $"{type.Name}.{property.Name}";

        textual.ShouldBeEmpty();
    }
}
