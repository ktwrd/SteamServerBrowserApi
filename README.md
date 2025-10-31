# Steam Server Browser API

Rest API Server for accessing and querying the Steam for game servers.

- You can get Source Engine Server information at the endpoint `/server/info`
    - Query protocol can be found in the Valve Developer Wiki: [https://developer.valvesoftware.com/wiki/Master_Server_Query_Protocol#Filter](https://developer.valvesoftware.com/wiki/Master_Server_Query_Protocol#Filter)
- Server Browser can be queried at `/server/search`
- Swagger UI is available at `/swagger/`

On first launch, a QR code will be visible in the console. Scan it with the Steam App (or use the URL) to login. Your username & refresh token will be stored in `auth.json`.

>[!IMPORTANT]
> Rate limiting has not been implemented, but caching has been implemented.

## Development

To build this project from source, checkout this repository and execute
the following commands in your terminal. This requires the
[.NET SDK](https://dotnet.microsoft.com/download) to be installed.

```
cd SteamServerBrowserApi
dotnet run
```

This will make the service available at http://localhost:8080/.

## Deployment

To run this project with [Docker](https://www.docker.com/), run the 
following commands in your terminal (and adjust the first line
depending on your platform).

```
docker build -t SteamServerBrowserApi .

docker run -p 8080:8080 SteamServerBrowserApi
```

## About

This project uses the [GenHTTP webserver](https://genhttp.org/) to
implement its functionality.
