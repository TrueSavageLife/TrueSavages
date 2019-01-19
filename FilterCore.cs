using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Drawing;
using System.Collections.Generic;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using VirindiViewService;
using VirindiHotkeySystem;

namespace TrueSavages
{

    [FriendlyName("TrueSavages")]
    public class FilterCore : FilterBase
    {

        /* this is a .dll import for creating a debugging console window */
        [DllImport("kernel32")]
        static extern bool AllocConsole();

        /* this is used to send text to border of client window (when a user relogs a timer is displayed) */
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool SetWindowText(IntPtr hwnd, string lpString);

        /* used to handle keyboard/mouse events (click enter game when relogger countdown finishes) */
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool PostMessage(IntPtr hhwnd, uint msg, IntPtr wparam, UIntPtr lparam);

        /* importing beep sound (used for pk detect) */
        [DllImport("Kernel32.dll")]
        public static extern bool Beep(int dwFreq, int dwDuration);

        /* global variables */
        internal bool beep = false;
        internal bool logOnDeath = true;
        internal bool logOnVitae = true;
        internal bool relogging = false;
        internal bool currentlyRelogging = false;
        internal bool debug = false;
        internal System.Windows.Forms.Timer relogTimer = new System.Windows.Forms.Timer();
        internal System.Windows.Forms.Timer vitTimer = new System.Windows.Forms.Timer();
        internal DateTime startTime;
        internal TimeSpan remaining;
        internal int relogDuration = 3;
        internal int monarch = 0;
        internal string loggedBy;
        internal bool alertGuild = false;
        internal List<int> enemies = new List<int>();
        internal D3DObj PointArrow;
        internal bool pk = false;
        internal VirindiHotkeySystem.VHotkeyInfo pkHotkey;
        internal int me;
        internal int target;

        /* mouse/key constants */
        internal const int WM_MOUSEMOVE = 0x0200;
        internal const int WM_LBUTTONDOWN = 0x0201;
        internal const int WM_LBUTTONUP = 0x0202;
        internal const int WM_KEYDOWN = 0x0100;
        internal const int WM_KEYUP = 0x0101;
        internal const byte VK_PAUSE = 0x13;
        internal const byte VK_HOME = 0x24;

        /* startup function is called when Decal is injected into AC client */
        protected override void Startup()
        {

            /* if debug variable is set to true, create a new debugging console in a new thread */
            if (debug)
            {
                Thread debugger = new Thread(new ThreadStart(Debugger));
                debugger.Start();
            }

            base.Core.PluginInitComplete += PluginInitComplete;
            base.Core.PluginTermComplete += PluginTermComplete;
        }

        /* this event is called when our plugin is injected into the client, this even is fired, we use this to subscribe to events needed immediately */
        void PluginInitComplete(object sender, EventArgs e)
        {
            CoreManager.Current.CommandLineText += CheckCommands;
            CoreManager.Current.CharacterFilter.LoginComplete += LoginComplete;
            CoreManager.Current.CharacterFilter.Logoff += Logoff;
            CoreManager.Current.CharacterFilter.Login += Login;
            CoreManager.Current.CharacterFilter.Death += Death;
        }

        /* this event is called sometime after logout  we will use it to unsubscribe to certain events on log out to prevent memory leaks */
        void PluginTermComplete(object sender, EventArgs e)
        {
            Console.WriteLine("plugintermcomplete");
            CoreManager.Current.CommandLineText -= CheckCommands;
            CoreManager.Current.CharacterFilter.LoginComplete -= LoginComplete;
            CoreManager.Current.CharacterFilter.Logoff -= Logoff;
            CoreManager.Current.CharacterFilter.Login -= Login;
            CoreManager.Current.CharacterFilter.Death -= Death;
            CoreManager.Current.WorldFilter.CreateObject -= CreateObject;
            CoreManager.Current.WorldFilter.MoveObject -= MoveObject;
            CoreManager.Current.WorldFilter.ReleaseObject -= ReleaseObject;
            CoreManager.Current.ChatBoxMessage -= ParseChatBoxMessage;
            pkHotkey.Fired2 -= PkHotkeyFired;

            enemies.Clear();
            Console.WriteLine("Enemies List Cleared");
            Console.WriteLine("Enemies List Count: " + enemies.Count.ToString());
            if (relogging)
            {
                SetRelogTimer();
            }

        }

        /* create a debugging console */
        void Debugger()
        {
            AllocConsole();
            Console.BufferHeight = 9000;
            Console.SetWindowPosition(0, 0);
            Console.SetWindowSize(60, 30);
            Console.WriteLine("Debugging enabled.");
        }

        /* CreateObject and MoveObject are events that emit whenever an object is created or moves in AC, these can be any objects */
        void CreateObject(object sender, Decal.Adapter.Wrappers.CreateObjectEventArgs e)
        {
            try
            {
                ProcessWorldObject(e.New);
            }
            catch (Exception ex) { Console.WriteLine(ex.Message.ToString()); }
        }

        void MoveObject(object sender, Decal.Adapter.Wrappers.MoveObjectEventArgs e)
        {
            try
            {

                ProcessWorldObject(e.Moved);
            }
            catch (Exception ex) { Console.WriteLine(ex.Message.ToString()); }
        }

        /* Where most of our logic lies, pk detect and relog init happens here. */
        void ProcessWorldObject(WorldObject wo)
        {
            try
            {
                if (wo.ObjectClass != ObjectClass.Player)
                    return;


                if (wo.Name == CoreManager.Current.CharacterFilter.Name)
                    return;

                if (monarch == 0)
                    return;

                double playerDistance = GetDistanceFromPlayer(wo);
                int woMonarch = wo.Values(LongValueKey.Monarch);
                int distance = Convert.ToInt32(playerDistance);
                bool contained = enemies.Contains(wo.Id);

                //Console.WriteLine("Player: " + wo.Name + " Distance: " + playerDistance.ToString());

                if ((woMonarch != monarch))
                {
                    if (!contained)
                    {
                        if (beep)
                        {
                            Beep(5000, 50);
                        }

                        enemies.Add(wo.Id);               
                        Console.WriteLine("Enemy Added: " + wo.Id);
                        Console.WriteLine("Enemies List Count: " + enemies.Count.ToString());
                        LogText("Enemy Detected: " + wo.Name + " at [ " + wo.Coordinates().ToString() + " ]");
                    }
                }

                if (relogging && (woMonarch != monarch) && !currentlyRelogging && distance <= 125)
                {
                    loggedBy = wo.Name;
                    currentlyRelogging = true;
                    string coords = wo.Coordinates().ToString();               

                    CoreManager.Current.Actions.Logout();
                    Console.WriteLine("logged by " + loggedBy);
                }
            }
            catch (Exception ex) { Console.WriteLine(ex.Message.ToString()); }
        }

        /* this function is called to remove an enemy from our list of enemies */
        void RemoveEnemy(int id)
        {
            enemies.Remove(id);
            Console.WriteLine("Enemy Removed: " + id.ToString());
            Console.WriteLine("Enemies Count: " + enemies.Count.ToString());
        }

        /* this event is called when an object is released from decal we remove enemies if they are released from decal */
        void ReleaseObject(object sender, ReleaseObjectEventArgs e)
        {
            try
            {
                WorldObject wo = e.Released;

                if (wo.ObjectClass != ObjectClass.Player)
                    return;

                if (enemies.Contains(wo.Id))
                {
                    RemoveEnemy(wo.Id);
                }

                if (PointArrow != null && wo.Id == target)
                {
                    PointArrow.Dispose();
                    PointArrow = null;
                }
            }
            catch (Exception ex) { Console.WriteLine(ex.Message.ToString()); }
    
        }

        /* we initialize the relog timer here */
        void SetRelogTimer()
        {
            try
            {
                relogTimer.Tick += ParseRelogTimer;
                relogTimer.Interval = 100;
                startTime = DateTime.Now;
                relogTimer.Enabled = true;
            }
            catch (Exception ex) { Console.WriteLine(ex.Message.ToString()); }
        }

        /* every second our relogger will emit this event, we update the window text and check if a user logs in before time finishes */
        void ParseRelogTimer(object sender, EventArgs e)
        {
            try
            {

                remaining = (TimeSpan.FromMinutes(relogDuration) - (DateTime.Now - startTime));
                string countdown = string.Format("{0:00}:{1:00}", (int)remaining.Minutes, remaining.Seconds);
                

                if (remaining.Minutes <= 0 && remaining.Seconds <= 0)
                {
                    relogTimer.Tick -= ParseRelogTimer;
                    relogTimer.Enabled = false;
                    SendMouseClick(300, 407);
                    return;
                }

                SetWindowText(base.Host.Decal.Hwnd, "Logged by: " + loggedBy + " relogging in " + countdown);
            }
            catch (Exception ex) { Console.WriteLine(ex.Message.ToString()); }
        }
        
        /* check distance form player */
        double GetDistanceFromPlayer(WorldObject destObj)
        {
            if (CoreManager.Current.CharacterFilter.Id == 0)
                throw new ArgumentOutOfRangeException("destObj", "CharacterFilter.Id of 0");

            if (destObj.Id == 0)
                throw new ArgumentOutOfRangeException("destObj", "Object passed with an Id of 0");


            return CoreManager.Current.WorldFilter.Distance(CoreManager.Current.CharacterFilter.Id, destObj.Id) * 240;
        }

        /* we listen for chat messages a user sends, this is where we check for commands */
        void CheckCommands(object sender, ChatParserInterceptEventArgs e)
        {
            try
            {
                if (e.Text == null)
                    return;

                string cmd = e.Text.ToLower().Trim();

                if (cmd == "/tsl help")
                {
                    e.Eat = true;
                    LogText("/relog to enable/disable the relogger");
                    LogText("/duration <minutes> to set the relog timer (default is 3 minute)");
                    LogText("/beep to enable/disable beep sound on enemy detection");
                    LogText("/death to toggle log on death");
                    LogText("/vitae to toggle close client on >= 10% vit");
                    LogText("/pk toggles pk targeting plugin (only works in fellows)");
                }

                if (cmd == "/vitae")
                {
                    e.Eat = true;
                    logOnVitae = !logOnVitae;
                    LogText("Log on vitae is " + (logOnVitae ? "enabled" : "disabled"));
                }

                if (cmd == "/death")
                {
                    e.Eat = true;
                    logOnDeath = !logOnDeath;
                    LogText("Log on death is " + (logOnDeath ? "enabled" : "disabled"));
                }
                if (cmd == "/beep")
                {
                    e.Eat = true;
                    beep = !beep;
                    LogText("Beep is " + (beep ? "enabled" : "disabled"));
                }


                if (cmd.StartsWith("/duration"))
                {
                    e.Eat = true;
                    string[] splits = cmd.Split(' ');

                    if (splits.Length == 2)
                    {

                        if (!int.TryParse(splits[1], out int duration)) return;

                        relogDuration = duration;

                        Console.WriteLine("Changed relog duration to " + splits[1]);
                        LogText("Duration set to " + splits[1] + " minutes");
                    }
                }
                
                if (cmd == "/relog")
                {
                    e.Eat = true;

                    if (!relogging)
                    {
                        ToggleRelogger(true);
                        pk = false;
                        return;
                    }

                    ToggleRelogger(false);
                }

                if (cmd == "/pk")
                {
                    e.Eat = true;
                    
                    if (pk)
                    {
                        pk = false;
                        Console.WriteLine("Pk Mode Disabled");
                        LogText("Pk Mode Disabled");

                        if (PointArrow != null)
                        {
                            PointArrow.Dispose();
                            PointArrow = null;
                        }

                        return;
                    }

                    pk = true;

                    if (relogging)
                    {
                        ToggleRelogger(false);
                    }

                    Console.WriteLine("Pk Mode Enabled");
                    LogText("Pk Mode Enabled");
                }
            }
            catch (Exception ex) { Console.WriteLine(ex.Message.ToString()); }
        }

        void ToggleRelogger(bool on)
        {
            relogging = on;
            string enabled = on ? "Enabled" : "Disabled";
            Console.WriteLine("Relogger " + enabled);
            LogText("Relogger " + enabled);
        }


        /* we subscribe to these worldobject events */
        void ListenForObjects()
        {
            CoreManager.Current.WorldFilter.CreateObject += CreateObject;
            CoreManager.Current.WorldFilter.MoveObject += MoveObject;
            CoreManager.Current.WorldFilter.ReleaseObject += ReleaseObject;
        }

        /* clean window text */
        void CleanWindowText()
        {
            SetWindowText(base.Host.Decal.Hwnd, "Asheron's Call");
        }

        /* this event is fired when the user dies */
        void Death (object sender, DeathEventArgs e)
        {
            Console.WriteLine("Logged out on death");
            int vitae = CoreManager.Current.CharacterFilter.Vitae;

            if (!logOnDeath)
            {
                LogText("[Warning] You currently have log on Death disabled", 6);
            }

            if (vitae >= 15 && logOnDeath)
            {
                System.Environment.Exit(1);
            }

            if (logOnDeath)
            {
                ToggleRelogger(false);
                relogTimer.Enabled = false;
                SetWindowText(base.Host.Decal.Hwnd, e.Text);
                CoreManager.Current.Actions.Logout();
            }
        }

        void Logoff(object sender, EventArgs e)
        {
            Console.WriteLine("Logoff Complete");
        }
        
        void VitHandler (object sender, EventArgs e)
        {
            vitTimer.Stop();
            vitTimer.Tick -= VitHandler;

            if (logOnVitae)
            {
                ToggleRelogger(false);
                CoreManager.Current.Actions.Logout();
                SetWindowText(base.Host.Decal.Hwnd, "Logged off due to Vitae");
            }
        }

        /* this event is fired when login completes */
        void LoginComplete(object sender, EventArgs e)
        {
            Console.WriteLine("Login Complete");

            int vitae = CoreManager.Current.CharacterFilter.Vitae;

            if (vitae >= 10 && logOnVitae)
            {
                LogText("You have over 10% vitae, type /vitae or your character will be logged off", 6);

                vitTimer.Interval = 10 * 1000;
                vitTimer.Tick += VitHandler;
                vitTimer.Start();             
            }

            try
            {
                LogText("Plugin Started.");

                try
                {
                    monarch = CoreManager.Current.CharacterFilter.Monarch.Id;
                }
                catch (Exception ex)
                {
                    monarch = CoreManager.Current.CharacterFilter.Id;
                }

                me = CoreManager.Current.CharacterFilter.Id;
                Console.WriteLine("Character: " + CoreManager.Current.CharacterFilter.Name + " Monarch: " + monarch.ToString());

                pkHotkey = new VirindiHotkeySystem.VHotkeyInfo("TrueSavages", true, "pkplugin", "enables pk targeting", VK_HOME, false, false, false);
                VirindiHotkeySystem.VHotkeySystem.InstanceReal.AddHotkey(pkHotkey);
                CoreManager.Current.ChatBoxMessage += ParseChatBoxMessage;

                Console.WriteLine("Hotkey Added");

                pkHotkey.Fired2 += PkHotkeyFired;

                if (!relogging)
                {
                    LogText("type /tsl help for commands");
                    return;
                }

                LogText("Restarting Vtank");
                SendPause();

            }
            catch (Exception ex) { Console.WriteLine("LoginComplet: " + ex.Message.ToString()); }
        }

        void ParseChatBoxMessage(object sender, ChatTextInterceptEventArgs e)
        {
            try
            {
                if (e.Text == null || !pk)
                    return;
                
                string text = e.Text.ToLower().Replace('\"', ' ').Trim();

                if (text.StartsWith("[fellowship]") && text.Contains("target"))
                {
                    string[] splits = text.Replace('\"', ' ').Split(':');

                    if (!int.TryParse(splits[splits.Length - 1].Trim(), out int targetId)) return;

                    target = targetId;
                    CoreManager.Current.Actions.SelectItem(targetId);
                    Console.WriteLine("Target Selected: " + targetId.ToString());

                    if (PointArrow != null)
                    {
                        PointArrow.Dispose();
                        PointArrow = null;
                    }

                    if (me == targetId)
                    {
                        return;
                    }

                    PointArrow = base.Core.D3DService.PointToObject(targetId, Color.Red.ToArgb());
                }
            }
            catch (Exception ex) { Console.WriteLine(ex.Message.ToString()); }
        }

        void PkHotkeyFired(object s, VHotkeyInfo.cEatableFiredEventArgs e)
        {
            try
            {
                VirindiHotkeySystem.VHotkeyInfo keyInfo = (VirindiHotkeySystem.VHotkeyInfo)s;

                if (!pk || CoreManager.Current.Actions.ChatState || keyInfo.AltState || keyInfo.ControlState)
                    return;

                Console.WriteLine("Hotkey Fired!");

                if (Host.Actions.CurrentSelection == 0 || Core.WorldFilter[Host.Actions.CurrentSelection] == null)
                {
                    LogText("No Player Selected!", 5);
                    Console.WriteLine("No Player Selected");
                } else if (Host.Actions.CurrentSelection == CoreManager.Current.CharacterFilter.Id)
                {
                    LogText("Can't Select yourself", 5);
                    Console.WriteLine("Can't Select yourself");
                } else
                {
                    Console.WriteLine("Sending Target to Fellow Chat");
                    CoreManager.Current.Actions.InvokeChatParser("/f TARGET: " + base.Host.Actions.CurrentSelection.ToString());
                }
            }
            catch (Exception ex) { Console.WriteLine(ex.Message.ToString()); }
        }

        /* everytime a user logs in this is fired, subsribes to world object events */
        void Login (object sender, EventArgs e)
        {
            Console.WriteLine("Logging in");
            currentlyRelogging = false;
            CleanWindowText();
            try
            {

                if (relogging && (remaining.Minutes > 0 || remaining.Seconds > 0))
                {
                    Console.WriteLine("Early Login");
                    relogging = false;
                    relogTimer.Tick -= ParseRelogTimer;
                    relogTimer.Enabled = false;
                }


                ListenForObjects();
            }

            catch (Exception ex) { Console.WriteLine(ex.Message.ToString()); }
        }

        /* log text to chat for the plugin */
        void LogText(string text, int color)
        {
            base.Host.Actions.AddChatText("[TrueSavageLife] " + text, color);
        }

        void LogText(string text)
        {
            LogText(text, 3);
        }

        /* this function is used to click on a certain window handle. we use this to log back in */
        void SendMouseClick(int x, int y)
        {
            int loc = (y * 0x10000) + x;

            PostMessage(CoreManager.Current.Decal.Hwnd, WM_MOUSEMOVE, (IntPtr)0x00000000, (UIntPtr)loc);
            PostMessage(CoreManager.Current.Decal.Hwnd, WM_LBUTTONDOWN, (IntPtr)0x00000001, (UIntPtr)loc);
            PostMessage(CoreManager.Current.Decal.Hwnd, WM_LBUTTONUP, (IntPtr)0x00000000, (UIntPtr)loc);
        }

        /* VTANK can be enabled by the pause button on a keyboard, this function presses the pause key in-game */
        void SendPause()
        {
            Console.WriteLine("Enabling Vtank");
            PostMessage(CoreManager.Current.Decal.Hwnd, WM_KEYDOWN, (IntPtr)VK_PAUSE, (UIntPtr)0x00450001);
            PostMessage(CoreManager.Current.Decal.Hwnd, WM_KEYUP, (IntPtr)VK_PAUSE, (UIntPtr)0xC0450001);
        }

        void SendHome()
        {
            Console.WriteLine("Selecting Target");
            PostMessage(CoreManager.Current.Decal.Hwnd, WM_KEYDOWN, (IntPtr)VK_HOME, (UIntPtr)0x00450001);
            PostMessage(CoreManager.Current.Decal.Hwnd, WM_KEYUP, (IntPtr)VK_HOME, (UIntPtr)0xC0450001);
        }

                

        protected override void Shutdown()
        { }
    }

}
