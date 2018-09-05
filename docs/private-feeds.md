# Private Feeds

!!! warning
    This page is a work in progress! See [this pull request](https://github.com/loic-sharma/BaGet/pull/69) for more information.

!!! tip
    Refer to the [API key](configuration/#requiring-an-api-key) documentation if you'd like to require an API key to push packages.

A private feed requires users to authenticate before accessing packages. BaGet supports the following authentication providers:

* [Azure Active Directory](#azure-active-directory)

## Azure Active Directory

### Setup

* Build Azure Active Directory apps
* Register the users on the tenant
* Get the tenant id, app ids, etc..

### Configuration

* Configure BaGet
* Configure the NuGet plugin