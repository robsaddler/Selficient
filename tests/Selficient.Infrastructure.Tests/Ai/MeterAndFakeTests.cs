using Microsoft.Extensions.Options;
using Selficient.Application.Ai;
using Selficient.Application.Composition;
using Selficient.Application.Planning;
using Selficient.Domain.Households;
using Selficient.Domain.Plans;
using Selficient.Domain.Risks;
using Selficient.Infrastructure.Ai;
using Selficient.TestKit;

namespace Selficient.Infrastructure.Tests.Ai;

public sealed class MeterAndFakeTests
{
    [Fact]
    public void The_meter_counts_households_months_and_the_site_day()
    {
        ManualClock clock = new();
        InMemoryAnalysisMeter meter = new(Options.Create(new AiOptions { FreeAnalysesPerMonth = 2, DailyGlobalCap = 3 }), clock);

        meter.FreePerMonth.ShouldBe(2);
        meter.TryTake("h:a").ShouldBe(MeterOutcome.Allowed);
        meter.TryTake("h:a").ShouldBe(MeterOutcome.Allowed);
        meter.TryTake("h:a").ShouldBe(MeterOutcome.AllowanceUsed);
        meter.TryTake("h:b").ShouldBe(MeterOutcome.Allowed, "a refused household did not burn the site's count");
        meter.TryTake("h:c").ShouldBe(MeterOutcome.SiteBusy, "the third analysis was the day's last");

        clock.Advance(TimeSpan.FromDays(1));
        meter.TryTake("h:c").ShouldBe(MeterOutcome.Allowed, "a new day");
        meter.TryTake("h:a").ShouldBe(MeterOutcome.AllowanceUsed, "still the same month");

        clock.Advance(TimeSpan.FromDays(31));
        meter.TryTake("h:a").ShouldBe(MeterOutcome.Allowed, "a new month");
        Should.Throw<ArgumentNullException>(() => meter.TryTake(null!));
        Should.Throw<ArgumentNullException>(() => new InMemoryAnalysisMeter(null!, clock));
        Should.Throw<ArgumentNullException>(() => new InMemoryAnalysisMeter(Options.Create(new AiOptions()), null!));
    }

    [Fact]
    public async Task The_fake_answers_every_moment_and_can_be_unreadable()
    {
        FakeAssistant fake = new();
        SanitisedImage image = new(new byte[] { 0xFF, 0xD8 }, 640, 480);
        SanitisedImage unreadable = new(new byte[] { 0xFF, 0xD8 }, FakeAssistant.UnreadableWidth, 480);

        KitInventory kit = (await fake.KitAsync(image, KitCandidates.All, CancellationToken.None)).Value;
        BoilerReading boiler = (await fake.BoilerAsync(image, CancellationToken.None)).Value;
        ConsumerUnitReading unit = (await fake.ConsumerUnitAsync(image, CancellationToken.None)).Value;
        PlanNarrative narrative = (await fake.NarrateAsync(RobsPlan(), CancellationToken.None)).Value;

        fake.IsAvailable.ShouldBeTrue();
        kit.Items.Count.ShouldBe(5);
        kit.Satisfied.Select(a => a.Value).ShouldBe(["water-bottled-three-days", "headtorches", "radio-dab-fm-with-crank", "food-no-cook-three-days"]);
        (await fake.KitAsync(image, [], CancellationToken.None)).Value.Satisfied.ShouldBeEmpty("ids outside the candidates are never returned");
        boiler.Make.ShouldBe("Worcester Bosch");
        unit.SpareWays.ShouldBe(2);
        narrative.Paragraphs.Count.ShouldBe(4);
        (await fake.KitAsync(unreadable, KitCandidates.All, CancellationToken.None)).Error.Code.ShouldBe("ai.unavailable");
        (await fake.BoilerAsync(unreadable, CancellationToken.None)).Error.Code.ShouldBe("ai.unavailable");
        (await fake.ConsumerUnitAsync(unreadable, CancellationToken.None)).Error.Code.ShouldBe("ai.unavailable");
        await Should.ThrowAsync<ArgumentNullException>(() => fake.KitAsync(null!, [], CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => fake.KitAsync(image, null!, CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => fake.BoilerAsync(null!, CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => fake.ConsumerUnitAsync(null!, CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => fake.NarrateAsync(null!, CancellationToken.None));
    }

    private static Plan RobsPlan()
    {
        GeneratePlanHandler engine = new(new RiskAssessmentPipeline(ApplicationServiceCollectionExtensions.Assessors()), new PlanBuilder(ApplicationServiceCollectionExtensions.Rules()));
        HouseholdProfile profile = Profiles.RobsHouse();
        return engine.Handle(new GeneratePlanCommand(4, 2, false, false, true, profile.Dwelling.Type, 3, profile.Dwelling.Heating, profile.Dwelling.Hob, true, true, true, true, 232m, profile.Dwelling.FloodRisk)).Value;
    }
}
