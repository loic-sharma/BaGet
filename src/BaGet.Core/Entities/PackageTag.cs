using System.Collections.Generic;

namespace BaGet.Core.Entities
{
    public class PackageTag : EntityBase
    {
        public virtual string Tag { get; set; }
        public virtual IList<Package> Packages { get; set; }

        public PackageTag()
        {
            Packages = new List<Package>();
        }

        public PackageTag(string tag) : this()
        {
            Tag = tag;
        }
    }
}
