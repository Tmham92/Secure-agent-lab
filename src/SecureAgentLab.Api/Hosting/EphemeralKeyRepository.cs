using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace SecureAgentLab.Api.Hosting;
// ASP.NET's key-ring warmup must also remain process-local; credentials do not use this key ring.
internal sealed class EphemeralKeyRepository : IXmlRepository
{
    private readonly List<XElement> _elements = [];
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        lock (_elements)
        {
            return [.. _elements.Select(e => new XElement(e))];
        }
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        lock (_elements)
        {
            _elements.Add(new XElement(element));
        }
    }
}
