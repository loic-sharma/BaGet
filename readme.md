# BaGet :baguette_bread:

[![Build Status](https://sharml.visualstudio.com/BaGet/_apis/build/status/loic-sharma.BaGet)](https://sharml.visualstudio.com/BaGet/_build/latest?definitionId=2)

A lightweight [NuGet service](https://docs.microsoft.com/en-us/nuget/api/overview) implementation.

## Getting Started

1. Install [.NET Core SDK](https://www.microsoft.com/net/download)
2. Run `dotnet tool install --global BaGet`
3. Start the service with `baget`
4. Push your first package using `dotnet nuget push -s http://localhost:5000/v3/index.json newtonsoft.json.11.0.2.nupkg`
4. Browse `http://localhost:5000/` in your browser

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