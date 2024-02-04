# BaGet service for Linux

This is the BaGet light weight NuGet server built as a Linux service.

## Building for Linux

To build for your Linux platform:

* Create a new Publishing Profile
* Select FolderProfile
* Set the Target Runtime to the required Linux runtime
* Publish.

This will create an application that can be directly run on Linux.
That is you can directly call ./BaGet on the target Linux architecture
and the service will start in the same way that it starts from the
commandline in Windows or in the Visual Studio debugger.

## Installation

Installation of BaGet on Linux comprises the following steps:

* Copy the BaGet build to /usr/share/baget
* Make the BaGet application executable
* Edit the appsettings.json to your needs
* Edit /usr/share/baget/baget.service to your needs
* Copy the baget.service file to /etc/systemd/system/baget.service
* Enable the baget service
* Start the baget service

```
$chmod 0755 /usr/share/baget/BaGet
$vi /usr/share/baget/appsettings.json
$vi /usr/share/baget/baget.service
$sudo cp /usr/share/baget/baget.service /etc/systemd/system/baget.service
$sudo systemctl enable baget
$sudo systemctl start baget
```
