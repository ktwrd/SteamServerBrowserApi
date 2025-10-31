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

using System.Text.Json;
using QRCoder;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.Discovery;
using SteamServerBrowserApi.Models;

namespace SteamServerBrowserApi;

public class SteamWrapper
{
    public SteamWrapper()
    {
        Console.WriteLine("==================== Configuration Files ====================");
        Console.WriteLine($"           Auth: {AuthFilename}");
        Console.WriteLine($"        Cell ID: {CellIdFilename}");
        Console.WriteLine($"    Server List: {ServerListFilename}");
        Console.WriteLine();
#if DEBUG
        DebugLog.AddListener(new SteamDebugListener());
        DebugLog.Enabled = true;
#endif
        _cellId = GetCellId();
        var configuration = SteamConfiguration.Create(b =>
        {
            b.WithCellID(_cellId)
                .WithServerListProvider(new FileStorageServerListProvider(ServerListFilename));
        });

        _client = new SteamClient(configuration);
        _client.DebugNetworkListener = new NetHookNetworkListener();
        _manager = new CallbackManager(_client);
        
        _manager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
        _manager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);

        _manager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
        _manager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);
        
        _steamUser = _client.GetHandler<SteamUser>() ?? throw new InvalidOperationException($"Failed to get handler for {typeof(SteamUser)}");
    }

    private readonly SteamClient _client;
    private readonly CallbackManager _manager;
    private readonly SteamUser _steamUser;
    private uint _cellId;
    private bool _isRunning = true;
    private Thread? _thread;

    public SteamClient SteamClient => _client;
    public bool IsRunning => _isRunning;
    public bool IsConnected => _client.IsConnected;
    public DateTimeOffset? LastHeartbeat { get; private set; }
    
    public void RunThread()
    {
        if (_thread?.IsAlive ?? false)
            throw new InvalidOperationException($"Thread already running! ({_thread.ManagedThreadId} {_thread.Name})");
        _thread = new Thread(Thread)
        {
            Name = nameof(SteamWrapper) + '.' + nameof(Thread)
        };
        _thread.Start();
    }
    
    private void Thread()
    {
        try
        {
            _client.Connect();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to connect to steam! {ex}");
            return;
        }
        while (_isRunning)
        {
            _manager.RunWaitCallbacks(TimeSpan.FromSeconds(1));
            LastHeartbeat = DateTimeOffset.UtcNow;
        }
    }

    private async void OnConnected(SteamClient.ConnectedCallback cb)
    {
        var authJson = ReadAuthJson();
        try
        {
            if (TryLogon(authJson, true)) return;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync("Failed to login with saved credentials. Refreshing!\n" + ex);
        }
        // Start an authentication session by requesting a link
        var authSession = await _client.Authentication.BeginAuthSessionViaQRAsync( new AuthSessionDetails() );

        // Steam will periodically refresh the challenge url, this callback allows you to draw a new qr code
        authSession.ChallengeURLChanged = () =>
        {
            Console.WriteLine();
            Console.WriteLine("Steam has refreshed the challenge url");

            DrawQrCode(authSession);
        };

        // Draw current qr right away
        DrawQrCode(authSession);

        // Starting polling Steam for authentication response
        // This response is later used to logon to Steam after connecting
        var pollResponse = await authSession.PollingWaitForResultAsync();

        // Logon to Steam with the access token we have received
        authJson.Username = pollResponse.AccountName;
        authJson.AccessToken = pollResponse.RefreshToken;
        TryLogon(authJson);
        WriteAuthJson(authJson);
    }

    private bool TryLogon(SteamAuthenticationData authJson, bool ignoreValidationErrors = false)
    {
        if (!string.IsNullOrEmpty(authJson.Username)
            && !string.IsNullOrEmpty(authJson.AccessToken))
        {
            Console.WriteLine($"Logging in as \"{authJson.Username}\"");
            _steamUser.LogOn(new SteamUser.LogOnDetails
            {
                Username = authJson.Username,
                AccessToken = authJson.AccessToken
            });
            return true;
        }
        if (ignoreValidationErrors)
        {
            throw new InvalidOperationException("Missing username or accessToken from auth.json!\n" +
                                                JsonSerializer.Serialize(authJson));
        }

        return false;
    }

    private static SteamAuthenticationData ReadAuthJson()
    {
        if (File.Exists(AuthFilename))
        {
            return JsonSerializer.Deserialize<SteamAuthenticationData>(File.ReadAllText(AuthFilename), Project.SerializerOptions)
                   ?? new SteamAuthenticationData();
        }

        return new SteamAuthenticationData();
    }

    private static void WriteAuthJson(SteamAuthenticationData data)
    {
        var json = JsonSerializer.Serialize(data, Project.SerializerOptions);
        File.WriteAllText(AuthFilename, json);
        Console.WriteLine($"[WriteAuthJson] Wrote to file: {AuthFilename}");
    }

    private static void DrawQrCode( QrAuthSession authSession )
    {
        Console.WriteLine($"Challenge URL: {authSession.ChallengeURL}");
        Console.WriteLine();

        // Encode the link as a QR code
        using var qrGenerator = new QRCodeGenerator();
        var qrCodeData = qrGenerator.CreateQrCode(authSession.ChallengeURL, QRCodeGenerator.ECCLevel.L);
        using var qrCode = new AsciiQRCode(qrCodeData);
        var qrCodeAsAsciiArt = qrCode.GetGraphic( 2, drawQuietZones: false);

        Console.WriteLine("Use the Steam Mobile App to sign in via QR code:");
        Console.WriteLine(qrCodeAsAsciiArt);
    }

    private void OnDisconnected(SteamClient.DisconnectedCallback cb)
    {
        Console.WriteLine($"Disconnected from Steam ({nameof(cb.UserInitiated)}: {cb.UserInitiated})");
        _isRunning = false;
    }

    private void OnLoggedOn(SteamUser.LoggedOnCallback callback)
    {
        if (callback.Result != EResult.OK)
        {
            if (callback.Result == EResult.AccountLogonDenied)
            {
                // if we receive AccountLogonDenied or one of its flavors (AccountLogonDeniedNoMailSent, etc)
                // then the account we're logging into is SteamGuard protected
                // see sample 5 for how SteamGuard can be handled

                Console.WriteLine( "Unable to logon to Steam: This account is SteamGuard protected." );

                _isRunning = false;
                return;
            }

            Console.WriteLine("Unable to logon to Steam: {0} / {1}", callback.Result, callback.ExtendedResult);

            _isRunning = false;
            return;
        }

        // save the current cellid somewhere. if we lose our saved server list, we can use this when retrieving
        // servers from the Steam Directory.
        _cellId = callback.CellID;
        File.WriteAllText("cellid.txt", callback.CellID.ToString() );

        Console.WriteLine("Successfully logged on! Press Ctrl+C to log off..." );
    }

    private void OnLoggedOff(SteamUser.LoggedOffCallback callback)
    {
        Console.WriteLine("Logged off of Steam: {0}", callback.Result);
    }

    private static uint GetCellId()
    {
        var cellId = 0u;
        if (File.Exists( CellIdFilename))
        {
            if (!uint.TryParse( File.ReadAllText( CellIdFilename), out cellId))
            {
                Console.WriteLine($"Error parsing cellid from {CellIdFilename} (assuming value of 0)");
                cellId = 0;
            }
            else
            {
                Console.WriteLine($"Using persisted cell ID {cellId}");
            }
        }

        return cellId;
    }


    private static string GetBasePath()
    {
        var path = Environment.GetCommandLineArgs().Any(e => e == "docker")
            ? "/data/"
            : "./data";
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
        return path;
    }

    private static string CellIdFilename => Path.Join(GetBasePath(), "cellid.txt");
    private static string ServerListFilename => Path.Join(GetBasePath(), "servers_list.bin");
    private static string AuthFilename => Path.Join(GetBasePath(), "auth.json");
}

public class SteamDebugListener : IDebugListener
{
    public void WriteLine(string category, string msg)
    {
        if (!(msg.EndsWith("(703)") || msg.EndsWith("(766)") || msg.EndsWith("(146)") || msg.EndsWith("(822)")))
            Console.WriteLine("SteamKit2 - {0}: {1}", category, msg);
    }
}