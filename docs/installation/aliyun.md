# Use Alibaba (Aliyun) Object Storage Service

You can store packages to [Alibaba(Aliyun) OSS](https://www.alibabacloud.com/product/oss).

## Configure BaGet Extension

You can modify BaGet Extension's configurations by editing the `appsettings.json` file. For the full list of configurations, please refer to [BaGet Extension's configuration](../configuration.md) guide.

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
