using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Selficient.Application.Operations;

namespace Selficient.Web.Security;

/// <summary>
/// The Data Protection key ring over the platform's key store, so the operator's signed cookie and
/// the antiforgery tokens survive an app-pool recycle, a redeploy and a second process. Keys are the
/// framework's XML, protected at rest by the encryptor the composition root chooses; a key is
/// written once under its name and never updated.
/// </summary>
public sealed class SqlXmlRepository : IXmlRepository
{
    private readonly IKeyRingStore _store;

    /// <summary>Creates the repository over the store.</summary>
    public SqlXmlRepository(IKeyRingStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<XElement> GetAllElements() => _store.ReadAll().Select(static xml => XElement.Parse(xml)).ToList();

    /// <inheritdoc />
    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);
        string name = string.IsNullOrWhiteSpace(friendlyName) ? Guid.NewGuid().ToString("N") : friendlyName;
        _store.Write(name, element.ToString(SaveOptions.DisableFormatting));
    }
}
