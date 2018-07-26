using Dota2.GC;
using Dota2.GC.Dota.Internal;
using Newtonsoft.Json;
using SteamKit2;
using SteamKit2.Discovery;
using SteamKit2.GC;
using SteamKit2.Internal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using static Dota2.GC.Dota.Internal.CMsgDOTARequestChatChannelListResponse;

namespace DotaSpamBot
{
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
        private ConfigModel botConfig;
        private SteamUser steamUser;

        private bool isRunning;
        private bool dotaIsReady;
        private string authCode, twoFactorAuth;
        private List<ChatChannel> targetChannels;
        private int totalProccessedCount;
        private string[] chatRegions;

        static void Main(string[] args)
        {
            var program = new Program();
            program.Run().GetAwaiter().GetResult();
        }

        private async Task Run()
        {
            try
            {
                if (File.Exists("config.json"))
                {
                    using (var sr = new StreamReader("config.json"))
                    {
                        string jsonString = string.Empty;
                        string line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            jsonString += line;
                        }
                        botConfig = JsonConvert.DeserializeObject<ConfigModel>(jsonString);
                        if (string.IsNullOrEmpty(botConfig.login))
                        {
                            Console.WriteLine("ERROR: Steam login is empty");
                            await Task.Delay(3000);
                            return;
                        }
                        if (string.IsNullOrEmpty(botConfig.password))
                        {
                            Console.WriteLine("ERROR: Steam password is empty");
                            await Task.Delay(3000);
                            return;
                        }
                        if (string.IsNullOrEmpty(botConfig.msgText))
                        {
                            Console.WriteLine("ERROR: Message content is empty");
                            await Task.Delay(3000);
                            return;
                        }
                    }
                }
                else
                {
                    Console.WriteLine("ERROR: config.json file is not found");
                    await Task.Delay(3000);
                    return;
                }
                if(File.Exists("chat_regions.txt"))
                {
                    chatRegions = File.ReadAllLines("chat_regions.txt");
                }

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
                manager.Subscribe<DotaGCHandler.GCWelcomeCallback>(OnDotaWelcome);

                isRunning = true;
                Console.WriteLine("Connecting to Steam...");

                // initiate the connection
                steamClient.Connect();
                while (isRunning)
                {
                    manager.RunWaitCallbacks(TimeSpan.FromSeconds(1));                    
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception occured: {ex.GetType()}: {ex.Message}");
                dotaIsReady = false;
                steamClient.Disconnect();
            }
            await Task.Delay(-1);
        }

        private void OnDotaWelcome(DotaGCHandler.GCWelcomeCallback callback)
        {
            if (!dotaIsReady && dota.Ready)
            {
                dotaIsReady = true;
                Console.WriteLine($"Requesting channel list...");
                dota.RequestChatChannelList();
            }
        }

        //public void Send(IClientGCMsg msg)
        //{
        //    var clientMsg = new ClientMsgProtobuf<CMsgGCClient>(EMsg.ClientToGC);

        //    clientMsg.Body.msgtype = MsgUtil.MakeGCMsg(msg.MsgType, msg.IsProto);
        //    clientMsg.Body.appid = (uint)570;

        //    clientMsg.Body.payload = msg.Serialize();

        //    steamClient.Send(clientMsg);
        //}

        private void OnConnectionStatus(DotaGCHandler.ConnectionStatus callback)
        {
            if (callback.result.status == Dota2.GC.Internal.GCConnectionStatus.GCConnectionStatus_NO_SESSION_IN_LOGON_QUEUE)
            {
                dotaIsReady = false;
                Console.WriteLine("Waiting for LOGON QUEUE 10 seconds...");
                Thread.Sleep(TimeSpan.FromSeconds(10));
                return;
            }
            if (callback.result.status != Dota2.GC.Internal.GCConnectionStatus.GCConnectionStatus_HAVE_SESSION)
            {
                dotaIsReady = false;
                if (dota.Ready)
                    dota.Stop();
                dota.Start();
            }
        }

        private void OnBeginSession(DotaGCHandler.BeginSessionResponse callback)
        {
            Console.WriteLine($"Starting session ID: {callback.response.SessionId}: [{callback.response.Result}]");
        }

        private void OnChatChannelList(DotaGCHandler.ChatChannelListResponse callback)
        {
            Console.WriteLine("Updating channel list...");
            var chatChannels = callback.result.channels;
            targetChannels?.Clear();
            targetChannels = chatChannels.FindAll(x => x.num_members >= botConfig.minChannelUsersCount);
            //targetChannels = new List<ChatChannel> { new ChatChannel { channel_name = "Kazan, TT", channel_type = DOTAChatChannelType_t.DOTAChannelType_Regional } };
            if(chatRegions != null && chatRegions.Length > 0)
            {
                targetChannels.AddRange(chatRegions.Select(x => new ChatChannel { channel_name = x }));
            }
            Console.WriteLine($"{targetChannels?.Count} channels found");
            JoinChannels();
        }

        private void OnJoinChatChannel(DotaGCHandler.JoinChatChannelResponse callback)
        {
            if (callback.result.result == CMsgDOTAJoinChatChannelResponse.Result.USER_IN_TOO_MANY_CHANNELS)
            {
                Console.WriteLine($"Connected channels limit exceed");
                dota.LeaveChatChannel(callback.result.channel_id);
            }
            if (callback.result.result != CMsgDOTAJoinChatChannelResponse.Result.JOIN_SUCCESS)
            {
                Console.WriteLine($"Failed to join {callback.result?.channel_name} ({callback.result?.channel_id})");
            }
            else
            {
                Console.WriteLine($"Sending message to {callback.result?.channel_name} ({callback.result?.channel_id})");
                SendMessage(callback.result.channel_id);
                Console.WriteLine($"Leaving channel {callback.result?.channel_name} ({callback.result?.channel_id})");
                dota.LeaveChatChannel(callback.result.channel_id);
            }
            totalProccessedCount++;            
            if (steamClient.IsConnected && targetChannels != null && targetChannels.Count != 0)
            {
                if (totalProccessedCount == targetChannels.Count)
                {                    
                    totalProccessedCount = 0;
                    dota.RequestChatChannelList();
                    return;
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
            if (!dota.Ready)
            {
                Console.WriteLine($"Starting dota...");
                dota.Start();
            }
        }

        private void OnDisconnected(SteamClient.DisconnectedCallback callback)
        {
            dotaIsReady = false;
            Console.WriteLine("Disconnected from Steam, reconnecting in 5 seconds...");
            System.Threading.Thread.Sleep(5000);
            if (dota != null && dota.Ready)
                dota.Stop();
            if (steamClient.IsConnected)
                steamClient.Disconnect();
            steamClient.Connect();
        }

        private void OnConnected(SteamClient.ConnectedCallback callback)
        {
            Console.WriteLine($"Connected to Steam! Logging in '{botConfig.login}'...");
            byte[] sentryHash = null;
            if (File.Exists("sentry.bin"))
            {
                // if we have a saved sentry file, read and sha-1 hash it
                byte[] sentryFile = File.ReadAllBytes("sentry.bin");
                sentryHash = CryptoHelper.SHAHash(sentryFile);
            }
            steamUser.LogOn(new SteamUser.LogOnDetails
            {
                Username = botConfig.login,
                Password = botConfig.password,
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

        private async void JoinChannels()
        {
            for (int i = 0; i < targetChannels.Count; i++)
            {
                dota.JoinChatChannel(targetChannels[i].channel_name, targetChannels[i].channel_type);
                await Task.Delay(10000);
            }
        }

        private void SendMessage(ulong channelId)
        {
            var message = botConfig.msgText;
            dota.SendChannelMessage(channelId, message);
        }
    }
}
