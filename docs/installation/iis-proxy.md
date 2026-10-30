# Windows IIS Proxy

With a few extra steps you can run BaGet Extension behind a Windows IIS proxy. This has many benefits, including automatic restarts on reboots.

## IIS setup

1. Install the [.NET 10 Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/10.0) on the web server, then restart IIS. Use a published build: `dotnet publish` generates the `web.config`.
1. Copy the BaGet Extension directory over to your hosting area such as `C:\Inetpub\wwwroot\BaGet`
1. Using IIS Manager, create a new Application Pool:

    - Name = `BaGetAppPool` (can be whatever you want)
    - .NET CLR version = No Managed Code
    - Managed Pipeline Mode = Integrated
    - Start application pool immediately = checked

1. Using IIS Manager, create a new web site:
    - Choose your site name and physical path
    - Choose `BaGetAppPool` as the application pool
    - In the Binding area, enter the host name and port you want to serve. Under IIS in-process hosting the Kestrel endpoint (`localhost:6060`) in `appsettings.json` is not used.

## BaGet Extension folder permissions

You **may** need to give special permissions to the top-level BaGet Extension folder so that the app can persist its state. This is necessary as the Application Pools' identity is a virtual account that isn't recognized by the Windows User Management Console. For more information, please refer to ASP.NET Core's documentation:

* [Application Pools](https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0#application-pools)
* [Application Pool Identity](https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0#application-pool-identity)

## Alternative storage path

!!! info
    Virtual Directories do not work with IIS and Kestrel. For more information, please refer to [ASP.NET Core's documentation](https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0#virtual-directories).

Ensure that the configuration's storage `Path` has the appropriate forward slashes:

```javascript
...
  "Storage": {
    "Type": "FileSystem",
    "Path": "C://AnotherFolder/Packages"
  },
...
```

Note that you will need to adjust folder permissions if the `Path` is created outside of the BaGet Extension top-level directory. See the [BaGet Extension Folder Permissions](#baget-extension-folder-permissions).

## API key

With an empty `ApiKey` and [accounts](../configuration.md#accounts-package-owners-and-api-keys) on (the default), a push needs a user API key from `/account/apikeys`; or set `ApiKey` and pass it with `-k`. See [Require an API key](../configuration.md#require-an-api-key).

## Logging under IIS

- Logs go to `logs/baget-YYYYMMDD.log` under the site folder; give the application pool identity write access to it (see
  [folder permissions](#baget-extension-folder-permissions)) or point `Serilog:WriteTo:File:Args:path` at a writable folder.
  A file that cannot be written is reported on stderr (enable `stdoutLogEnabled` in `web.config` to capture it), not silently dropped.
- Warnings and errors, startup failures included, also go to the Windows **Application** event log (source `.NET Runtime`),
  without administrator rights. Turn that off with `Serilog:EventLog:Enabled=false`.
- To switch a sink off, set `Serilog:WriteTo:File:Enabled` to `false` (do not rely on an empty environment variable).
  See [Logging](../configuration.md#logging).

## IIS server options

Settings such as the maximum package size can be configured for IIS in the appsettings.json file - see [IIS Server Options](../configuration.md#iis-server-options).