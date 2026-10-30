# Extension.BaGet.Aliyun

Alibaba Cloud (Aliyun) OSS package storage for [BaGet Extension](https://pieroviano.github.io/BaGet/).

```powershell
dotnet add package Extension.BaGet.Aliyun
```

## Usage

```csharp
services.AddBaGetWebApplication(app => app.AddAliyunOssStorage());
```

Active when `Storage:Type` is `AliyunOss`.

| Setting | Value |
|---|---|
| `Storage:Endpoint` | OSS endpoint, e.g. `oss-cn-hangzhou.aliyuncs.com` |
| `Storage:Bucket` | Bucket name |
| `Storage:Prefix` | Optional key prefix |
| `Storage:AccessKey`, `Storage:AccessKeySecret` | Credentials |

## Links

[Hosting on Alibaba Cloud](https://pieroviano.github.io/BaGet/installation/aliyun/) · [Source](https://github.com/pieroviano/BaGet)
