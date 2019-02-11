# Running BaGet on AWS

!!! warning
    This page is a work in progress!

## Amazon S3

You can configure BaGet to store uploaded packages on [Amazon S3](https://aws.amazon.com/s3/)
by updating [`appsettings.json`](https://github.com/loic-sharma/BaGet/blob/master/src/BaGet/appsettings.json) file:

```json
{
    ...

    "Storage": {
        "Type": "AwsS3",
        "Region": "us-west-1",
        "Bucket": "foo",
        "AccessKey": "",
        "SecretKey": ""
    },

    ...
}
```
