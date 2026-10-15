import Tabs from '@theme/Tabs';
import TabItem from '@theme/TabItem';

# Configuration

You can modify BaGetter's configurations by editing the `appsettings.json` file.

## Require an API key

You can require that users provide a password, called an API key, to publish packages.
To do so, you can insert the desired API key in the `ApiKey` field.

```json
{
    "ApiKey": "NUGET-SERVER-API-KEY",
    ...
}
```

You can also use the `ApiKeys` array in order to manage multiple API keys for multiple teams/developers.

```json
{
    "Authentication": {
        "ApiKeys": [
            {
                "Key" : "NUGET-SERVER-API-KEY-1"
            },
            {
                "Key" : "NUGET-SERVER-API-KEY-2"
            }
        ]
        ...
    }
    ...
}
```

Both `ApiKey` and `ApiKeys` work in conjunction additively eg.: `or` `||` logical operator.

**Security:** With neither key configured, BaGetter rejects publishing,
deletion, relisting and symbol uploads (HTTP 401). Configure a strong secret
before publishing. Existing instances relying on anonymous writes must set an
API key before upgrading.

Configuring `Authentication:Credentials` for private NuGet downloads also
protects the web UI's package listings, details and statistics. With no
download credentials configured, browsing remains public.

Users will now have to provide the API key to push packages:

```shell
dotnet nuget push -s http://localhost:5000/v3/index.json -k NUGET-SERVER-API-KEY package.1.0.0.nupkg
```

## Reverse proxies and cross-origin requests

BaGetter only trusts forwarded headers from ASP.NET Core's trusted loopback
proxies by default. If your reverse proxy runs at a different address,
allowlist **only its actual IP** with `ForwardedHeaders:KnownProxies`:

```json
{
    "ForwardedHeaders": {
        "KnownProxies": ["172.20.0.5"]
    }
}
```

Do not allow untrusted clients to connect directly to the application.
Your proxy must overwrite any client-supplied `X-Forwarded-*` headers.
Use a stable proxy address if configuring proxies by IP.

By default, BaGetter does not grant cross-origin browser access. If needed,
allowlist specific browser origins through `Cors:AllowedOrigins`:

```json
{
    "Cors": {
        "AllowedOrigins": ["https://packages.example.org"]
    }
}
```

## Hosting on a different path

By default, BaGetter is hosted at the root path `/` (e.g. `bagetter.your-company.org`). You can host BaGetter at a different path (e.g. `bagetter.your-company.org/bagetter`) by setting the `PathBase` field:

```json
{
    ...
    "PathBase": "/bagetter",
    ...
}
```

## Enable read-through caching

Upstream V3 version and registration metadata is cached **per BaGetter instance** for
60 seconds by default to reduce requests to nuget.org. Set
`Mirror:MetadataCacheSeconds` (0–3600) to adjust freshness; 0 disables metadata
caching. A cached response is reused for the specified interval and package
updates at the origin may therefore take up to this duration to become visible.
Cached **.nupkg** bytes are kept independently of this metadata TTL. Transient
upstream errors are never stored as successful cache entries.



Read-through caching lets you index packages from an upstream source. You can use read-through
caching to:

1. Speed up your builds if restores from [nuget.org](https://nuget.org) are slow
2. Enable package restores in offline scenarios

The following `Mirror` setting configures BaGetter to index packages from [nuget.org](https://nuget.org):

<Tabs>
  <TabItem value="None" label="No Authentication" default>
    ```json
    {
        ...

        "Mirror": {
            "Enabled":  true,
            "PackageSource": "https://api.nuget.org/v3/index.json"
        },

        ...
    }
    ```
  </TabItem>

  <TabItem value="Basic" label="Basic Authentication">
    For basic authentication, set `Type` to `Basic` and provide a `Username` and `Password`:

    ```json
    {
        ...

        "Mirror": {
            "Enabled":  true,
            "PackageSource": "https://api.nuget.org/v3/index.json",
            "Authentication": {
                "Type": "Basic",
                "Username": "username",
                "Password": "password"
            }
        },

        ...
    }
    ```
  </TabItem>

  <TabItem value="Bearer" label="Bearer Token">
    For bearer authentication, set `Type` to `Bearer` and provide a `Token`:

    ```json
    {
        ...

        "Mirror": {
            "Enabled":  true,
            "PackageSource": "https://api.nuget.org/v3/index.json",
            "Authentication": {
                "Type": "Bearer",
                "Token": "your-token"
            }
        },

        ...
    }
    ```
  </TabItem>

  <TabItem value="Custom" label="Custom Authentication">
    With the custom authentication type, you can provide any key-value pairs which will be set as headers in the request:

    ```json
    {
        ...

        "Mirror": {
            "Enabled":  true,
            "PackageSource": "https://api.nuget.org/v3/index.json",
            "Authentication": {
                "Type": "Custom",
                "CustomHeaders": {
                    "My-Auth": "your-value",
                    "Other-Header": "value"
                }
            }
        },

        ...
    }
    ```
  </TabItem>
</Tabs>


:::info

`PackageSource` is the value of the [NuGet service index](https://docs.microsoft.com/nuget/api/service-index).

:::

## Enable package hard deletions

To prevent the ["left pad" problem](https://blog.npmjs.org/post/141577284765/kik-left-pad-and-npm),
BaGetter's default configuration doesn't allow package deletions. Whenever BaGetter receives a package deletion
request, it will instead "unlist" the package. An unlisted package is undiscoverable but can still be
downloaded if you know the package's id and version. You can override this behavior by setting the
`PackageDeletionBehavior`:

```json
{
    ...

    "PackageDeletionBehavior": "HardDelete",

    ...
}
```

## Package auto-deletion

If your build server generates many nuget packages, your BaGetter server can quickly run out of space. Bagetter leverages [SemVer 2](https://semver.org/) and has logic to keep a history of packages based on the version numbering such as `<major>.<minor>.<patch>-<prerelease tag>.<prerelease build number>`.

There is an optional config section for `Retention` and the following parameters can be enabled to limit history for each level of the version. If none of these are set, there are no cleaning rules enforced. Each parameter is optional, e.g. if you specify only a `MaxPatchVersions`, the package limit will only enforced within each major and minor version combination.
Packages deleted are always the oldest based on version numbers.

Retention is evaluated when **any** limit is set. During read-through mirroring,
the package currently being fetched is temporarily protected from deletion so the
requesting client can download it. Subsequent indexing operations may apply
retention to that older version. Avoid automatic deletion if your builds must be
reproducible across long periods.

- MaxMajorVersions: Maximum number of major versions for each package
- MaxMinorVersions: Maximum number of minor versions for each major version
- MaxPatchVersions: Maximum number of patch versions for each major + minor version
- MaxPrereleaseVersions: Maximum number of prerelease builds for each major + minor + patch version and prerelease type. If you have `beta` and `alpha` this will keep `MaxPrereleaseVersions` versions for both `beta` and `alpha`. Suffixes incompatible with [SemVer 2](https://semver.org/) will be treated as a separate type.

```json
{
    ...
    "Retention": {
        "MaxMajorVersions": 5,
        "MaxMinorVersions": 5,
        "MaxPatchVersions": 5,
        "MaxPrereleaseVersions": 5,
    }
    ...
}
```

## Enable package overwrites

Normally, BaGetter will reject a package upload if the id and version are already taken. This is to maintain the [immutability of semantically versioned packages](https://learn.microsoft.com/azure/devops/artifacts/artifacts-key-concepts?view=azure-devops#immutability).

:::warning

NuGet clients cache packages on multiple levels, so overwriting a package can lead to unexpected behavior.
A client may have a cached version of the package that is different from the one on the server.
Make sure that everyone involved is aware of the implications of overwriting packages.

:::

You can configure BaGetter to overwrite the already existing package by setting `AllowPackageOverwrites`:

```json
{
    ...

    "AllowPackageOverwrites": "true",

    ...
}
```

To allow pre-release versions to be overwritten but not stable releases, set `AllowPackageOverwrites` to `PrereleaseOnly`.

Pushing a package with a pre-release version like "3.1.0-SNAPSHOT" will overwrite the existing "3.1.0-SNAPSHOT" package, but pushing a "3.1.0" package will fail if a "3.1.0" package already exists.

## Private feeds

A private feed requires users to authenticate before accessing packages.

You can require that users provide a username and password to access the nuget feed.
To do so, you can insert the credentials in the `Authentication` section.

```json
{
    "Authentication": {
        "Credentials": [
            {
                "Username": "username",
                "Password": "password"
            }
        ]
        ...
    }
    ...
}
```

Users will now have to provide the username and password to fetch and download packages.

How to add private nuget feed:

1. Download the latest NuGet executable.
2. Open a Command Prompt and change the path to the nuget.exe location.
3. The command from the example below stores a token in the %AppData%\NuGet\NuGet.config file. Your original credentials cannot be obtained from this token.


```shell
NuGet Sources Add -Name "localhost" -Source "http://localhost:5000/v3/index.json" -UserName "username" -Password "password"
```

If you are unable to connect to the feed by using encrypted credentials, store your credentials in clear text:

```shell
NuGet Sources Add -Name "localhost" -Source "http://localhost:5000/v3/index.json" -UserName "username" -Password "password" -StorePasswordInClearText
```

If you have already stored a token instead of storing the credentials as clear text, update the definition in the %AppData%\NuGet\NuGet.config file by using the following command:

```shell
NuGet Sources Update -Name "localhost" -Source "http://localhost:5000/v3/index.json" -UserName "username" -Password "password" -StorePasswordInClearText
```

The commands are slightly different when using the Package Manager console in Visual Studio:

```shell
dotnet nuget add source "http://localhost:5000/v3/index.json" --name "bagetter" --username "username" --password "password"
```

## Database configuration

BaGetter supports multiple database engines for storing package information:

- MySQL: `MySql`
- SQLite: `Sqlite`
- SQL Server: `SqlServer`
- PostgreSQL: `PostgreSql`
- Azure Table Storage: `AzureTable`

Each database engine requires a connection string to configure the connection. Please refer to [ConnectionStrings.com](https://www.connectionstrings.com/) to learn how to create the proper connection string for each database engine.

You may configure the chosen database engine either using environment variables or by editing the `appsettings.json` file.

:::info

Database migrations are automatically applied on application startup.

:::

### Environment Variables

There are two environment variables related to database configuration. These are:

- **Database__Type**: The database engine to use, this should be one of the strings from the above list such as `PostgreSql` or `Sqlite`.
- **Database__ConnectionString**: The connection string for your database engine.

### `appsettings.json`

The database settings are located under the `Database` key in the `appsettings.json` configuration file:

```json
{
    ...

    "Database": {
        "Type": "Sqlite",
        "ConnectionString": "Data Source=bagetter.db"
    },

    ...
}
```

There are two settings related to the database configuration:

- **Type**: The database engine to use, this should be one of the strings from the above list such as `PostgreSql` or `Sqlite`.
- **ConnectionString**: The connection string for your database engine.

## IIS server options

IIS Server options can be configured under the `IISServerOptions` key. The available options are detailed at [docs.microsoft.com](https://docs.microsoft.com/dotnet/api/microsoft.aspnetcore.builder.iisserveroptions)

:::note

HTTP request body limits are controlled by `MaxPackageSizeGiB` and optional `MaxRequestBodySizeMiB`. Ensure IIS and any reverse proxy use compatible limits. `IISServerOptions` can additionally be configured to restrict IIS-hosted deployments.

:::

```json
{
    ...

    "IISServerOptions": {
        "MaxRequestBodySize": 262144000
    },

    ...
}
```

## Health Endpoint

A health endpoint is exposed at `/health` that returns 200 OK or 503 Service Unavailable and always includes a json object listing the current status of the application:

```json
{
  "Status": "Healthy",
  "Sqlite": "Healthy",
  ...
}
```

The services can be omitted by setting the `Statistics:ListConfiguredServices` to false, in which case only the `Status` property is returned in the json object.

This path and the name of the "Status" property are configurable if needed:

```json
{
    ...

    "HealthCheck": {
        "Path": "/healthz",
        "StatusPropertyName": "Status"
    },

    ...
}
```

## Maximum package size

The maximum upload request size defaults to 8 GiB for backward compatibility,
using `MaxPackageSizeGiB`. Unlike earlier versions, Kestrel no longer accepts
unlimited raw request bodies. The limit also applies to multipart form uploads
and IIS hosting.

For a stricter limit, optionally set `MaxRequestBodySizeMiB` (in mebibytes).
This cannot exceed `MaxPackageSizeGiB` and includes the request's multipart
overhead. For most private feeds, start with 256 MiB and adjust based on actual
package sizes. A reverse proxy must also enforce its own request limits.
This can be useful if you are hosting a private feed and need to host large packages that include chocolatey installers, machine learning models, etc.

```json
{
    ...

    "MaxPackageSizeGiB": 8,
    "MaxRequestBodySizeMiB": 256,

    ...
}
```

## Statistics

On the application's statistics page the currently used services and overall package and version counts are listed.
You can hide or show this page by modifying the `EnableStatisticsPage` configuration.
If you set `ListConfiguredServices` to `false` the currently used services for database and storage (such as `Sqlite`) are omitted on the stats page:

```json
{
    ...

    "Statistics": {
        "EnableStatisticsPage": true,
        "ListConfiguredServices": false
    },

    ...
}
```



## Load secrets from files

Mostly useful when running containerised (e.g. using Docker, Podman, Kubernetes, etc), the application will look for files named in the same pattern as environment variables under `/run/secrets`.

```shell
/run/secrets/Database__ConnectionString
```

This allows for sensitive values to be provided individually to the application, typically by bind-mounting files.

### Docker Compose example

```yaml
services:
  bagetter:
    image: bagetter/bagetter:latest
    volumes:
      # Single file mounted for API key
      - ./secrets/api-key.txt:/run/secrets/ApiKey:ro
      - ./data:/srv/baget
    ports:
      - "5000:8080"
    environment:
      - Database__ConnectionString=Data Source=/srv/baget/bagetter.db
      - Database__Type=Sqlite
      - Mirror__Enabled=false
      - Storage__Type=FileSystem
      - Storage__Path=/srv/baget/packages
```

The specified file `./secrets/api-key.txt` contains the clear text api key only.

The port mapping will make available the service at `http://localhost:5000`. (To make it available using `https` you should use an additional reverse proxy service, like "apache" or "nginx".)

Instead of targeting the `latest` version you may also refer to tags for major, minor and fixed releases, e.g. `1`, `1.4` or `1.4.8`.

Aditional documentation for secrets:

- [How to use secrets in Docker Compose](https://docs.docker.com/compose/use-secrets)
- [Docker Swarm secrets](https://docs.docker.com/engine/swarm/secrets)
- [Kubernetes secrets](https://kubernetes.io/docs/concepts/configuration/secret)
- [ASP.NET Core Documentation](https://docs.microsoft.com/aspnet/core/fundamentals/configuration/#key-per-file-configuration-provider)
