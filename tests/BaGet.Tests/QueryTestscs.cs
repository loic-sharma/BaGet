using BaGet.Core.Configuration;
using BaGet.Core.Entities;
using BaGet.Db;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace BaGet.Tests
{
    public class QueryTestscs
    {
        [Fact]
        public void SqServer2008_Test()
        {
            var options = new DatabaseOptions()
            {
                ConnectionString = "data source=(local);initial catalog=baget_test;persist security info=True;user id=sa;password=asdasd",
                RunMigrationsAtStartup = true,
                Type = DatabaseType.SqlServer,
                SqlDialect = SqlDialect.MsSql2008
            };

            var contextFactory = new ContextFactory(options);
            using (var context = contextFactory.CreateSession())
            {
                var search = context.Query<Package>();
                var criteria = search
                    .OrderBy(s => s.PackageId)
                    .Select(s => s.PackageId)
                    .Distinct();

                search.Where(s => search.Any(q => q.PackageId == q.PackageId)).ToList();
            }
        }

        [Fact]
        public void Sqlite_Incorrect_OrderBy()
        {
            var options = new DatabaseOptions()
            {
                ConnectionString = "Data Source=temp.db",
                RunMigrationsAtStartup = true,
                Type = DatabaseType.Sqlite
            };

            var contextFactory = new ContextFactory(options);
            using (var context = contextFactory.CreateSession())
            {
                var search = context.Query<Package>();
                var packagesResult = search
                    .OrderBy(s => s.PackageId)
                    .Select(s => s.PackageId)
                    .Distinct()
                    //.OrderBy(s => s)
                    .Skip(1)
                    .Take(10)
                    .ToList();
            }
            // 
        }
    }
}

