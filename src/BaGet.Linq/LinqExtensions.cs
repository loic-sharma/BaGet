using NHibernate.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Linq
{
    public static class LinqExtensions
    {
        public static Task<int> CountAsync<TSource>(this IQueryable<TSource> source, CancellationToken cancellationToken = default) => LinqExtensionMethods.CountAsync(source, cancellationToken);
        public static Task<List<TSource>> ToListAsync<TSource>(this IQueryable<TSource> source, CancellationToken cancellationToken = default) => LinqExtensionMethods.ToListAsync(source, cancellationToken);
    }
}
