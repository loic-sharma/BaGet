# BaGet :baguette_bread:

![Build status](https://img.shields.io/github/workflow/status/loic-sharma/BaGet/Build/main) [![Discord](https://img.shields.io/discord/889377258068930591)](https://discord.gg/MWbhpf66mk) [![Twitter](https://img.shields.io/twitter/follow/bagetapp?label=Follow)](https://twitter.com/bagetapp)

A lightweight [NuGet](https://docs.microsoft.com/en-us/nuget/what-is-nuget) and [Symbol](https://docs.microsoft.com/en-us/windows/desktop/debug/symbol-servers-and-symbol-stores) server.

<p align="center">
  <img width="100%" src="https://user-images.githubusercontent.com/737941/50140219-d8409700-0258-11e9-94c9-dad24d2b48bb.png">
</p>

## Getting Started

1. Install [.NET Core SDK](https://www.microsoft.com/net/download)
2. Download and extract [BaGet's latest release](https://github.com/loic-sharma/BaGet/releases)
3. Start the service with `dotnet BaGet.dll`
4. Browse `http://localhost:5000/` in your browser

For more information, please refer to [our documentation](https://loic-sharma.github.io/BaGet/).

## Features

* Cross-platform
* [Dockerized](https://loic-sharma.github.io/BaGet/installation/docker/)
* [Cloud ready](https://loic-sharma.github.io/BaGet/installation/azure/)
* [Supports read-through caching](https://loic-sharma.github.io/BaGet/configuration/#enable-read-through-caching)
* Can index the entirety of nuget.org. See [this documentation](https://loic-sharma.github.io/BaGet/configuration/#enable-read-through-caching)
* Coming soon: Supports [private feeds](https://loic-sharma.github.io/BaGet/configuration/#private-feeds)
* And more!

Stay tuned, more features are planned!

## Develop

1. Install [.NET Core SDK](https://www.microsoft.com/net/download) and [Node.js](https://nodejs.org/)
2. Run `git clone https://github.com/loic-sharma/BaGet.git`
3. Navigate to `.\BaGet\src\BaGet`
4. Navigate to `..\BaGet`
5. Start the service with `dotnet run`
6. Open the URL `http://localhost:5000/v3/index.json` in your browser
