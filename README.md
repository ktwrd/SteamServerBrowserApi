# Steam Server Browser API

Rest API Server for accessing and querying Steam for game servers.

- You can get Source Engine Server information at the endpoint `/server/info`
    - Query protocol can be found in the Valve Developer Wiki: [https://developer.valvesoftware.com/wiki/Master_Server_Query_Protocol#Filter](https://developer.valvesoftware.com/wiki/Master_Server_Query_Protocol#Filter)
- Server Browser can be queried at `/server/search`
- Swagger UI is available at `/swagger/`

On first launch, a QR code will be visible in the console. Scan it with the Steam App (or use the URL) to login. Your username & refresh token will be stored in `auth.json`.

## Development

To build this project from source, checkout this repository and execute
the following commands in your terminal. This requires the
[.NET SDK](https://dotnet.microsoft.com/download) to be installed.

```bash
cd SteamServerBrowserApi
dotnet run
```

This will make the service available at http://localhost:8080/.

## Deployment

To run this project with [Docker](https://www.docker.com/), run the 
following commands in your terminal (and adjust the first line
depending on your platform).

```bash
docker build -t SteamServerBrowserApi .

docker run -v ./data:/data -p 8080:8080 SteamServerBrowserApi
```

You can also run the latest image from the GitHub Container Registry
```bash
docker run -v ./data:/data -p 8080:8080 ghcr.io/ktwrd/steam-server-browser-api:latest
```

## About

This project uses the [GenHTTP webserver](https://genhttp.org/) to
implement its functionality.
