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

using System.Text.Json.Serialization;
using SteamQuery.Models;

namespace SteamServerBrowserApi.Models;

public class ServerBrowserResponseItem(string ip, SteamQueryInformation info)
{
    [JsonConstructor]
    public ServerBrowserResponseItem()
    : this("", new SteamQueryInformation())
    {
    }
    
    public string? IpAddress { get; set; } = ip;
    public long OnlinePlayers { get; set; } = info.OnlinePlayers;
    public string? ServerName { get; set; } = info.ServerName;
    public string? Version { get; set; } = info.Version;
    public string? GameName { get; set; } = info.GameName;
    public string? Map { get; set; } = info.Map;
    public SteamQueryInformation Information { get; set; } = info;
}