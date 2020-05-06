using System;
using System.Linq;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace BaGet.Core
{
    public class StringArrayComparer : ValueComparer<string[]>
    {
        public static readonly StringArrayComparer Instance = new StringArrayComparer();

        public StringArrayComparer()
            : base(
                (c1, c2) => c1.SequenceEqual(c2),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToArray())
        {
        }
    }
}
