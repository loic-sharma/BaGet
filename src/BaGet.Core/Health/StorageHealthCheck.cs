using Microsoft.Extensions.Diagnostics.HealthChecks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core.Health
{
    public class StorageHealthCheck : IHealthCheck
    {
        private IStorageService Storage { get; set; }

        public StorageHealthCheck(IStorageService storage)
        {
            Storage = storage;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            var randomPath = "StorageHealthCheck" + Guid.NewGuid().ToString("N");
            var contentStream = new MemoryStream();
            var contentBytes = Encoding.ASCII.GetBytes("Health Check");
            var contentType = "text/plain";
            contentStream.Write(contentBytes, 0, contentBytes.Length);
            contentStream.Flush();
            contentStream.Position = 0;

            using(contentStream)
            {
                var putResult = await Storage.PutAsync(randomPath, contentStream, contentType, cancellationToken);
                if (putResult != StoragePutResult.Success)
                {
                    return HealthCheckResult.Unhealthy("Cannot put new storage item");
                }

                try
                {
                    using (var getStream = await Storage.GetAsync(randomPath, cancellationToken))
                    {
                        contentStream.Position = 0;
                        var matches = contentStream.Matches(getStream);
                        if (!matches)
                        {
                            return HealthCheckResult.Unhealthy("Data corruption while reading back storage item");
                        }
                    }
                }
                catch (Exception)
                {
                    return HealthCheckResult.Unhealthy("Error reading back storage item");
                }

                try
                {
                    await Storage.DeleteAsync(randomPath, cancellationToken);
                }
                catch (Exception)
                {
                    return HealthCheckResult.Unhealthy("Error deleting temporary storage item");
                }
            }
            

            return HealthCheckResult.Healthy();
        }
    }
}
