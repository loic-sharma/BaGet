# Extension.BaGet.Gcp

Google Cloud Storage package storage for [BaGet Extension](https://pieroviano.github.io/BaGet/).

```powershell
dotnet add package Extension.BaGet.Gcp
```

## Usage

```csharp
services.AddBaGetWebApplication(app => app.AddGoogleCloudStorage());
```

Active when `Storage:Type` is `GoogleCloud`.

| Setting | Value |
|---|---|
| `Storage:BucketName` | Bucket name |

Credentials come from [Application Default Credentials](https://cloud.google.com/docs/authentication/application-default-credentials)
(e.g. `GOOGLE_APPLICATION_CREDENTIALS`).

## Links

[Hosting on Google Cloud](https://pieroviano.github.io/BaGet/installation/gcp/) · [Source](https://github.com/pieroviano/BaGet)
