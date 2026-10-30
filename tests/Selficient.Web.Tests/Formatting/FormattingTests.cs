using Selficient.Domain.Measures;
using Selficient.Domain.Risks;
using Selficient.Web.Formatting;

namespace Selficient.Web.Tests.Formatting;

public sealed class FormattingTests
{
    [Fact]
    public void Money_ranges_read_as_pounds()
    {
        MoneyFormat.Range(MoneyRange.Free).ShouldBe("Free");
        MoneyFormat.Range(MoneyRange.Pounds(15m, 15m)).ShouldBe("£15");
        MoneyFormat.Range(MoneyRange.Pounds(720m, 1250m)).ShouldBe("£720 to £1,250");
        MoneyFormat.Range(MoneyRange.Pounds(0m, 1500m)).ShouldBe("£0 to £1,500");
        MoneyFormat.Amount(Money.FromPounds(12.6m).Value).ShouldBe("£12.60");
    }

    [Theory]
    [InlineData(RiskKind.StormPowerCut, "A storm takes the power out")]
    [InlineData(RiskKind.FallingTree, "The tall tree comes down")]
    [InlineData(RiskKind.WaterInterruption, "The water stops")]
    [InlineData(RiskKind.WinterAccess, "Snow or ice closes the road")]
    [InlineData(RiskKind.CommsLoss, "Phones and internet go with the power")]
    [InlineData(RiskKind.GardenFire, "A garden or hillside fire")]
    [InlineData(RiskKind.Flooding, "Flooding")]
    [InlineData(RiskKind.RegionalDisruption, "A two-week regional disruption")]
    [InlineData(RiskKind.IndoorFireOrCarbonMonoxide, "Fire or carbon monoxide from backup cooking")]
    [InlineData((RiskKind)99, "Something else")]
    public void Every_risk_kind_has_a_title(RiskKind kind, string title)
    {
        RiskCopy.Title(kind).ShouldBe(title);
    }

    [Fact]
    public void Ratings_read_as_a_sentence_fragment_with_a_capital()
    {
        RiskCopy.Rating(new Risk(RiskKind.Flooding, Likelihood.High, Impact.Severe, "x")).ShouldBe("High likelihood, severe impact");
        RiskCopy.Rating(new Risk(RiskKind.Flooding, Likelihood.Low, Impact.Medium, "x")).ShouldBe("Low likelihood, medium impact");
        Should.Throw<ArgumentNullException>(() => RiskCopy.Rating(null!));
    }
}
