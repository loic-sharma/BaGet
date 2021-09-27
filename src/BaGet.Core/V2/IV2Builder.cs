using System.Collections.Generic;
using System.Xml.Linq;

namespace BaGet.Core
{
    public interface IV2Builder
    {
        XElement BuildIndex();
        XElement BuildPackages(IReadOnlyList<Package> packages);
        XElement BuildPackage(Package package);
    }
}
