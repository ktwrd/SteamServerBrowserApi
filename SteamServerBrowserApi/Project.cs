/*
   Copyright 2025 Kate Ward <kate@dariox.club>

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/

using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using GenHTTP.Api.Content;
using GenHTTP.Modules.Layouting;
using GenHTTP.Modules.Security;
using GenHTTP.Modules.Functional;
using GenHTTP.Modules.OpenApi;
using GenHTTP.Modules.ApiBrowsing;
using GenHTTP.Modules.Conversion;
using Microsoft.Extensions.Caching.Memory;
using SteamKit2;
using SteamServerBrowserApi.Models;

namespace SteamServerBrowserApi;

public class Project
{
    private readonly SteamWrapper _wrapper = new();

    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };
    
    private readonly Dictionary<string, SteamQuery.GameServer> _gameServerCache = [];
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions()
    {
        SizeLimit = 1000
    });

    public IHandlerBuilder Setup()
    {
        var ser = Serialization.Default(SerializerOptions);
        
        var healthApi = Inline.Create()
            .Serializers(ser)
            .Get("health", GetHealth);

        var serverApi = Inline.Create()
            .Serializers(ser)
            .Get("server/search", GetServerSearch)
            .Get("server/info", GetServerInfo);
        
        _wrapper.RunThread();
        
        return Layout.Create()
                     .Add(serverApi)
                     .Add(healthApi)
                     .AddOpenApi()
                     .AddSwaggerUI(segment: "swagger")
                     .Add(CorsPolicy.Permissive());
    }
    
    /// <summary>
    /// Search for servers using the Master Server Query Protocol
    /// </summary>
    /// <param name="appId">Steam App Id</param>
    /// <param name="region">Default to <see cref="ERegionCode.World"/> when null</param>
    /// <param name="filter">
    /// For more information, see <see href="https://developer.valvesoftware.com/wiki/Master_Server_Query_Protocol#Filter"/>
    /// </param>
    /// <returns>
    /// List of servers with the provided filtering
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this method couldn't get <see cref="SteamMasterServer"/> from <see cref="SteamClient"/>
    /// </exception>
    private async Task<List<ServerBrowserResponseItem>> GetServerSearch(
        uint appId,
        ERegionCode? region = null,
        string? filter = null)
    {
        if (!_wrapper.IsConnected) throw new ApplicationException("Not connected to Steam");
        
        var handler = _wrapper.SteamClient.GetHandler<SteamMasterServer>()
                      ?? throw new InvalidOperationException($"Failed to get handler for {typeof(SteamMasterServer)}");
        var options = new SteamMasterServer.QueryDetails()
        {
            AppID = appId,
            Region = region.GetValueOrDefault(ERegionCode.World),
            Filter = filter,
            MaxServers = 1000
        };
        var optionsJson = JsonSerializer.Serialize(options, SerializerOptions);
        var cacheKey = $"{nameof(GetServerSearch)}\n" + optionsJson.ToLower();

        if (_cache.TryGetValue(cacheKey, out List<ServerBrowserResponseItem>? mappedData) && mappedData != null) return mappedData;
        
        SteamMasterServer.QueryCallback result;
        try
        {
            result = await handler.ServerQuery(options);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Failed to run query: {optionsJson}\n{ex}");
            throw;
        }
        
        if (_cache.TryGetValue(cacheKey, out mappedData) && mappedData != null) return mappedData;
        
        mappedData = MapServerBrowserQueryResponse(result.Servers);
        _cache.Set(cacheKey, mappedData, new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromSeconds(30)));
        
        return mappedData;
    }

    /// <summary>
    /// Get Server Information for something that runs on the Source Engine (or supports it's query protocol)
    /// </summary>
    /// <param name="ip"></param>
    /// <param name="port"></param>
    /// <returns></returns>
    private SteamQuery.Models.SteamQueryInformation? GetServerInfo(string ip, int? port = null)
    {
        var endpoint = port.HasValue
            ? new IPEndPoint(IPAddress.Parse(ip), port.Value)
            : IPEndPoint.Parse(ip);

        return FetchServerInfo(endpoint);
    }

    private HealthResponse GetHealth()
    {
        return new HealthResponse()
        {
            IsRunning = _wrapper.IsRunning,
            IsConnected = _wrapper.IsConnected,
            LastHeartbeat = _wrapper.LastHeartbeat?.ToUnixTimeSeconds(),
            Version = GetType().Assembly.GetName().Version?.ToString()
        };
    }
    
    private List<ServerBrowserResponseItem> MapServerBrowserQueryResponse(ICollection<SteamMasterServer.QueryCallback.Server> skServer)
    {
        var result = new List<ServerBrowserResponseItem>();
        Parallel.ForEach(skServer.Select(e => e.EndPoint), endpoint =>
        {
            var endpointStr = endpoint.ToString();
            if (endpointStr.StartsWith("169.254") || endpointStr.StartsWith("192.") || endpointStr.StartsWith("10.")) return;

            var info = FetchServerInfo(endpoint);
            if (info == null) return;

            lock (result)
            {
                result.Add(new ServerBrowserResponseItem(endpointStr, info));
            }
        });
        return result.OrderByDescending(e => e.OnlinePlayers).ToList();
    }

    private SteamQuery.Models.SteamQueryInformation? FetchServerInfo(IPEndPoint endpoint)
    {
        var endpointStr = endpoint.ToString();
        var cacheKey = $"{nameof(MapServerBrowserQueryResponse)}\tinfo\t{endpointStr}";
        if (_cache.TryGetValue(cacheKey, out SteamQuery.Models.SteamQueryInformation? info)) return info;
        
        SteamQuery.GameServer? server;
        lock (_gameServerCache)
        {
            if (!_gameServerCache.TryGetValue(endpointStr, out server))
            {
                server = new SteamQuery.GameServer(endpoint);
                server.SendTimeout = TimeSpan.FromSeconds(2);
                server.ReceiveTimeout = TimeSpan.FromSeconds(2);
                _gameServerCache[endpointStr] = server;
            }
            server.SendTimeout = TimeSpan.FromSeconds(2);
            server.ReceiveTimeout = TimeSpan.FromSeconds(2);
            _gameServerCache[endpointStr] = server;
        }

        try
        {
            info = server.GetInformation();
        }
        catch (SocketException socketException)
        {
            if (!IgnoredSocketErrors.Contains(socketException.SocketErrorCode)) throw;
            info = null;
        }

        _cache.Set(cacheKey, info, new MemoryCacheEntryOptions()
            .SetSlidingExpiration(TimeSpan.FromSeconds(info == null ? 15 : 30)));

        return info;
    }

    private static readonly HashSet<SocketError> IgnoredSocketErrors =
    [
        SocketError.HostDown,
        SocketError.HostUnreachable,
        SocketError.HostNotFound,
        SocketError.OperationAborted,
        SocketError.AccessDenied,
        SocketError.Fault,
        SocketError.TimedOut,
        SocketError.ConnectionRefused
    ];
}
