using BaGet.Core.Configuration;
using BaGet.Core.Entities;
using BaGet.Db;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace BaGet.Tests
{
    public class QueryTestscs
    {
        [Fact]
        public void v()
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

        }
    }
}
