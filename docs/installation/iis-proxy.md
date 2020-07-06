# Windows IIS Proxy

With a few extra steps you can run BaGet behind a Windows IIS proxy. This has many benefits, including automatic restarts on reboots.

## IIS setup

1. Install the [.NET Core Runtime](https://dotnet.microsoft.com/download) on the web server.
1. Copy the BaGet directory over to your hosting area such as `C:\Inetpub\wwwroot\BaGet`
1. Using IIS Manager, create a new Application Pool:

    - Name = `BaGetAppPool` (can be whatever you want)
    - .NET CLR version = No Managed Code
    - Managed Pipeline Mode = Integrated
    - Start application pool immediately = checked

1. Using IIS Manager, create a new web site:
    - Choose your site name and physical path
    - Choose `BaGetAppPool` as the application pool
    - In the Binding area, enter the default BaGet port of 5000

## BaGet folder permissions

You **may** need to give special permissions to the top-level BaGet folder so that the app can persist its state. This is necessary as the Application Pools' identity is a virtual account that isn't recognized by the Windows User Management Console. For more information, please refer to ASP.NET Core's documentation:

* [Application Pools](https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-2.2#application-pools)
* [Application Pool Identity](https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-2.2#application-pool-identity)

## Alternative storage path

!!! info
    Virtual Directories do not work with IIS and Kestrel. For more information, please refer to [ASP.NET Core's documentation](https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-2.2#virtual-directories).

Ensure that the configuration's storage `Path` has the appropriate forward slashes:

```javascript
...
  "Storage": {
    "Type": "FileSystem",
    "Path": "C://AnotherFolder/Packages"
  },
...
```

Note that you will need to adjust folder permissions if the `Path` is created outside of the BaGet top-level directory. See the [BaGet Folder Permissions](#baget-folder-permissions).


## IIS server options

Settings such as the maximum package size can be configured for IIS in the appsettings.json file - see [IIS Server Options](../configuration.md#iis-server-options).