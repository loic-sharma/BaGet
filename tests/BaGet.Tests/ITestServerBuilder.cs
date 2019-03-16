using Microsoft.AspNetCore.TestHost;

namespace BaGet.Tests
{
    public interface ITestServerBuilder
    {
        TestServer Build();
    }
}
