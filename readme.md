# BaGet :baguette_bread:

[![Build Status](https://sharml.visualstudio.com/BaGet/_apis/build/status/loic-sharma.BaGet)](https://sharml.visualstudio.com/BaGet/_build/latest?definitionId=2)

A lightweight [NuGet service](https://docs.microsoft.com/en-us/nuget/api/overview) implementation.

## Getting Started

1. Install [.NET Core SDK](https://www.microsoft.com/net/download)
2. Download and extract [BaGet's latest release](https://github.com/loic-sharma/BaGet/releases)
3. Start the service with `dotnet BaGet.dll`
4. Push your first package using `dotnet nuget push -s http://localhost:5000/v3/index.json newtonsoft.json.11.0.2.nupkg`
5. Browse `http://localhost:5000/` in your browser

For more information, please refer to [our documentation](https://loic-sharma.github.io/BaGet/).

## Features

* Cross-platform
* [Dockerized](https://loic-sharma.github.io/BaGet/#running-baget-on-docker)
* [Cloud ready](https://loic-sharma.github.io/BaGet/cloud/azure/)
* [Supports read-through caching](https://loic-sharma.github.io/BaGet/configuration/#enabling-read-through-caching)
* Can index the entirety of nuget.org. See [this documentation](https://loic-sharma.github.io/BaGet/tools/mirroring/)
* Coming soon: Supports [private feeds](https://loic-sharma.github.io/BaGet/private-feeds/)
* And more!

Stay tuned, more features are planned!

## Develop

1. Install:
    1. [.NET Core SDK](https://www.microsoft.com/net/download)
    2. [Node.js](https://nodejs.org/)
    3. [Yarn](https://yarnpkg.com/en/docs/install)
2. Run `git clone https://github.com/loic-sharma/BaGet.git`
3. Launch the backend:
    1. Navigate to `.\BaGet\src\BaGet`
    2. Start the backend with `dotnet run`
4. Launch the frontend:
    1. Navigate to `.\BaGet\src\BaGet.UI`
    2. Run `yarn install`
    3. Start the frontend with `yarn develop`
5. Open the URL `http://localhost:5000/v3/index.json` in your browser