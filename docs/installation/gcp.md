# Run BaGet on Google Cloud Platform

!!! warning
    This page is a work in progress!

We're open source and accept contributions!
[Fork us on GitHub](https://github.com/loic-sharma/BaGet).

Before you begin, you should decide which [AppEngine region](https://cloud.google.com/appengine/docs/locations)
you will use. For best performance, Cloud Storage and Cloud SQL should be located
in the same region as your AppEngine deployment.

## Google Cloud Storage

Packages can be stored in [Google Cloud Storage](https://cloud.google.com/storage/).

### Setup

Follow the instructions in [Using Cloud Storage](https://cloud.google.com/appengine/docs/flexible/dotnet/using-cloud-storage) to create a bucket.

### Configuration

**NOTE:** If you plan to use AppEngine, skip this part and follow the AppEngine instructions below.

Set up a service account and download credentials. Set the `GOOGLE_APPLICATION_CREDENTIALS` environment variable to the path to the JSON file you downloaded.

Configure BaGet to use Google Cloud Storage by updating the `appsettings.json` file:

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

* Follow the instructions in [Using Cloud SQL](https://cloud.google.com/appengine/docs/flexible/dotnet/using-cloud-sql) to create a 2nd Gen MySQL 5.7 Google Cloud SQL instance. The default options should work well.
* Create a database named `baget`. This can be done through the Google Cloud Console. Use `utf8mb4` as the Character set.
* Follow [Configuring SSL/TLS](https://cloud.google.com/sql/docs/mysql/configure-ssl-instance#new-client) to create a client certificate. Download the three files it creates.
* Convert the PEM to a PFX by running `openssl pkcs12 -inkey client-key.pem -in client-cert.pem -export -out client.pfx`
  * One way to obtain OpenSSL on Windows is to install [Git Bash](https://gitforwindows.org/).
* Configure BaGet to use Google Cloud SQL by updating the [`appsettings.json`](https://github.com/loic-sharma/BaGet/blob/master/src/BaGet/appsettings.json) file:

```json
{
    ...
    "Database": {
        "Type": "MySql",
        "ConnectionString": "Server=YOURIP;User Id=root;Password=***;Database=baget;CertificateFile=C:\\Path\\To\\client.pfx;CACertificateFile=C:\\Path\\To\\server-ca.pem;SSL Mode=VerifyCA"
    },
    ...
}
```

* Create the tables by running `dotnet ef database update --context MySqlContext --project src\BaGet`

## Google AppEngine

BaGet can be hosted in Google AppEngine. See [here](https://cloud.google.com/appengine/docs/flexible/dotnet/quickstart)
for a tutorial on how to create a new AppEngine project.

Create a `app.yaml` file to publish the Docker container built by the Dockerfile in this repo. In the template
below, make the following replacements:

* `PROJECT` - your GCP project, as returned by `gcloud config get-value project`
* `REGION` -- the GCP region your Google Cloud SQL database is in, e.g., `us-central1` or `us-west2`
* `DBINSTANCE` -- the name of your Google Cloud SQL database instance
* `DBNAME` -- the name of the BaGet database on that instance (e.g., `baget` in the instructions above)
* `PASSWORD` -- the password for the database root user
* `BUCKETNAME` -- the name of the Google Cloud Storage Bucket configured above

```yaml
runtime: custom
env: flex

# The settings below are to reduce costs during testing and are not necessarily
# appropriate for production use. For more information, see:
# https://cloud.google.com/appengine/docs/flexible/dotnet/configuring-your-app-with-app-yaml
resources:
  cpu: 1
  memory_gb: 0.5
  disk_size_gb: 10

beta_settings:
  cloud_sql_instances: "PROJECT:REGION:DBINSTANCE"

env_variables:
  Database__Type: "MySql"
  Database__ConnectionString: "Server=/cloudsql/PROJECT:REGION:DBINSTANCE;User Id=root;Password=PASSWORD;Database=DBNAME;SslMode=None"
  Storage__Type: "GoogleCloud"
  Storage__BucketName: "BUCKETNAME"
  Search__Type: "Database"
  ASPNETCORE_URLS: "http://0.0.0.0:8080"
```

To publish the application, run `gcloud app deploy`.
