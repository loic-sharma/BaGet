# Extension.BaGet.Azure

Azure providers for [BaGet Extension](https://pieroviano.github.io/BaGet/): Blob Storage, Table Storage and Azure Search.

```powershell
dotnet add package Extension.BaGet.Azure
```

## Usage

```csharp
services.AddBaGetWebApplication(app =>
{
    app.AddAzureTableDatabase();
    app.AddAzureBlobStorage();
    app.AddAzureSearch();
});
```

| Provider | Active when | Settings |
|---|---|---|
| `AddAzureTableDatabase` | `Database:Type` = `AzureTable` | `Database:ConnectionString` |
| `AddAzureBlobStorage` | `Storage:Type` = `AzureBlobStorage` | `Storage:ConnectionString`, or `Storage:AccountName` + `Storage:AccessKey`; `Storage:Container`; `Storage:DownloadUriExpiry` |
| `AddAzureSearch` | `Search:Type` = `AzureSearch` | `Search:AccountName`, `Search:ApiKey` |

With `Database:Type` = `AzureTable`, `Search:Type` = `Database` searches Table Storage.

## Links

[Hosting on Azure](https://pieroviano.github.io/BaGet/installation/azure/) · [Source](https://github.com/pieroviano/BaGet)
