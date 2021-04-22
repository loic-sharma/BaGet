# Use Bce (Baidu Cloud) Object Storage Service

You can store packages to [Bce (Baidu Cloud) BOS](https://cloud.baidu.com/doc/BOS/index.html).

## Configure BaGet

You can modify BaGet's configurations by editing the `appsettings.json` file. For the full list of configurations, please refer to [BaGet's configuration](../configuration.md) guide.

### Bce BOS Storage

Update the `appsettings.json` file:

```json
{
    ...

    "Storage": {
        "Type": "BceBos",
        "Endpoint": "http://su.bcebos.com",
        "Bucket": "foo",
        "AccessKey": "",
        "AccessKeySecret": "",
        "Prefix": "lib/baget" // optional
    },

    ...
}
```
