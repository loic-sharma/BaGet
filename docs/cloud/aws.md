# Running BaGet on AWS

!!! warning
    This page is a work in progress!

## Amazon S3

You can configure BaGet to store uploaded packages on [Amazon S3](https://aws.amazon.com/s3/)
by updating the [`appsettings.json`](https://github.com/loic-sharma/BaGet/blob/master/src/BaGet/appsettings.json) file in one of three ways:

1. Basic AWS credentials
2. The instance profile
3. A cross-account role

### Basic AWS credentials

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

### Instance profile configuration

```json
{
    ...

    "Storage": {
        "Type": "AwsS3",
        "Region": "us-west-1",
        "Bucket": "foo",
        "UseInstanceProfile": true
    },

    ...
}
```

### Cross-account role configuration

```json
{
    ...

    "Storage": {
        "Type": "AwsS3",
        "Region": "us-west-1",
        "Bucket": "foo",
        "AssumeRoleArn": "arn:aws:iam::123456789012:role/myrole"
    },

    ...
}
```