using Dota2.GC;
using SteamKit2;
using SteamKit2.Discovery;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using static Dota2.GC.Dota.Internal.CMsgDOTARequestChatChannelListResponse;

namespace DotaSpamBot
{
    // define our debuglog listener
    class DebugListener : IDebugListener
    {
        public void WriteLine(string category, string msg)
        {
            Console.WriteLine($"DebugListener [{category}]: {msg}");
        }
    }

    class Program
    {
        private SteamClient steamClient;
        private CallbackManager manager;
        private DotaGCHandler dota;

        private SteamUser steamUser;

        private bool isRunning;

        private string user = "emin38453";
        private string pass = "46702294Spice";

        private string authCode, twoFactorAuth;
        private List<ChatChannel> targetChannels;
        private int proccessedCount;
        private int currentProccessedCount;
        private int totalProccessedCount;

        static void Main(string[] args)
        {
            var program = new Program();
            program.Run().GetAwaiter().GetResult();
        }

        private async Task Run()
        {
            try
            {
                //DebugLog.AddListener(new DebugListener());
                //DebugLog.Enabled = true;
                //create our steamclient instance
                var cellid = 0u;

                // if we've previously connected and saved our cellid, load it.
                if (File.Exists("cellid.txt"))
                {
                    if (!uint.TryParse(File.ReadAllText("cellid.txt"), out cellid))
                    {
                        Console.WriteLine("Error parsing cellid from cellid.txt. Continuing with cellid 0.");
                        cellid = 0;
                    }
                    else
                    {
                        Console.WriteLine($"Using persisted cell ID {cellid}");
                    }
                }
                var configuration = SteamConfiguration.Create((config) =>
                {
                    config.WithServerListProvider(new FileStorageServerListProvider("servers_list.bin"));
                    config.WithCellID(cellid);
                });

                steamClient = new SteamClient(configuration);
                DotaGCHandler.Bootstrap(steamClient);
                dota = steamClient.GetHandler<DotaGCHandler>();

                // create the callback manager which will route callbacks to function calls
                manager = new CallbackManager(steamClient);

                // get the steamuser handler, which is used for logging on after successfully connecting
                steamUser = steamClient.GetHandler<SteamUser>();

                // register a few callbacks we're interested in
                // these are registered upon creation to a callback manager, which will then route the callbacks
                // to the functions specified
                manager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
                manager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);

                manager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
                manager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);
                manager.Subscribe<SteamUser.UpdateMachineAuthCallback>(OnMachineAuth);
                manager.Subscribe<DotaGCHandler.BeginSessionResponse>(OnBeginSession);
                manager.Subscribe<DotaGCHandler.ConnectionStatus>(OnConnectionStatus);
                manager.Subscribe<DotaGCHandler.JoinChatChannelResponse>(OnJoinChatChannel);
                manager.Subscribe<DotaGCHandler.ChatChannelListResponse>(OnChatChannelList);

                isRunning = true;
                Console.WriteLine("Connecting to Steam...");

                // initiate the connection
                steamClient.Connect();

                // create our callback handling loop
                while (isRunning)
                {
                    // in order for the callbacks to get routed, they need to be handled by the manager
                    manager.RunWaitCallbacks(TimeSpan.FromSeconds(1));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception occured: {ex.GetType()}: {ex.Message}");
                Run().GetAwaiter().GetResult();
            }
            await Task.Delay(-1);
        }

        private void OnConnectionStatus(DotaGCHandler.ConnectionStatus callback)
        {
            Console.WriteLine($"Connection status: {callback.result.status}");
        }

        private void OnBeginSession(DotaGCHandler.BeginSessionResponse callback)
        {
            Console.WriteLine($"Starting session ID: {callback.response.SessionId}: [{callback.response.Result}]");
        }

        private void OnChatChannelList(DotaGCHandler.ChatChannelListResponse callback)
        {
            Console.WriteLine("Updating channel list...");
            var chatChannels = callback.result.channels;
            targetChannels = chatChannels.FindAll(x => (x.channel_name.Contains("Party") || x.channel_name.Contains("PARTY") || x.channel_name.Contains("ПАТИ") || x.channel_name.Contains("пати") || x.channel_name.Contains("TestChannel") || x.channel_name.Contains("St. Petersburg") || x.channel_name.Contains("Moscow") || x.channel_name.Contains("Russ") || x.channel_name.Contains("Ищу") || x.channel_name.Contains("ищу") || x.channel_name.Contains("абуз") || x.channel_name.Contains("Абуз") || x.channel_name.Contains("чит") || x.channel_name.Contains("Набор") || x.channel_name.Contains("Abuse") || x.channel_name.Contains("abuse")) && !x.channel_name.Contains("2016") && !x.channel_name.Contains("pass") && !x.channel_name.Contains("Battle") && !x.channel_name.Contains("Battlepass") && !x.channel_name.Contains("Compendium") && !x.channel_name.Contains("compendium") && x.num_members > 0);
            //targetChannels = chatChannels.FindAll(x => x.channel_name.Contains("TestChannel") || x.channel_name.Contains("DotaMeat"));
            Console.WriteLine($"{targetChannels?.Count} channels found");
            JoinChannels().GetAwaiter().GetResult();
        }

        private void OnJoinChatChannel(DotaGCHandler.JoinChatChannelResponse callback)
        {
            if (callback.result.result.ToString() != "JOIN_SUCCESS")
            {
                targetChannels.RemoveAt(totalProccessedCount);
                Console.WriteLine($"Failed to join {callback.result?.channel_name} ({callback.result?.channel_id})");
            }
            else
            {
                Console.WriteLine($"Sending message to {callback.result?.channel_name} ({callback.result?.channel_id})");
                SendMessage(callback.result.channel_id);
                Console.WriteLine($"Leaving channel {callback.result?.channel_name} ({callback.result?.channel_id})");
                dota.LeaveChatChannel(callback.result.channel_id);                
            }
            proccessedCount++;
            totalProccessedCount++;
            System.Threading.Thread.Sleep(7000);
            if (steamClient.IsConnected && targetChannels != null && targetChannels.Count != 0)
            {
                if (totalProccessedCount == targetChannels.Count)
                {
                    currentProccessedCount = 0;
                    totalProccessedCount = 0;
                    proccessedCount = 0;
                    //isAdvSpam = !isAdvSpam;
                    dota.RequestChatChannelList();
                    return;
                }

                if (proccessedCount == 5)
                {
                    JoinChannels().GetAwaiter().GetResult();
                }
                Console.WriteLine($"Processed channels count: {totalProccessedCount}");
            }
        }

        private void OnLoggedOff(SteamUser.LoggedOffCallback callback)
        {
            Console.WriteLine($"Logged off of Steam: {callback.Result}");
        }

        private void OnLoggedOn(SteamUser.LoggedOnCallback callback)
        {
            bool isSteamGuard = callback.Result == EResult.AccountLogonDenied;
            bool is2FA = callback.Result == EResult.AccountLoginDeniedNeedTwoFactor;

            if (isSteamGuard || is2FA)
            {
                Console.WriteLine("This account is SteamGuard protected!");
                if (is2FA)
                {
                    Console.Write("Please enter your 2 factor auth code from your authenticator app: ");
                    twoFactorAuth = Console.ReadLine();
                }
                else
                {
                    Console.Write($"Please enter the auth code sent to the email at {callback.EmailDomain}: ");
                    authCode = Console.ReadLine();
                }
                return;
            }

            if (callback.Result != EResult.OK)
            {
                if (callback.Result == EResult.AccountLogonDenied)
                {
                    // if we recieve AccountLogonDenied or one of it's flavors (AccountLogonDeniedNoMailSent, etc)
                    // then the account we're logging into is SteamGuard protected
                    // see sample 5 for how SteamGuard can be handled

                    Console.WriteLine("Unable to logon to Steam: This account is SteamGuard protected.");
                    isRunning = false;
                    return;
                }

                Console.WriteLine($"Unable to logon to Steam: {callback.Result} / {callback.ExtendedResult}");

                isRunning = false;
                return;
            }
            File.WriteAllText("cellid.txt", callback.CellID.ToString());
            Console.WriteLine("Successfully logged on!");
            Console.WriteLine($"Starting dota...");
            dota.Start();
            System.Threading.Thread.Sleep(2000);
            Console.WriteLine($"Requesting channel list...");
            dota.RequestChatChannelList();
        }

        private void OnDisconnected(SteamClient.DisconnectedCallback callback)
        {
            Console.WriteLine("Disconnected from Steam, reconnecting in 5...");
            System.Threading.Thread.Sleep(5000);
            steamClient.Connect();
        }

        private void OnConnected(SteamClient.ConnectedCallback callback)
        {
            Console.WriteLine($"Connected to Steam! Logging in '{user}'...");
            byte[] sentryHash = null;
            if (File.Exists("sentry.bin"))
            {
                // if we have a saved sentry file, read and sha-1 hash it
                byte[] sentryFile = File.ReadAllBytes("sentry.bin");
                sentryHash = CryptoHelper.SHAHash(sentryFile);
            }
            steamUser.LogOn(new SteamUser.LogOnDetails
            {
                Username = user,
                Password = pass,
                // in this sample, we pass in an additional authcode
                // this value will be null (which is the default) for our first logon attempt
                AuthCode = authCode,
                TwoFactorCode = twoFactorAuth,

                // our subsequent logons use the hash of the sentry file as proof of ownership of the file
                // this will also be null for our first (no authcode) and second (authcode only) logon attempts
                SentryFileHash = sentryHash,
                ClientLanguage = "Russian"
            });
        }

        private void OnMachineAuth(SteamUser.UpdateMachineAuthCallback callback)
        {
            Console.WriteLine("Updating sentryfile...");

            // write out our sentry file
            // ideally we'd want to write to the filename specified in the callback
            // but then this sample would require more code to find the correct sentry file to read during logon
            // for the sake of simplicity, we'll just use "sentry.bin"

            int fileSize;
            byte[] sentryHash;
            using (var fs = File.Open("sentry.bin", FileMode.OpenOrCreate, FileAccess.ReadWrite))
            {
                fs.Seek(callback.Offset, SeekOrigin.Begin);
                fs.Write(callback.Data, 0, callback.BytesToWrite);
                fileSize = (int)fs.Length;

                fs.Seek(0, SeekOrigin.Begin);
                using (var sha = SHA1.Create())
                {
                    sentryHash = sha.ComputeHash(fs);
                }
            }

            // inform the steam servers that we're accepting this sentry file
            steamUser.SendMachineAuthResponse(new SteamUser.MachineAuthDetails
            {
                JobID = callback.JobID,

                FileName = callback.FileName,

                BytesWritten = callback.BytesToWrite,
                FileSize = fileSize,
                Offset = callback.Offset,

                Result = EResult.OK,
                LastError = 0,

                OneTimePassword = callback.OneTimePassword,

                SentryFileHash = sentryHash,
            });

            Console.WriteLine("Updating sentryfile has been done!");
        }

        private Task JoinChannels()
        {
            currentProccessedCount += proccessedCount;
            proccessedCount = 0;
            var iters = targetChannels?.Count < 5 ? targetChannels.Count : 5;
            if ((targetChannels.Count - currentProccessedCount) < 5)
                iters = targetChannels.Count - currentProccessedCount;
            for (int i = 0; i < iters; i++)
            {
                dota.JoinChatChannel(targetChannels[currentProccessedCount + i].channel_name);
            }
            return Task.CompletedTask;
        }

        private void SendMessage(ulong channelId)
        {
            var message = "Устал проигрывать? МАПХАК УЖЕ ЗДЕСЬ! Только самые актуальные ЧИТЫ на DotaMeat.com";            
            dota.SendChannelMessage(channelId, message);            
        }
    }
}
