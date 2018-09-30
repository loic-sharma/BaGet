SET PATH=%LOCALAPPDATA%\Microsoft\dotnet;%PATH%
dotnet restore .paket/
dotnet fake run build.fsx %*
