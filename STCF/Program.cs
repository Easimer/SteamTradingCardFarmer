/// Author: Easimer
/// Date: 2013. 07. 23.
/// Stage: Alpha
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;
using System.Net;
using SteamKit2;
using SteamKit2.Internal;
using ProtoBuf;


namespace STCF
{
    class Program
    {
        static SteamClient steamClient;
        static CallbackManager manager;

        static SteamUser steamUser;
        static SteamFriends steamFriends;
        static string logo = "..####...######...####...........######...####...#####...##...##..######..#####..\n.##........##....##..##..........##......##..##..##..##..###.###..##......##..##.\n..####.....##....##..............####....######..#####...##.#.##..####....#####..\n.....##....##....##..##..........##......##..##..##..##..##...##..##......##..##.\n..####.....##.....####...........##......##..##..##..##..##...##..######..##..##.\n"; //lel
        static bool isRunning;

        static string user, pass;
        static string authCode;
        static void Main(string[] args)
        {
            Console.Write(logo);
            if (!EasimerNet.CheckForInternetConnection())
            {
                EasimerNet.WriteError("No internet connection!");
            }
            Console.WriteLine("Enter username:");
            user = Console.ReadLine();
            Console.WriteLine("Enter password:");
            pass = Console.ReadLine();
            steamClient = new SteamClient();
            manager = new CallbackManager(steamClient);
            steamUser = steamClient.GetHandler<SteamUser>();
            steamFriends = steamClient.GetHandler<SteamFriends>();
            new Callback<SteamClient.ConnectedCallback>(OnConnected, manager);
            new Callback<SteamClient.DisconnectedCallback>(OnDisconnected, manager);
            new Callback<SteamUser.LoggedOnCallback>(OnLoggedOn, manager);
            new Callback<SteamUser.LoggedOffCallback>(OnLoggedOff, manager);
            new JobCallback<SteamUser.UpdateMachineAuthCallback>(OnMachineAuth, manager);

            isRunning = true;

            EasimerNet.WriteInfo("Connecting to Steam...");
            steamClient.Connect();
            steamFriends.SetPersonaState(EPersonaState.Online);
            while (isRunning)
            {
                manager.RunWaitCallbacks(TimeSpan.FromSeconds(1));
            }

        }
        static void OnConnected(SteamClient.ConnectedCallback callback)
        {
            if (callback.Result != EResult.OK)
            {
                Console.WriteLine("Unable to connect: {0}", callback.Result);

                isRunning = false;
                return;
            }

            Console.WriteLine("Connected to Steam! Logging in '{0}'...", user);

            byte[] sentryHash = null;
            if (File.Exists("sentry.bin"))
            {
                byte[] sentryFile = File.ReadAllBytes("sentry.bin");
                sentryHash = CryptoHelper.SHAHash(sentryFile);
            }

            steamUser.LogOn(new SteamUser.LogOnDetails
            {
                Username = user,
                Password = pass,
                AuthCode = authCode,
                SentryFileHash = sentryHash,
            });
        }

        static void OnDisconnected(SteamClient.DisconnectedCallback callback)
        {
            EasimerNet.WriteWarning("Disconnected. Reconnecting in 5...");

            Thread.Sleep(TimeSpan.FromSeconds(5));

            steamClient.Connect();
        }

        static void OnLoggedOn(SteamUser.LoggedOnCallback callback)
        {
            if (callback.Result == EResult.AccountLogonDenied)
            {
                EasimerNet.WriteInfo("This account is protected by SteamGuard...");
                Console.Write("Please enter the key sent to {0}: ", callback.EmailDomain);

                authCode = Console.ReadLine();
                return;
            }

            if (callback.Result != EResult.OK)
            {
                Console.WriteLine("Unable to log in: {0} {1}", callback.Result, callback.ExtendedResult);

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
            File.WriteAllBytes("sentry.bin", callback.Data);
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

            EasimerNet.WriteInfo("Done.");
        }
        public static void LaunchGame(ulong appid)
        {
            var clientMsg = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayedNoDataBlob);
            clientMsg.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed
            {
                game_id = appid,
            });
            steamClient.Send(clientMsg);
        }
        public static void GoOnline()
        {
            steamFriends.SetPersonaState(EPersonaState.Online);
        }
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
        public static void WriteError(string text)
        {
            Console.BackgroundColor = ConsoleColor.Red;
            Console.WriteLine("[ERROR] " + text);
            Console.BackgroundColor = ConsoleColor.Black;
        }
        public static void WriteWarning(string text)
        {
            Console.BackgroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[WARNING] " + text);
            Console.BackgroundColor = ConsoleColor.Black;
        }
        public static void WriteInfo(string text)
        {
            Console.BackgroundColor = ConsoleColor.Blue;
            Console.WriteLine("[INFO] " + text);
            Console.BackgroundColor = ConsoleColor.Black;
        }
        public static string GetOSLang()
        {
            string lang = System.Globalization.CultureInfo.CurrentCulture.ToString();
            return lang;
        }
    }
}
//After over 6 hours in development, hopefully it would have been worth the weight