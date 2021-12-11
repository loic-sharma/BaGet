using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace BaGet.Core.Tests.Services
{
    public class PackageDatabaseTests
    {
        public class AddAsync : FactsBase
        {
            [Fact]
            public async Task ReturnsPackageAlreadyExistsOnUniqueConstraintViolation()
            {
                await Task.Yield();
            }

            [Fact]
            public async Task AddsPackage()
            {
                // TODO: Returns Success
                // TODO: Adds package
                await Task.Yield();
            }
        }

        public class ExistsAsync : FactsBase
        {
            [Theory]
            [InlineData("Package", "1.0.0", true)]
            [InlineData("Package", "1.0.0.0", true)]
            [InlineData("Unlisted.Package", "1.0.0", true)]
            [InlineData("Fake.Package", "1.0.0", false)]
            public async Task ReturnsTrueIfPackageExists(string packageId, string packageVersion, bool exists)
            {
                System.Console.WriteLine($"TODO: {packageId} {packageVersion} {exists}");
                await Task.Yield();
            }
        }

        public class FindAsync : FactsBase
        {
            [Fact]
            public async Task ReturnsEmptyListIfPackageDoesNotExist()
            {
                // Ensure the context has packages with a different id/version
                await Task.Yield();
            }

            [Theory]
            [MemberData(nameof(ReturnsPackagesData))]
            public async Task ReturnsPackages(string packageId, string packageVersion, bool includeUnlisted, bool exists)
            {
                // TODO: Ensure resulting versions are normalized.
                System.Console.WriteLine($"TODO: {packageId} {packageVersion} {includeUnlisted} {exists}");
                await Task.Yield();
            }

            public static IEnumerable<object[]> ReturnsPackagesData()
            {
                object[] ReturnsPackagesHelper(string packageId, string packageVersion, bool includeUnlisted, bool exists)
                {
                    return new object[] { packageId, packageVersion, includeUnlisted, exists };
                }

                // A package that doesn't exist should never be returned
                yield return ReturnsPackagesHelper("Fake.Package", "1.0.0", includeUnlisted: true, exists: false);

                // A listed package should be returned regardless of the "includeUnlisted" parameter
                yield return ReturnsPackagesHelper("Package", "1.0.0", includeUnlisted: false, exists: true);
                yield return ReturnsPackagesHelper("Package", "1.0.0", includeUnlisted: true, exists: true);

                // The inputted package version should be normalized
                yield return ReturnsPackagesHelper("Package", "1.0.0.0", includeUnlisted: false, exists: true);

                // Unlisted packages should only be returned if "includeUnlisted" is true
                yield return ReturnsPackagesHelper("Unlisted.Package", "1.0.0", includeUnlisted: false, exists: false);
                yield return ReturnsPackagesHelper("Unlisted.Package", "1.0.0", includeUnlisted: true, exists: true);
            }
        }

        public class FindOrNullAsync : FactsBase
        {
            [Fact]
            public async Task ReturnsNullIfPackageDoesNotExist()
            {
                await Task.Yield();
            }

            [Theory]
            [MemberData(nameof(ReturnsPackageData))]
            public async Task ReturnsPackage(string packageId, string packageVersion, bool includeUnlisted, bool exists)
            {
                // TODO: Ensure resulting versions are normalized.
                System.Console.WriteLine($"TODO: {packageId} {packageVersion} {includeUnlisted} {exists}");
                await Task.Yield();
            }

            public static IEnumerable<object[]> ReturnsPackageData()
            {
                object[] ReturnsPackageHelper(string packageId, string packageVersion, bool includeUnlisted, bool exists)
                {
                    return new object[] { packageId, packageVersion, includeUnlisted, exists };
                }

                // A package that doesn't exist should never be returned
                yield return ReturnsPackageHelper("Fake.Package", "1.0.0", includeUnlisted: true, exists: false);

                // A listed package should be returned regardless of the "includeUnlisted" parameter
                yield return ReturnsPackageHelper("Package", "1.0.0", includeUnlisted: false, exists: true);
                yield return ReturnsPackageHelper("Package", "1.0.0", includeUnlisted: true, exists: true);

                // The inputted package version should be normalized
                yield return ReturnsPackageHelper("Package", "1.0.0.0", includeUnlisted: false, exists: true);

                // Unlisted packages should only be returned if "includeUnlisted" is true
                yield return ReturnsPackageHelper("Unlisted.Package", "1.0.0", includeUnlisted: false, exists: false);
                yield return ReturnsPackageHelper("Unlisted.Package", "1.0.0", includeUnlisted: true, exists: true);
            }
        }

        public class UnlistPackageAsync : FactsBase
        {
            [Fact]
            public async Task ReturnsFalseIfPackageDoesNotExist()
            {
                await Task.Yield();
            }

            [Theory]
            [InlineData(false)]
            [InlineData(true)]
            public async Task UnlistsPackage(bool listed)
            {
                // TODO: This should succeed if the package is unlisted.
                // TODO: Returns true
                System.Console.WriteLine($"TODO: {listed}");
                await Task.Yield();
            }
        }

        public class RelistPackageAsync : FactsBase
        {
            [Fact]
            public async Task ReturnsFalseIfPackageDoesNotExist()
            {
                await Task.Yield();
            }

            [Theory]
            [InlineData(false)]
            [InlineData(true)]
            public async Task RelistsPackage(bool listed)
            {
                // TODO: This should succeed if the package is listed.
                // TODO: Return true
                System.Console.WriteLine($"TODO: {listed}");
                await Task.Yield();
            }
        }

        public class AddDownloadAsync : FactsBase
        {
            [Fact]
            public async Task ReturnsFalseIfPackageDoesNotExist()
            {
                await Task.Yield();
            }

            [Fact]
            public async Task IncrementsPackageDownloads()
            {
                await Task.Yield();
            }
        }

        public class HardDeletePackageAsync : FactsBase
        {
            [Fact]
            public async Task ReturnsFalseIfPackageDoesNotExist()
            {
                await Task.Yield();
            }

            [Fact]
            public async Task DeletesPackage()
            {
                await Task.Yield();
            }
        }

        public class FactsBase
        {
            protected readonly Mock<IContext> _context;
            protected readonly PackageDatabase _target;

            public FactsBase()
            {
                _context = new Mock<IContext>();
                _target = new PackageDatabase(_context.Object);
            }
        }
    }
}
