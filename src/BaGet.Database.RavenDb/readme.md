# BaGet's Raven Database Provider

This project contains BaGet's Raven database provider.

## Migrations

Migrations not required.

## Notes

Provider assumes that IAsyncDocumentSession is registered in DI. Eq:

    services.AddSingleton(sp =>
    {
        var store = new DocumentStore
        {
            ...
        };
        store.Initialize();
        return store;);
    });
    services.AddScoped(sp => sp.GetRequiredService<IDocumentStore>().OpenAsyncSession());
