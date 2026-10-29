using Selficient.Application.Abstractions;

namespace Selficient.Application.Tests.Abstractions;

/// <summary>
/// Keeps the Application suite runnable until the first use case lands in M1, when this file and
/// the marker it pins are deleted together.
/// </summary>
public sealed class ApplicationMarkerTests
{
    [Fact]
    public void The_marker_anchors_the_application_assembly()
    {
        Type marker = typeof(IApplicationMarker);

        marker.IsInterface.ShouldBeTrue();
        marker.Assembly.GetName().Name.ShouldBe("Selficient.Application");
    }
}
