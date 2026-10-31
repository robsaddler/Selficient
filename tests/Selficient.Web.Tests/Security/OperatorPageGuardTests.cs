using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using NSubstitute;
using Selficient.Application.Security;
using Selficient.Web.Pages.Admin;
using Selficient.Web.Security;

namespace Selficient.Web.Tests.Security;

/// <summary>
/// What the operator's pages do when the ground moves under them: an account deleted between the
/// cookie being issued and the page being asked for, a reviewer switched off between two requests,
/// and an enrolment posted without the seed it was given. None of these can be reached through a
/// browser, and all of them are a redirect rather than a crash.
/// </summary>
public sealed class OperatorPageGuardTests
{
    private static readonly DateTimeOffset Evening = new(2026, 10, 31, 20, 0, 0, TimeSpan.Zero);

    private static OperatorAccount Account(bool mustChange = false) =>
        new(7, "rob", "Rob", PasswordHasher.Hash("the lantern on the kitchen table"), Evening, [1], Evening, 0, [2], Guid.NewGuid(), true, mustChange, 0, null, null, Evening);

    [Fact]
    public async Task The_enrolment_sends_a_caller_with_no_account_back_to_the_sign_in()
    {
        IOperatorStore store = Substitute.For<IOperatorStore>();
        store.FindAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns((OperatorAccount?)null);
        EnrolModel page = new(store, SignIn(store), Options.Create(new OperatorOptions()), TimeProvider.System);
        Bind(page, Principal(7));

        (await page.OnGetAsync(CancellationToken.None)).ShouldBeOfType<RedirectToPageResult>().PageName.ShouldBe("/Admin/SignIn");
        (await page.OnPostAsync(CancellationToken.None)).ShouldBeOfType<RedirectToPageResult>().PageName.ShouldBe("/Admin/SignIn");
    }

    [Fact]
    public async Task The_enrolment_refuses_a_post_that_carries_no_seed()
    {
        IOperatorStore store = Substitute.For<IOperatorStore>();
        store.FindAsync(7L, Arg.Any<CancellationToken>()).Returns(Account());
        EnrolModel page = new(store, SignIn(store), Options.Create(new OperatorOptions()), TimeProvider.System) { Secret = "  ", Code = "123456" };
        Bind(page, Principal(7));

        (await page.OnPostAsync(CancellationToken.None)).ShouldBeOfType<RedirectToPageResult>().PageName.ShouldBe("/Admin/SignIn");
    }

    [Fact]
    public async Task A_page_held_by_a_principal_with_no_number_finds_no_account()
    {
        IOperatorStore store = Substitute.For<IOperatorStore>();
        EnrolModel enrol = new(store, SignIn(store), Options.Create(new OperatorOptions()), TimeProvider.System);
        PasswordModel password = new(store, Options.Create(new OperatorOptions()), TimeProvider.System);
        Bind(enrol, new ClaimsPrincipal(new ClaimsIdentity()));
        Bind(password, new ClaimsPrincipal(new ClaimsIdentity()));

        (await enrol.OnGetAsync(CancellationToken.None)).ShouldBeOfType<RedirectToPageResult>();
        (await password.OnGetAsync(CancellationToken.None)).ShouldBeOfType<RedirectToPageResult>();
        (await password.OnPostAsync(CancellationToken.None)).ShouldBeOfType<RedirectToPageResult>();
        await store.DidNotReceive().FindAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_password_page_sends_a_caller_with_no_account_back_to_the_sign_in()
    {
        IOperatorStore store = Substitute.For<IOperatorStore>();
        store.FindAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns((OperatorAccount?)null);
        PasswordModel page = new(store, Options.Create(new OperatorOptions()), TimeProvider.System);
        Bind(page, Principal(7));

        (await page.OnGetAsync(CancellationToken.None)).ShouldBeOfType<RedirectToPageResult>().PageName.ShouldBe("/Admin/SignIn");
        (await page.OnPostAsync(CancellationToken.None)).ShouldBeOfType<RedirectToPageResult>().PageName.ShouldBe("/Admin/SignIn");
    }

    [Fact]
    public async Task A_password_change_with_nothing_typed_is_refused_by_the_policy()
    {
        IOperatorStore store = Substitute.For<IOperatorStore>();
        store.FindAsync(7L, Arg.Any<CancellationToken>()).Returns(Account());
        PasswordModel page = new(store, Options.Create(new OperatorOptions()), TimeProvider.System) { Current = "the lantern on the kitchen table" };
        Bind(page, Principal(7));

        (await page.OnPostAsync(CancellationToken.None)).ShouldBeOfType<PageResult>();

        page.Refusal.ShouldNotBeNull();
        await store.DidNotReceive().SetPasswordAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task With_the_reviewer_switched_off_the_sign_in_is_not_there_on_either_verb()
    {
        IOperatorStore store = Substitute.For<IOperatorStore>();
        SignInModel page = new(SignIn(store), Options.Create(new OperatorOptions { Enabled = false }));
        Bind(page, new ClaimsPrincipal(new ClaimsIdentity()));

        page.OnGet().ShouldBeOfType<NotFoundResult>();
        (await page.OnPostAsync(CancellationToken.None)).ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public void An_operator_already_signed_in_is_sent_on_from_the_sign_in()
    {
        IOperatorStore store = Substitute.For<IOperatorStore>();
        SignInModel page = new(SignIn(store), Options.Create(new OperatorOptions()));
        Bind(page, OperatorAuth.Principal(Account(), pending: false));

        page.OnGet().ShouldBeOfType<RedirectToPageResult>().PageName.ShouldBe("/Admin/Catalogue");
    }

    [Fact]
    public void An_operator_half_way_through_sees_the_sign_in_again_rather_than_the_reviewer()
    {
        IOperatorStore store = Substitute.For<IOperatorStore>();
        SignInModel page = new(SignIn(store), Options.Create(new OperatorOptions()));
        Bind(page, OperatorAuth.Principal(Account(), pending: true));

        page.OnGet().ShouldBeOfType<PageResult>("the password step is done and the code is not, so there is nowhere to send them yet");
    }

    [Fact]
    public void A_caller_carrying_no_identity_at_all_gets_the_sign_in()
    {
        IOperatorStore store = Substitute.For<IOperatorStore>();
        SignInModel page = new(SignIn(store), Options.Create(new OperatorOptions()));
        Bind(page, new ClaimsPrincipal());

        page.OnGet().ShouldBeOfType<PageResult>();
    }

    private static OperatorSignIn SignIn(IOperatorStore store) =>
        new(store, Substitute.For<ISecretProtector>(), TimeProvider.System);

    private static ClaimsPrincipal Principal(long id) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString(System.Globalization.CultureInfo.InvariantCulture))], "test"));

    private static void Bind(PageModel page, ClaimsPrincipal user)
    {
        DefaultHttpContext http = new() { User = user };
        page.PageContext = new PageContext(new ActionContext(http, new RouteData(), new Microsoft.AspNetCore.Mvc.RazorPages.PageActionDescriptor(), new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary()))
        {
            ViewData = new ViewDataDictionary<object>(new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(), new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary()),
        };
    }
}
