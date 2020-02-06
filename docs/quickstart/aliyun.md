# Use Alibaba (Aliyun) Object Storage Service

You can store packages to [Alibaba(Aliyun) OSS](https://www.alibabacloud.com/product/oss).

## Configure BaGet

You can modify BaGet's configurations by editing the `appsettings.json` file. For the full list of configurations, please refer to [BaGet's configuration](../configuration.md) guide.

### Aliyun OSS Storage

Update the `appsettings.json` file:

```json
{
    ...

    "Storage": {
        "Type": "AliyunOss",
        "Endpoint": "oss-us-west-1.aliyuncs.com",
        "Bucket": "foo",
        "AccessKey": "",
        "AccessKeySecret": "",
        "Prefix": "lib/baget" // optional
    },

    ...
}
```
