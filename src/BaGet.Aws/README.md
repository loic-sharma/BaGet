# Extension.BaGet.Aws

Amazon S3 package storage for [BaGet Extension](https://pieroviano.github.io/BaGet/).

```powershell
dotnet add package Extension.BaGet.Aws
```

## Usage

```csharp
services.AddBaGetWebApplication(app => app.AddAwsS3Storage());
```

Active when `Storage:Type` is `AwsS3`.

| Setting | Value |
|---|---|
| `Storage:Region` | AWS region system name, e.g. `eu-west-1` |
| `Storage:Bucket` | Bucket name |
| `Storage:Prefix` | Optional key prefix |
| `Storage:AccessKey`, `Storage:SecretKey` | Static credentials |
| `Storage:UseInstanceProfile` | `true` to use the EC2 instance profile instead of keys |
| `Storage:AssumeRoleArn` | Optional role to assume |

## Links

[Hosting on AWS](https://pieroviano.github.io/BaGet/installation/aws/) · [Source](https://github.com/pieroviano/BaGet)
