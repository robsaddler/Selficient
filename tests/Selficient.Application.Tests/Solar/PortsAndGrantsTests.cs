using Selficient.Application.Geography;
using Selficient.Application.Solar;
using Selficient.Domain.Households;
using Selficient.Domain.Solar;

namespace Selficient.Application.Tests.Solar;

public sealed class PortsAndGrantsTests
{
    [Fact]
    public void The_best_segment_faces_south_of_east_west_or_is_the_largest()
    {
        RoofInsights robs = new([new RoofSegment(45, 35, 60), new RoofSegment(225, 35, 48), new RoofSegment(135, 30, 9)], 14, 430);
        RoofInsights northOnly = new([new RoofSegment(10, 35, 60), new RoofSegment(350, 35, 20)], 10, 400);
        RoofInsights empty = new([], 0, 400);

        robs.Best!.Bearing.ShouldBe(225);
        robs.Best.Aspect.ShouldBe(RoofAspect.SouthWest);
        northOnly.Best!.Bearing.ShouldBe(10);
        empty.Best.ShouldBeNull();
    }

    [Fact]
    public async Task The_null_provider_is_never_available_and_never_answers()
    {
        NoRoofInsights none = new();

        none.IsAvailable.ShouldBeFalse();
        (await none.DescribeAsync(new Coordinates(51, -3), CancellationToken.None)).Error.Code.ShouldBe("lookup.unavailable");
    }

    [Theory]
    [InlineData(Nation.Wales, "Green Homes Wales 0 percent loan")]
    [InlineData(Nation.Scotland, "Home Energy Scotland grant and loan")]
    [InlineData(Nation.England, "Warm Homes: Local Grant")]
    [InlineData(Nation.NorthernIreland, "Northern Ireland schemes")]
    public void Each_nation_gets_the_universal_grants_and_its_own(Nation nation, string own)
    {
        IReadOnlyList<Grant> grants = GrantBook.For(nation);

        grants.Count.ShouldBe(3);
        grants[0].Title.ShouldBe("Zero-rate VAT until 31 March 2027");
        grants[1].Title.ShouldBe("Smart Export Guarantee");
        grants[2].Title.ShouldBe(own);
        GrantBook.For(Nation.Unknown).Count.ShouldBe(2);
    }

    [Fact]
    public void Grant_copy_is_reviewed_twice_a_year_and_links_are_https()
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

        GrantBook.All.Count.ShouldBe(6);
        GrantBook.All.ShouldAllBe(g => today.DayNumber - g.Seen.DayNumber <= 180, "a grant over 180 days old is a reminder to review the book");
        GrantBook.All.ShouldAllBe(g => g.Link.Scheme == "https");
        GrantBook.All.ShouldAllBe(g => g.Sentence.EndsWith('.'));
    }
}
