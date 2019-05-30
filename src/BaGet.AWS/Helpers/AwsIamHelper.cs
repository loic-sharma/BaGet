using System;
using System.Threading.Tasks;
using Amazon.Runtime;

namespace BaGet.AWS.Helpers
{
    public static class AwsIamHelper
    {
        public static async Task<AWSCredentials> AssumeRoleAsync(AWSCredentials credentials, string roleArn,
            string roleSessionName)
        {
            var assumedCredentials = new AssumeRoleAWSCredentials(credentials, roleArn, roleSessionName);
            var immutableCredentials = await credentials.GetCredentialsAsync();

            if (string.IsNullOrWhiteSpace(immutableCredentials.Token))
            {
                throw new InvalidOperationException($"Unable to assume role {roleArn}");
            }

            return assumedCredentials;
        }
    }
}
