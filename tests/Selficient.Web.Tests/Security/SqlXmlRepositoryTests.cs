using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Selficient.Application.Operations;
using Selficient.TestKit;
using Selficient.Web.Security;

namespace Selficient.Web.Tests.Security;

public sealed class SqlXmlRepositoryTests
{
    [Fact]
    public void Keys_go_to_the_store_as_their_xml_under_their_name_or_a_fresh_one()
    {
        IKeyRingStore store = Substitute.For<IKeyRingStore>();
        store.ReadAll().Returns(["<key id=\"a\" />", "<key id=\"b\"><creationDate>2026-10-31</creationDate></key>"]);
        SqlXmlRepository repository = new(store);
        XElement element = new("key", new XAttribute("id", "c"));

        IReadOnlyCollection<XElement> all = repository.GetAllElements();
        repository.StoreElement(element, "key-c");
        repository.StoreElement(element, " ");

        all.Select(e => e.Attribute("id")!.Value).ShouldBe(["a", "b"]);
        all.Last().Element("creationDate")!.Value.ShouldBe("2026-10-31");
        store.Received().Write("key-c", "<key id=\"c\" />");
        store.Received().Write(Arg.Is<string>(name => name.Length == 32 && name != "key-c"), "<key id=\"c\" />");
        Should.Throw<ArgumentNullException>(() => repository.StoreElement(null!, "x"));
        Should.Throw<ArgumentNullException>(() => new SqlXmlRepository(null!));
    }

    [Fact]
    public void The_host_keeps_its_key_ring_in_the_platform_database()
    {
        using SelficientWebApplicationFactory factory = new();

        IXmlRepository? repository = factory.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository;
        IReadOnlyList<string> keys = factory.Services.GetRequiredService<IKeyRingStore>().ReadAll();

        repository.ShouldBeOfType<SqlXmlRepository>();
        keys.Count.ShouldBeGreaterThanOrEqualTo(0, "the ring is read from the database the host migrated");
    }
}
