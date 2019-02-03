# Windows IIS Proxy

Running BaGet behind IIS as a proxy on Windows may require a few extra steps, however it may be advantageous because IIS will automatically manage restarting the server for you on reboots, etc.

## IIS Setup

Ensure that the required [.Net Core runtime](https://dotnet.microsoft.com/download) is installed on the web server (currently known as the Windows Hosting Bundle installer).

Copy the BaGet directory over to your hosting area such as `C:\Inetpub\wwwroot\BaGet`

Using IIS Manager, create a new Application Pool:

- Name = BaGetAppPool (can be whatever you want)
- .Net CLR version = No Managed Code
- Managed Pipeline Mode = Integrated
- Start application pool immediately = checked

Using IIS Manager, create a new web site:

- Choose your site name and physical path
- Choose BaGetAppPool as the application pool
- In the Binding area, enter the default BaGet port of 5000

## BaGet Folder permissions

In order for the app to create the appropriate NuGet package folder, as well as create various files (SqLite database for example) you **may** need to give special rights to the top level BaGet folder. 

Read under, "Application Pools" and, "Application Pool Identity" [here](https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-2.2). Basically the identity used in the app pool isn't a real user account and doesn't show up in the Windows User Management Console.

## Alternative NuGet package Storage Paths

Note that Virtual Directories will not work with IIS and Kestrel. Read more about that [here](https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-2.2)

Ensure that the configuration storage Path uses approprite forward slashes in the settings such as:

```javascript
...
  "Storage": {
    "Type": "FileSystem",
    "Path": "C://AnotherFolder/Packages"
  },
...
```

Note if a folder is created outside of the BaGet top level directory, you will definitely need to adjust folder permissions stated above.