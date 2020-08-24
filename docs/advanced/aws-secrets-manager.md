# Using AWS Secrets Manager to configure BaGet

!!! warning
    This page is a work in progress!

## Getting started

Refer to the [AWS Secrets Manager documentation](https://docs.aws.amazon.com/secretsmanager/latest/userguide/tutorials_basic.html) to create the required resource.
This guide will use as an example a secret store under the path `/staging/bagetconfig`
The secret value should be a valid JSON document having the same schema as the `appsettings.json` . The [Configuration](../configuration.md) page explains the available keys.

Secret value for `/staging/bagetconfig`
```
{
    "ApiKey": "NUGET-SERVER-API-KEY",
    "Database": {
        "Type": "MySql",
        "ConnectionString": "Server=myServerAddress;Database=myDataBase;Uid=myUsername;Pwd=myPassword;"
    },
    "Storage": {
        "Type": "AwsS3",
        "Region": "us-east-1",
        "Bucket": "foo"
    }
}
```

## Tell BaGet where to find the secrets

Two configuration keys are required. They can be passed either on the command-line or through environment variables.

* BAGET_AWS_SECRETS_PATH
* BAGET_AWS_SECRETS_REGION

The first, `BAGET_AWS_SECRETS_PATH`, should be the path to the secret; in this example the value would be `/staging/bagetconfig`.

The second, `BAGET_AWS_SECRETS_REGION` is the AWS region where the secret is stored. For example, `us-east-1`.

## Credentials to access AWS ressources

The AWS SDK is called without specifying credentials. The default credentials found on the computer are used.
From the [AWS documentation](https://docs.aws.amazon.com/sdkfornet/v3/apidocs/items/SecretsManager/MSecretsManagerctorSecretsManagerConfig.html) : `the credentials loaded from the application's default configuration, and if unsuccessful from the Instance Profile service on an EC2 instance.`
On a production server, this will be the EC2 instance profile, and on a developer's workstation the default AWS Profile.
