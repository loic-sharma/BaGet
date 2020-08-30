# NuGet.Server

!!! warning
    This page is a work in progress!

[NuGet.Server](https://github.com/NuGet/NuGet.Server) is a lightweight standalone NuGet server. It is strongly recommended that you upgrade to BaGet if you use NuGet.Server. Feel free to open [GitHub issues](https://github.com/loic-sharma/BaGet/issues) if you need help migrating.

* NuGet.Server
    * Only runs on Windows
    * Supports NuGet v2 APIs (missing verified packages, signed packages, etc...)
    * Doesn't support NuGet's v3 APIs
    * Does not scale well
    * Not well documented
    * Not well maintained
* BaGet
    * Cross-platform
    * Supports NuGet v3 APIs

## Migration Guide

You can use the [NuGet.Server migration](../import/nugetserver.md) guide to import your NuGet.Server packages into BaGet.