# BaGet Frontend

## Developing

1. Start the BaGet backend:

```
cd src/BaGet
dotnet run
```

2. Launch the BaGet frontend:

```
cd src/BaGet.UI
yarn install
yarn develop
```

TODO: backend URLs in "develop" mode are broken. You will need to update `components/SearchResults.tsx`
and `components/DisplayPackage.tsx` to point to your NuGet backend.

## Building

1. Delete `src/BaGet/wwwroot`
2. Rebuild the frontend:

```
cd src/BaGet.UI
yarn build
```