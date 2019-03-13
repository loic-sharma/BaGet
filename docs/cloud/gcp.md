# Running BaGet on Google Cloud

!!! warning
    This page is a work in progress!

We're open source and accept contributions!
[Fork us on GitHub](https://github.com/loic-sharma/BaGet).

## Google Cloud Storage

Packages can be stored in [Google Cloud Storage](https://cloud.google.com/storage/).

### Setup

Follow the instructions in [Using Cloud Storage](https://cloud.google.com/appengine/docs/flexible/dotnet/using-cloud-storage) to:

* Create a bucket
* Set up a service account and download credentials
* Set the `GOOGLE_APPLICATION_CREDENTIALS` environment variable to the path to the JSON file you downloaded

### Configuration

Configure BaGet to use GCS by updating the [`appsettings.json`](https://github.com/loic-sharma/BaGet/blob/master/src/BaGet/appsettings.json) file:

```json
{
    ...

    "Storage": {
        "Type": "GoogleCloud",
        "BucketName": "your-gcs-bucket"
    },

    ...
}
```

## Google Cloud SQL

* TODO

## Google AppEngine

* TODO
