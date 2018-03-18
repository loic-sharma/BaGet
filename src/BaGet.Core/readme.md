# BaGet.Core

Contains Entity Framework Core entities. Regenerate migrations with:

```
rm ..\BaGet\baget.db
dotnet ef migrations remove --startup-project ..\BaGet\
dotnet ef migrations add Initial --startup-project ..\BaGet\

dotnet ef database update --startup-project ..\BaGet\
```