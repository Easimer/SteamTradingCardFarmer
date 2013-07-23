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
namespace TF2Idler
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
            AppLocalization.SetLang();
            Console.WriteLine("[DEV]CURRENT LANG:"+EasimerNet.GetOSLang());
            Console.Write("..####...######...####...........######...####...#####...##...##..######..#####..\n.##........##....##..##..........##......##..##..##..##..###.###..##......##..##.\n..####.....##....##..............####....######..#####...##.#.##..####....#####..\n.....##....##....##..##..........##......##..##..##..##..##...##..##......##..##.\n..####.....##.....####...........##......##..##..##..##..##...##..######..##..##.\n");
            if (!EasimerNet.CheckForInternetConnection())
            {
                EasimerNet.WriteError(AppLocalization.noNetConn);
            }
            Console.WriteLine(AppLocalization.enterUser);
            user = Console.ReadLine();
            Console.WriteLine(AppLocalization.enterPass);
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

            EasimerNet.WriteInfo(AppLocalization.conn2Steam);

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
                Console.WriteLine(AppLocalization.unable2Conn, callback.Result);

                isRunning = false;
                return;
            }

            Console.WriteLine(AppLocalization.connLog, user);

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

            EasimerNet.WriteWarning(AppLocalization.reConn);

            Thread.Sleep(TimeSpan.FromSeconds(5));

            steamClient.Connect();
        }

        static void OnLoggedOn(SteamUser.LoggedOnCallback callback)
        {
            if (callback.Result == EResult.AccountLogonDenied)
            {
                EasimerNet.WriteInfo(AppLocalization.steamGuard);
                Console.Write(AppLocalization.enterKey, callback.EmailDomain);

                authCode = Console.ReadLine();
                return;
            }

            if (callback.Result != EResult.OK)
            {
                Console.WriteLine(AppLocalization.unable2Log, callback.Result, callback.ExtendedResult);

                isRunning = false;
                return;
            }

            EasimerNet.WriteInfo(AppLocalization.successLog);

            GetCMD();
        }

        static void OnLoggedOff(SteamUser.LoggedOffCallback callback)
        {
            Console.WriteLine(AppLocalization.logOff, callback.Result);
        }

        static void OnMachineAuth(SteamUser.UpdateMachineAuthCallback callback, JobID jobId)
        {
            EasimerNet.WriteInfo(AppLocalization.sentry);

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

            EasimerNet.WriteInfo(AppLocalization.done);
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
                Console.WriteLine(AppLocalization.enterAppID);
                LaunchGame(Convert.ToUInt64(Console.ReadLine()));
                GetCMD();
                return 1;
            }
            else if (tempcmd == "GoOnline()")
            {
                string profileName = steamFriends.GetPersonaName();
                EasimerNet.WriteInfo(profileName + AppLocalization.isOnline);
                GoOnline();
                GetCMD();
                return 1;
            }
            else if (tempcmd == "Exit()")
            {
                Console.WriteLine(AppLocalization.beepBoop);
                Environment.Exit(0);
                return 0;
            }
            else
            {
                EasimerNet.WriteError(AppLocalization.invalidCmd);
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
                Console.WriteLine(AppLocalization.check4Net);
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
            Console.WriteLine(AppLocalization.error + text);
            Console.BackgroundColor = ConsoleColor.Black;
        }
        /// <summary>
        /// Writes a message with white on yellow text
        /// </summary>
        /// <param name="text">Warning message</param>
        public static void WriteWarning(string text)
        {
            Console.BackgroundColor = ConsoleColor.Yellow;
            Console.WriteLine(AppLocalization.warning + text);
            Console.BackgroundColor = ConsoleColor.Black;
        }
        /// <summary>
        /// Writes a message with white on blue text
        /// </summary>
        /// <param name="text">Info message</param>
        public static void WriteInfo(string text)
        {
            Console.BackgroundColor = ConsoleColor.Blue;
            Console.WriteLine(AppLocalization.info + text);
            Console.BackgroundColor = ConsoleColor.Black;
        }
        /// <summary>
        /// Beep Boop
        /// </summary>
        public static void Beep()
        {
            Console.Beep();
        }
        public static string GetOSLang()
        {
            string lang = System.Globalization.CultureInfo.CurrentCulture.ToString();
            return lang;
        }
    }
    class AppLocalization
    {
        //fuck the static
        public static string noNetConn;
        public static string enterUser;
        public static string enterPass;
        public static string conn2Steam;
        public static string unable2Conn;
        public static string connLog;
        public static string reConn;
        public static string steamGuard;
        public static string enterKey;
        public static string unable2Log;
        public static string successLog;
        public static string logOff;
        public static string sentry;
        public static string done;
        public static string enterAppID;
        public static string isOnline;
        public static string check4Net;
        public static string error, info, warning;
        public static string beepBoop;
        public static string invalidCmd;
        public static void SetLang()
        {
        string curlang = EasimerNet.GetOSLang();

        if (curlang.Contains("en")) //en-US
        {
            noNetConn = "No internet connection!";
            enterUser = "Enter username:";
            enterPass = "Enter password:";
            conn2Steam = "Connecting to Steam...";
            unable2Conn = "Unable to connect to Steam: {0}";
            connLog = "Connected to Steam! Logging in '{0}'...";
            reConn = "Disconnected from Steam, reconnecting in 5...";
            steamGuard = "This account is SteamGuard protected!";
            enterKey = "Please enter the auth code sent to the email at {0}: ";
            unable2Log = "Unable to logon to Steam: {0} / {1}";
            successLog = "Successfully logged on!";
            logOff = "Logged off of Steam: {0}";
            sentry = "Updating sentryfile...";
            done = "Done.";
            enterAppID = "Enter a game appid (ex.: 420 = TF2, 730 = CS:GO)";
            isOnline = " is now Online!";
            check4Net = "Checking for internet connection...";
            error = "[ERROR] ";
            warning = "[WARNING] ";
            info = "[INFO] ";
            invalidCmd = "Invalid command";
            beepBoop = "Beep Boop Shutting Down...";
        }
        else if (curlang.Contains("hu")) //hungarian
        {
            noNetConn = "Nincs internet-kapcsolat!";
            enterUser = "Felhasználónév:";
            enterPass = "Jelszó:";
            conn2Steam = "Kapcsolódás a Steam-hez...";
            unable2Conn = "Sikertelen kapcsolódás: {0}";
            connLog = "Kapcsolódva a Steam-hez. Bejelentkezés mint '{0}'...";
            reConn = "Elveszett a kapcsolat a Steam-el, újrakapcsolódás...";
            steamGuard = "Ezt a profilt a Steam Guard védi!";
            enterKey = "Írd be a {0}-ra/re küldött kódot: ";
            unable2Log = "Sikertelen bejelentkezés: {0} / {1}";
            successLog = "Sikeres bejelentkezés!";
            logOff = "Kilépés a Steam-ről: {0}";
            sentry = "Sentry-fájl frissítése...";
            done = "Kész.";
            enterAppID = "Írj be egy játék ID-t (pl.: 420 = TF2, 730 = CS:GO)";
            isOnline = " Online lett!";
            check4Net = "Internet kapcsolat keresése...";
            error = "[HIBA] ";
            warning = "[FIGYELMEZTETÉS] ";
            info = "[INFÓ] ";
            invalidCmd = "Érvénytelen parancs";
            beepBoop = "Bip Bip Leállítás...";
        }
        else if (curlang.Contains("fr")) //french
        {
            noNetConn = "Pas de connexion internet!";
            enterUser = "S'il vous plaît entrer nom d'utilisateur:";
            enterPass = "S'il vous plaît entrer mot de passe:";
            conn2Steam = "Connectiong à Steam...";
            unable2Conn = "Impossible de se connecter à la Steam: {0}";
            connLog = "Connecté à Steam! Connexion en tant '{0}'...";
            reConn = "Déconnecté de Steam, reconnectant...";
            steamGuard = "Ce compte est protégé par SteamGuard!";
            enterKey = "Veuillez saisir le code Steam Guard envoyé à votre adresse email: {0} ";
            unable2Log = "Impossible de se connecter à Steam: {0} / {1}";
            successLog = "Réussir connecté!";
            logOff = "Déconnecté de Steam: {0}";
            sentry = "Mise à jour de sentryfile...";
            done = "Fait!";
            enterAppID = "Entrez un ID de jeu (ex.: 420 = TF2, 730 = CS:GO)";
            isOnline = " est en ligne!";
            check4Net = "Vérification de la connexion internet...";
            error = "[ERREUR] ";
            warning = "[ALERTE] ";
            info = "[INFOS] ";
            invalidCmd = "Commande invalide";
            beepBoop = "Beep Boop Arrêt...";
        }
        else //the default is english
        {
            noNetConn = "No internet connection!";
            enterUser = "Enter username:";
            enterPass = "Enter password:";
            conn2Steam = "Connecting to Steam...";
            unable2Conn = "Unable to connect to Steam: {0}";
            connLog = "Connected to Steam! Logging in '{0}'...";
            reConn = "Disconnected from Steam, reconnecting in 5...";
            steamGuard = "This account is SteamGuard protected!";
            enterKey = "Please enter the auth code sent to the email at {0}: ";
            unable2Log = "Unable to logon to Steam: {0} / {1}";
            successLog = "Successfully logged on!";
            logOff = "Logged off of Steam: {0}";
            sentry = "Updating sentryfile...";
            done = "Done.";
            enterAppID = "Enter a game appid (ex.: 420 = TF2, 730 = CS:GO)";
            isOnline = " is now Online!";
            check4Net = "Checking for internet connection...";
            error = "[ERROR] ";
            warning = "[WARNING] ";
            info = "[INFO] ";
            invalidCmd = "Invalid command";
            beepBoop = "Beep Boop Shutting Down...";
        }
        }
    }
}
