# BaGet :baguette_bread:

A lightweight NuGet service implementation.

## Getting Started

Run:

```
$ cd .\src\BaGet
$ dotnet run
Using launch settings from D:\Code\BaGet\src\BaGet\Properties\launchSettings.json...
Hosting environment: Development
Content root path: D:\Code\BaGet\src\BaGet
Now listening on: http://localhost:50561
Application started. Press Ctrl+C to shut down.
```

You can now access the [service index](https://docs.microsoft.com/en-us/nuget/api/overview#service-index) at http://localhost:50561/v3/index.json.

BaGet automatically indexes packages nuget.org as its upstream source. For example, accessing http://localhost:50561/v3/registration/newtonsoft.json/11.0.1.json will automatically index [Newtonsoft.Json v11.0.1](https://www.nuget.org/packages/Newtonsoft.Json/11.0.1).