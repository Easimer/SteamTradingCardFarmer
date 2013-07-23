using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;
using SteamKit2;
using ProtoBuf;
using SteamKit2.Internal;
using System.Net;
namespace STCF
{
    /// <summary>
    /// Program
    /// </summary>
    class Program
    {
        static SteamClient steamClient;
        static CallbackManager manager;

        static SteamUser steamUser;
        static SteamFriends steamFriends;

        static bool isRunning;

        static string user, pass;
        static string authCode;
        static void Main(string[] args)
        {
            Console.Write("..####...######...####...........######...####...#####...##...##..######..#####..\n.##........##....##..##..........##......##..##..##..##..###.###..##......##..##.\n..####.....##....##..............####....######..#####...##.#.##..####....#####..\n.....##....##....##..##..........##......##..##..##..##..##...##..##......##..##.\n..####.....##.....####...........##......##..##..##..##..##...##..######..##..##.\n");
            if (!EasimerNet.CheckForInternetConnection())
            {
                EasimerNet.WriteError("No internet connection!");
            }
            Console.WriteLine("Enter username");
            user = Console.ReadLine();
            Console.WriteLine("Enter password");
            pass = Console.ReadLine();
            // create our steamclient instance
            steamClient = new SteamClient();
            // create the callback manager which will route callbacks to function calls
            manager = new CallbackManager(steamClient);

            // get the steamuser handler, which is used for logging on after successfully connecting
            steamUser = steamClient.GetHandler<SteamUser>();
            // get the steam friends handler, which is used for interacting with friends on the network after logging on
            steamFriends = steamClient.GetHandler<SteamFriends>();

            // register a few callbacks we're interested in
            // these are registered upon creation to a callback manager, which will then route the callbacks
            // to the functions specified
            new Callback<SteamClient.ConnectedCallback>(OnConnected, manager);
            new Callback<SteamClient.DisconnectedCallback>(OnDisconnected, manager);

            new Callback<SteamUser.LoggedOnCallback>(OnLoggedOn, manager);
            new Callback<SteamUser.LoggedOffCallback>(OnLoggedOff, manager);

            // this callback is triggered when the steam servers wish for the client to store the sentry file
            new JobCallback<SteamUser.UpdateMachineAuthCallback>(OnMachineAuth, manager);

            isRunning = true;

            EasimerNet.WriteInfo("Connecting to Steam...");

            // initiate the connection
            steamClient.Connect();
            steamFriends.SetPersonaState(EPersonaState.Online);
            // create our callback handling loop
            while (isRunning)
            {
                // in order for the callbacks to get routed, they need to be handled by the manager
                manager.RunWaitCallbacks(TimeSpan.FromSeconds(1));
            }
            
        }
        static void OnConnected(SteamClient.ConnectedCallback callback)
        {
            if (callback.Result != EResult.OK)
            {
                Console.WriteLine("Unable to connect to Steam: {0}", callback.Result);

                isRunning = false;
                return;
            }

            Console.WriteLine("Connected to Steam! Logging in '{0}'...", user);

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

                // our subsequent logons use the hash of the sentry file as proof of ownership of the file
                // this will also be null for our first (no authcode) and second (authcode only) logon attempts
                SentryFileHash = sentryHash,
            });
        }

        static void OnDisconnected(SteamClient.DisconnectedCallback callback)
        {
            // after recieving an AccountLogonDenied, we'll be disconnected from steam
            // so after we read an authcode from the user, we need to reconnect to begin the logon flow again

            EasimerNet.WriteWarning("Disconnected from Steam, reconnecting in 5...");

            Thread.Sleep(TimeSpan.FromSeconds(5));

            steamClient.Connect();
        }

        static void OnLoggedOn(SteamUser.LoggedOnCallback callback)
        {
            if (callback.Result == EResult.AccountLogonDenied)
            {
                EasimerNet.WriteInfo("This account is SteamGuard protected!");
                Console.Write("Please enter the auth code sent to the email at {0}: ", callback.EmailDomain);

                authCode = Console.ReadLine();
                return;
            }

            if (callback.Result != EResult.OK)
            {
                Console.WriteLine("Unable to logon to Steam: {0} / {1}", callback.Result, callback.ExtendedResult);

                isRunning = false;
                return;
            }

            EasimerNet.WriteInfo("Successfully logged on!");

            GetCMD();
        }

        static void OnLoggedOff(SteamUser.LoggedOffCallback callback)
        {
            Console.WriteLine("Logged off of Steam: {0}", callback.Result);
        }

        static void OnMachineAuth(SteamUser.UpdateMachineAuthCallback callback, JobID jobId)
        {
            EasimerNet.WriteInfo("Updating sentryfile...");

            byte[] sentryHash = CryptoHelper.SHAHash(callback.Data);

            // write out our sentry file
            // ideally we'd want to write to the filename specified in the callback
            // but then this sample would require more code to find the correct sentry file to read during logon
            // for the sake of simplicity, we'll just use "sentry.bin"
            File.WriteAllBytes("sentry.bin", callback.Data);

            // inform the steam servers that we're accepting this sentry file
            steamUser.SendMachineAuthResponse(new SteamUser.MachineAuthDetails
            {
                JobID = jobId,

                FileName = callback.FileName,

                BytesWritten = callback.BytesToWrite,
                FileSize = callback.Data.Length,
                Offset = callback.Offset,

                Result = EResult.OK,
                LastError = 0,

                OneTimePassword = callback.OneTimePassword,

                SentryFileHash = sentryHash,
            });

            EasimerNet.WriteInfo("Done!");
        }
        /// <summary>
        /// Launch a game
        /// </summary>
        /// <param name="appid">Game's appid</param>
        public static void LaunchGame(ulong appid)
        {
            var clientMsg = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayedNoDataBlob);
            clientMsg.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed
            {
                game_id = appid,
            });
            steamClient.Send(clientMsg);
        }
        /// <summary>
        /// Sets user's state to online
        /// </summary>
        public static void GoOnline()
        {
            steamFriends.SetPersonaState(EPersonaState.Online);
        }
        /// <summary>
        /// Commands
        /// </summary>
        /// <returns></returns>
        static int GetCMD()
        {
            string tempcmd = Console.ReadLine();
            if (tempcmd.Contains("LaunchGame()"))
            {
                Console.WriteLine("Enter a game appid (ex.: 420 = TF2, 730 = CS:GO)");
                LaunchGame(Convert.ToUInt64(Console.ReadLine()));
                GetCMD();
                return 1;
            }
            else if (tempcmd == "GoOnline()")
            {
                string profileName = steamFriends.GetPersonaName();
                EasimerNet.WriteInfo(profileName + " is now Online!");
                GoOnline();
                GetCMD();
                return 1;
            }
            else if (tempcmd == "Exit()")
            {
                Console.WriteLine("Beep Boop Shutting Down...");
                Environment.Exit(0);
                return 0;
            }
            else
            {
                EasimerNet.WriteError("Invalid command");
                GetCMD();
                return 0;
            }
            
        }
    }

    class EasimerNet
    {
        /// <summary>
        /// Checks internet connection
        /// </summary>
        /// <param name="url">URL to download</param>
        /// <returns>True or False</returns>
        public static bool CheckForInternetConnection(string url = null)
        {
            if (url == null)
            {
                url = "http://www.google.com";
            }
            try
            {
                Console.WriteLine("Checking for internet connection...");
                using (var client = new WebClient())
                using (var stream = client.OpenRead(url))
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
        /// <summary>
        /// Writes a message with white on red text
        /// </summary>
        /// <param name="text">Error message</param>
        public static void WriteError(string text)
        {
            Console.BackgroundColor = ConsoleColor.Red;
            Console.WriteLine("[ERROR] " + text);
            Console.BackgroundColor = ConsoleColor.Black;
        }
        /// <summary>
        /// Writes a message with white on yellow text
        /// </summary>
        /// <param name="text">Warning message</param>
        public static void WriteWarning(string text)
        {
            Console.BackgroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[WARNING] " + text);
            Console.BackgroundColor = ConsoleColor.Black;
        }
        /// <summary>
        /// Writes a message with white on blue text
        /// </summary>
        /// <param name="text">Info message</param>
        public static void WriteInfo(string text)
        {
            Console.BackgroundColor = ConsoleColor.Blue;
            Console.WriteLine("[INFO] " + text);
            Console.BackgroundColor = ConsoleColor.Black;
        }
        /// <summary>
        /// Beep Boop
        /// </summary>
        public static void Beep()
        {
            Console.Beep();
        }
    }
}
