#!/usr/bin/env bash

# See: https://github.com/dotnet/aspnetcore/blob/4b4265972d1155e415633a4a177f159b940cf3bb/.devcontainer/scripts/container-creation.sh

set -e

# Install SDK and tool dependencies before container starts
# Also run the full restore on the repo so that go-to definition
# and other language features will be available in C# files
dotnet restore

# Add .NET Dev Certs to environment to facilitate debugging.
# Do **NOT** do this in a public base image as all images inheriting
# from the base image would inherit these dev certs as well.
dotnet dev-certs https

# The container creation script is executed in a new Bash instance
# so we exit at the end to avoid the creation process lingering.
exit
