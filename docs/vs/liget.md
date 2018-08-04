# LiGet

!!! warning
    This page is a work in progress!

[LiGet](https://github.com/ai-traders/liget) is a NuGet server created with a linux-first approach.

* LiGet
    * Strong support for Paket
    * Only supports NuGet's v2 APIs (missing verified packages, signed packages, etc...)
    * Stores all packages' metadata using a single JSON file
* BaGet
    * Supports NuGet's v3 APIs
    * Stores packages' metadata in a database
    * Capable of ingesting all packages on nuget.org
    * Can run on Azure