using BaGet.Db;
using System.Configuration;
using Xunit;

namespace BaGet.Tests
{
    public class ConnecttionStringTests
    {
        [Fact]
        public void GetProperty_ConnectionString_DataSource()
        {
            const string expected = "baget.db";
            var result = ConnectionString.GetProperty($"{ConnectionString.DataSourceProperty}={expected}", ConnectionString.DataSourceProperty);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void GetProperty_ConnectionStringWithoutDataSource_Throws(string dataSource)
        {
            Assert.Throws<ConfigurationErrorsException>(() => ConnectionString.GetProperty(dataSource, ConnectionString.DataSourceProperty));
        }
    }
}
