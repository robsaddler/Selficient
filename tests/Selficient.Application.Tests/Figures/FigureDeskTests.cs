using NSubstitute;
using Selficient.Application.Composition;
using Selficient.Application.Figures;
using Selficient.Application.Operations;
using Selficient.Application.Planning;
using Selficient.Domain.Households;
using Selficient.Domain.Parameters;
using Selficient.Domain.Plans;
using Selficient.Domain.Primitives;
using Selficient.Domain.Risks;
using Selficient.TestKit;

namespace Selficient.Application.Tests.Figures;

public sealed class FigureDeskTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    /// <summary>A desk over a store that remembers, so drafting, publishing and reading back are one story.</summary>
    private sealed class Rig
    {
        public IFigureStore Store { get; } = Substitute.For<IFigureStore>();

        public IOperationsLog Log { get; } = Substitute.For<IOperationsLog>();

        public List<FigureDraft> Drafts { get; } = [];

        public List<PublishedFigures> Sets { get; } = [];

        public List<FigureChange> Changes { get; } = [];

        public CurrentFigures Current { get; }

        public FigureDesk Desk { get; }

        public Rig()
        {
            Store.DraftsAsync(Arg.Any<CancellationToken>()).Returns(_ => (IReadOnlyList<FigureDraft>)[.. Drafts]);
            Store.SaveDraftAsync(Arg.Any<FigureDraft>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                FigureDraft draft = call.Arg<FigureDraft>();
                Drafts.RemoveAll(other => other.ParameterId == draft.ParameterId);
                Drafts.Add(draft);
                return Task.CompletedTask;
            });
            Store.CurrentAsync(Arg.Any<CancellationToken>()).Returns(_ => Sets.LastOrDefault());
            Store.FindAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(call => Sets.FirstOrDefault(set => set.Version == call.Arg<int>()));
            Store.MayPublishAsync("rob", Arg.Any<CancellationToken>()).Returns(true);
            Store.PublishAsync(Arg.Any<PublishedFigures>(), Arg.Any<IReadOnlyList<FigureChange>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                Sets.Add(call.Arg<PublishedFigures>());
                Changes.AddRange(call.Arg<IReadOnlyList<FigureChange>>());
                if (call.Arg<bool>())
                {
                    Drafts.Clear();
                }

                return true;
            });
            Current = new CurrentFigures(Store);
            Desk = new FigureDesk(Store, Current, Log, new RiskAssessmentPipeline(ApplicationServiceCollectionExtensions.Assessors()), new PlanBuilder(ApplicationServiceCollectionExtensions.Rules()), new ManualClock(Now));
        }
    }

    [Fact]
    public async Task A_correction_is_drafted_with_a_reason_and_a_source_and_replaces_an_earlier_draft_of_the_figure()
    {
        Rig rig = new();

        Result<FigureDraft> first = await rig.Desk.DraftAsync("expert", "stairlift-batteries.stock", 3m, "  An engineer measured it.  ", " Stannah manual ", CancellationToken.None);
        await rig.Desk.DraftAsync("expert", "stairlift-batteries.stock", 2.5m, "Measured again.", "The same manual", CancellationToken.None);

        first.Value.ShouldBe(new FigureDraft("stairlift-batteries.stock", 3m, "An engineer measured it.", "Stannah manual", "expert", Now));
        rig.Drafts.ShouldHaveSingleItem().Value.ShouldBe(2.5m);
        (await rig.Desk.DraftAsync("expert", "stairlift-batteries.stock", 3m, " ", "A manual", CancellationToken.None)).Error.Message.ShouldBe("Say why the figure should change, in up to 400 characters.");
        (await rig.Desk.DraftAsync("expert", "stairlift-batteries.stock", 3m, "Why.", null!, CancellationToken.None)).Error.Message.ShouldBe("Say where the value comes from, in up to 400 characters.");
        (await rig.Desk.DraftAsync("expert", "stairlift-batteries.stock", 3m, new string('x', 401), "A manual", CancellationToken.None)).Error.Code.ShouldBe("figures.text");
        (await rig.Desk.DraftAsync("expert", "no-such-figure", 3m, "Why.", "Where.", CancellationToken.None)).Error.Code.ShouldBe("parameters.unknown");
        (await rig.Desk.DraftAsync("expert", "stairlift-batteries.stock", 1000m, "Why.", "Where.", CancellationToken.None)).Error.Code.ShouldBe("parameters.range");
        rig.Drafts.ShouldHaveSingleItem("a refused draft is not kept");
        await Should.ThrowAsync<ArgumentException>(() => rig.Desk.DraftAsync(" ", "stairlift-batteries.stock", 3m, "Why.", "Where.", CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => rig.Desk.DraftAsync("expert", null!, 3m, "Why.", "Where.", CancellationToken.None));
    }

    [Fact]
    public async Task A_draft_is_checked_with_every_other_draft_so_what_can_be_published_can_always_be_run()
    {
        Rig rig = new();

        (await rig.Desk.DraftAsync("expert", "price.headtorches.high", 10m, "Why.", "Where.", CancellationToken.None)).Error.Message.ShouldBe("A headtorch cannot cost more at its least than at its most.");
        (await rig.Desk.DraftAsync("expert", "price.headtorches.low", 5m, "Why.", "Where.", CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await rig.Desk.DraftAsync("expert", "price.headtorches.high", 10m, "Why.", "Where.", CancellationToken.None)).IsSuccess.ShouldBeTrue("with the least drafted down first, the most can follow it");
        rig.Drafts.Count.ShouldBe(2);

        rig.Store.DiscardDraftAsync("price.headtorches.low", Arg.Any<CancellationToken>()).Returns(true);
        (await rig.Desk.DiscardAsync("price.headtorches.low", CancellationToken.None)).ShouldBeTrue();
        await Should.ThrowAsync<ArgumentNullException>(() => rig.Desk.DiscardAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task The_preview_says_which_households_a_draft_would_move_and_by_how_much()
    {
        Rig rig = new();
        (await rig.Desk.PreviewAsync(CancellationToken.None)).Value.Lines.ShouldBeEmpty("nothing drafted, nothing moves");

        await rig.Desk.DraftAsync("expert", "stairlift-batteries.stock", 1m, "Why.", "Where.", CancellationToken.None);
        await rig.Desk.DraftAsync("expert", "price.headtorches.high", 40m, "Why.", "Where.", CancellationToken.None);
        FigurePreview preview = (await rig.Desk.PreviewAsync(CancellationToken.None)).Value;

        preview.Compared.ShouldBe(ReferenceHouseholds.All.Count * Domain.Resilience.Scenarios.All.Count * 2);
        preview.Lines.ShouldNotBeEmpty();
        preview.Lines.ShouldAllBe(line => line.Household == "An older person in a terrace with a battery stairlift and a prepayment meter", "only the house with a battery stairlift is moved by its batteries");
        preview.Lines.ShouldAllBe(line => line.After < line.Before);
        preview.Lines.Select(line => line.Doing).ShouldAllBe(doing => doing == ReferenceHouseholds.NothingDone || doing == ReferenceHouseholds.ThisWeekDone);
        preview.Costs.Count.ShouldBe(ReferenceHouseholds.All.Count, "every household is asked to buy headtorches");
        preview.Costs.ShouldAllBe(cost => cost.After.To.Pence > cost.Before.To.Pence && cost.After.From == cost.Before.From);

        rig.Drafts.Add(new FigureDraft("no-such-figure", 1m, "Why.", "Where.", "expert", Now));
        (await rig.Desk.PreviewAsync(CancellationToken.None)).Error.Code.ShouldBe("parameters.unknown", "a draft a release has since made impossible is said, not run");
    }

    [Fact]
    public async Task Only_an_operator_who_may_publish_puts_a_draft_into_force_and_every_figure_it_moves_is_recorded()
    {
        Rig rig = new();
        await rig.Desk.DraftAsync("expert", "stairlift-batteries.stock", 3m, "An engineer measured it.", "Stannah manual", CancellationToken.None);
        await rig.Desk.DraftAsync("expert", "demand.drinking-litres", 3m, "No change, to see what happens.", "Nowhere", CancellationToken.None);

        (await rig.Desk.PublishAsync("expert", "The stairlift figures.", CancellationToken.None)).Error.Message.ShouldBe("Only an operator who may publish figures can put them into force.");
        (await rig.Desk.PublishAsync("rob", "  ", CancellationToken.None)).Error.Message.ShouldBe("Say what this publication is for, in up to 400 characters.");
        rig.Sets.ShouldBeEmpty();

        PublishedFigures published = (await rig.Desk.PublishAsync("rob", " The stairlift figures. ", CancellationToken.None)).Value;

        published.ShouldBe(rig.Sets.Single());
        (published.Version, published.At, published.By, published.Note, published.Restores).ShouldBe((1, Now, "rob", "The stairlift figures.", null));
        published.Corrections.ShouldBe(new Dictionary<string, decimal> { ["stairlift-batteries.stock"] = 3m });
        rig.Changes.ShouldHaveSingleItem().ShouldBe(new FigureChange(1, "stairlift-batteries.stock", 2m, 3m, "An engineer measured it.", "Stannah manual", "expert", Now), "a draft that changes nothing is no change");
        rig.Drafts.ShouldBeEmpty();
        rig.Current.Current.Version.ShouldBe(1, "the set is in force the moment it is published");
        rig.Current.Current["stairlift-batteries.stock"].ShouldBe(3m);
        rig.Current.Published.ShouldBe(published);
        await rig.Log.Received(1).RecordHandAsync("publish-figures", null, "Set 1 is in force: 1 figures changed. The stairlift figures.", "rob", Arg.Any<CancellationToken>());

        (await rig.Desk.PublishAsync("rob", "Again.", CancellationToken.None)).Error.Message.ShouldBe("Nothing drafted would change a figure, so there is nothing to publish.");
        await Should.ThrowAsync<ArgumentException>(() => rig.Desk.PublishAsync(" ", "Note.", CancellationToken.None));
    }

    [Fact]
    public async Task A_publication_that_loses_the_race_or_can_no_longer_be_run_is_refused_with_a_sentence()
    {
        Rig rig = new();
        await rig.Desk.DraftAsync("expert", "stairlift-batteries.stock", 3m, "Why.", "Where.", CancellationToken.None);
        rig.Store.PublishAsync(Arg.Any<PublishedFigures>(), Arg.Any<IReadOnlyList<FigureChange>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(false);

        (await rig.Desk.PublishAsync("rob", "Mine.", CancellationToken.None)).Error.Message.ShouldBe("Somebody else published while you were looking. Read their set and try again.");
        rig.Current.Current.Version.ShouldBe(0);
        await rig.Log.DidNotReceiveWithAnyArgs().RecordHandAsync(default!, default, default!, default, default);

        rig.Drafts.Add(new FigureDraft("no-such-figure", 1m, "Why.", "Where.", "expert", Now));
        (await rig.Desk.PublishAsync("rob", "Mine.", CancellationToken.None)).Error.Code.ShouldBe("parameters.unknown");
    }

    [Fact]
    public async Task An_earlier_set_is_put_back_as_a_new_set_and_what_it_moves_is_recorded()
    {
        Rig rig = new();
        await rig.Desk.DraftAsync("expert", "stairlift-batteries.stock", 3m, "Why.", "Where.", CancellationToken.None);
        await rig.Desk.PublishAsync("rob", "First.", CancellationToken.None);
        await rig.Desk.DraftAsync("expert", "stairlift-batteries.stock", 4m, "Why.", "Where.", CancellationToken.None);
        await rig.Desk.DraftAsync("expert", "demand.drinking-litres", 2.5m, "Why.", "Where.", CancellationToken.None);
        await rig.Desk.PublishAsync("rob", "Second.", CancellationToken.None);
        await rig.Desk.DraftAsync("expert", "demand.pet-water-litres", 3m, "Still in hand.", "Where.", CancellationToken.None);
        rig.Changes.Clear();

        (await rig.Desk.RestoreAsync("expert", 1, "It was wrong.", CancellationToken.None)).Error.Code.ShouldBe("figures.forbidden");
        (await rig.Desk.RestoreAsync("rob", 1, " ", CancellationToken.None)).Error.Code.ShouldBe("figures.text");
        (await rig.Desk.RestoreAsync("rob", 9, "It was wrong.", CancellationToken.None)).Error.Message.ShouldBe("That is not an earlier set of figures.");
        (await rig.Desk.RestoreAsync("rob", 2, "It was wrong.", CancellationToken.None)).Error.Message.ShouldBe("That is not an earlier set of figures.", "the set in force cannot be put back to itself");

        PublishedFigures restored = (await rig.Desk.RestoreAsync("rob", 1, "The second set was wrong.", CancellationToken.None)).Value;

        (restored.Version, restored.Restores, restored.Note).ShouldBe((3, 1, "The second set was wrong."));
        restored.Corrections.ShouldBe(new Dictionary<string, decimal> { ["stairlift-batteries.stock"] = 3m });
        rig.Changes.Select(change => (change.ParameterId, change.Before, change.After)).ShouldBe([("demand.drinking-litres", 2.5m, 3m), ("stairlift-batteries.stock", 4m, 3m)], ignoreOrder: true);
        rig.Changes.ShouldAllBe(change => change.Version == 3 && change.Reason == "Put back to set 1." && change.Source == "The second set was wrong." && change.DraftedBy == "rob");
        rig.Drafts.ShouldHaveSingleItem("a draft in hand is not thrown away by a restore");
        rig.Current.Current["stairlift-batteries.stock"].ShouldBe(3m);
        await rig.Log.Received(1).RecordHandAsync("restore-figures", null, "Set 3 is in force: 2 figures changed. The second set was wrong.", "rob", Arg.Any<CancellationToken>());

        // Nought is the shipped figures.
        PublishedFigures shipped = (await rig.Desk.RestoreAsync("rob", 0, "Back to what shipped.", CancellationToken.None)).Value;
        (shipped.Version, shipped.Restores).ShouldBe((4, 0));
        shipped.Corrections.ShouldBeEmpty();
        (await rig.Desk.RestoreAsync("rob", 0, "Again.", CancellationToken.None)).Error.Message.ShouldBe("That set has the same figures as the one in force.");
        await Should.ThrowAsync<ArgumentException>(() => rig.Desk.RestoreAsync(" ", 1, "Note.", CancellationToken.None));
    }

    [Fact]
    public async Task A_set_stored_before_a_release_narrowed_a_figure_cannot_be_put_back_and_says_why()
    {
        Rig rig = new();
        rig.Sets.Add(new PublishedFigures(1, Now, "rob", "Old.", null, new Dictionary<string, decimal> { ["a-figure-a-release-removed"] = 1m }));
        rig.Sets.Add(new PublishedFigures(2, Now, "rob", "New.", null, new Dictionary<string, decimal>()));
        await rig.Current.ReloadAsync(CancellationToken.None);

        (await rig.Desk.RestoreAsync("rob", 1, "Put it back.", CancellationToken.None)).Error.Code.ShouldBe("parameters.unknown");
    }

    [Fact]
    public async Task A_long_note_is_clipped_in_the_log_and_kept_whole_on_the_set()
    {
        Rig rig = new();
        string note = new('n', 400);
        await rig.Desk.DraftAsync("expert", "stairlift-batteries.stock", 3m, "Why.", "Where.", CancellationToken.None);

        PublishedFigures published = (await rig.Desk.PublishAsync("rob", note, CancellationToken.None)).Value;

        published.Note.Length.ShouldBe(400);
        await rig.Log.Received(1).RecordHandAsync("publish-figures", null, Arg.Is<string>(outcome => outcome.Length == FigureDesk.MaxTextLength), "rob", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_figures_in_force_are_the_shipped_ones_until_a_set_is_read_and_a_correction_the_code_no_longer_accepts_is_dropped_and_named()
    {
        IFigureStore store = Substitute.For<IFigureStore>();
        CurrentFigures current = new(store);
        current.Current.ShouldBeSameAs(ParameterSet.Default);
        current.Published.ShouldBeNull();
        current.PublishedAt.ShouldBeNull();
        current.IsLoaded.ShouldBeFalse();

        store.CurrentAsync(Arg.Any<CancellationToken>()).Returns((PublishedFigures?)null);
        await current.ReloadAsync(CancellationToken.None);
        current.IsLoaded.ShouldBeTrue();
        current.Current.ShouldBeSameAs(ParameterSet.Default);

        // A release removed one figure and narrowed another; the third still stands and is applied.
        store.CurrentAsync(Arg.Any<CancellationToken>()).Returns(new PublishedFigures(4, Now, "rob", "Old.", null, new Dictionary<string, decimal>
        {
            ["a-figure-a-release-removed"] = 1m, ["demand.drinking-litres"] = 99m, ["stairlift-batteries.stock"] = 3m,
        }));
        await current.ReloadAsync(CancellationToken.None);
        current.Current.Version.ShouldBe(4);
        current.PublishedAt.ShouldBe(Now);
        current.Current["stairlift-batteries.stock"].ShouldBe(3m);
        current.Current["demand.drinking-litres"].ShouldBe(3m);
        current.Dropped.ShouldBe([
            "The correction of \"a-figure-a-release-removed\" to 1 is no longer one the plan accepts, and was not applied.",
            "The correction of \"demand.drinking-litres\" to 99 is no longer one the plan accepts, and was not applied.",
        ], ignoreOrder: true);

        // Corrections that each stand and together contradict: none is applied, and the site still runs.
        store.CurrentAsync(Arg.Any<CancellationToken>()).Returns(new PublishedFigures(5, Now, "rob", "Odd.", null, new Dictionary<string, decimal>
        {
            ["price.headtorches.low"] = 50m, ["stairlift-batteries.stock"] = 3m,
        }));
        await current.ReloadAsync(CancellationToken.None);
        current.Current.Version.ShouldBe(5);
        current.Current.Corrections.ShouldBeEmpty();
        current.Dropped.ShouldHaveSingleItem().ShouldBe("A headtorch cannot cost more at its least than at its most. No correction in this set was applied.");

        new ShippedFigures().Current.ShouldBeSameAs(ParameterSet.Default);
        new ShippedFigures().PublishedAt.ShouldBeNull();
        Should.Throw<ArgumentNullException>(() => new CurrentFigures(null!));
    }

    [Fact]
    public void A_plan_is_built_from_the_figures_in_force_when_it_is_built()
    {
        ICurrentFigures figures = Substitute.For<ICurrentFigures>();
        ParameterSet corrected = ParameterSet.Create(2, new Dictionary<string, decimal> { ["water-butt.stock"] = 250m }).Value;
        figures.Current.Returns(ParameterSet.Default, corrected);
        RiskAssessmentPipeline risks = new(ApplicationServiceCollectionExtensions.Assessors());
        PlanBuilder builder = new(ApplicationServiceCollectionExtensions.Rules());
        GeneratePlanHandler handler = new(risks, builder, figures);
        GeneratePlanCommand house = new(4, 2, false, false, false, DwellingType.SemiDetached, 2, HeatingType.GasBoiler, HobType.Electric, true, true, false, false, 60m, FloodRisk.VeryLow);

        Plan before = handler.Handle(house).Value;
        Plan after = handler.Handle(house).Value;

        before.Actions.Single(action => action.Id.Value == "water-butt").Title.ShouldBe("A 210-litre water butt on a downpipe");
        after.Actions.Single(action => action.Id.Value == "water-butt").Title.ShouldBe("A 250-litre water butt on a downpipe");
        after.Profile.Figures.ShouldBeSameAs(corrected);
        new GeneratePlanHandler(risks, builder).Handle(house).Value.Profile.Figures.ShouldBeSameAs(ParameterSet.Default);
        Should.Throw<ArgumentNullException>(() => new GeneratePlanHandler(risks, builder, null!));
    }

    [Fact]
    public void The_desk_checks_its_arguments_and_the_reference_households_are_ones_the_plan_can_build()
    {
        Rig rig = new();
        RiskAssessmentPipeline risks = new(ApplicationServiceCollectionExtensions.Assessors());
        PlanBuilder builder = new(ApplicationServiceCollectionExtensions.Rules());
        ManualClock clock = new(Now);

        Should.Throw<ArgumentNullException>(() => new FigureDesk(null!, rig.Current, rig.Log, risks, builder, clock));
        Should.Throw<ArgumentNullException>(() => new FigureDesk(rig.Store, null!, rig.Log, risks, builder, clock));
        Should.Throw<ArgumentNullException>(() => new FigureDesk(rig.Store, rig.Current, null!, risks, builder, clock));
        Should.Throw<ArgumentNullException>(() => new FigureDesk(rig.Store, rig.Current, rig.Log, null!, builder, clock));
        Should.Throw<ArgumentNullException>(() => new FigureDesk(rig.Store, rig.Current, rig.Log, risks, null!, clock));
        Should.Throw<ArgumentNullException>(() => new FigureDesk(rig.Store, rig.Current, rig.Log, risks, builder, null!));
        ReferenceHouseholds.All.Select(household => household.Name).ShouldBeUnique();
        ReferenceHouseholds.All.ShouldAllBe(household => GeneratePlanHandler.ToProfile(household.Command).IsSuccess);
        rig.Desk.Compare(ParameterSet.Default, ParameterSet.Default).Lines.ShouldBeEmpty();
    }
}
