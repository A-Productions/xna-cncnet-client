﻿using System;
using System.Collections.Generic;
using System.Linq;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;
using Microsoft.Xna.Framework;
using ClientCore;
using System.IO;
using Rampastring.Tools;
using ClientCore.Statistics;
using DTAClient.DXGUI.Generic;
using DTAClient.Domain.Multiplayer;
using ClientGUI;

namespace DTAClient.DXGUI.Multiplayer.GameLobby
{
    /// <summary>
    /// A generic base class for multiplayer game lobbies (CnCNet and LAN).
    /// </summary>
    public abstract class MultiplayerGameLobby : GameLobbyBase, ISwitchable
    {
        public MultiplayerGameLobby(WindowManager windowManager, string iniName, 
            TopBar topBar, List<GameMode> GameModes)
            : base(windowManager, iniName, GameModes, true)
        {
            TopBar = topBar;

            chatBoxCommands = new List<ChatBoxCommand>
            {
                new ChatBoxCommand("HIDEMAPS", "Hide map list (game host only)", true,
                    s => HideMapList()),
                new ChatBoxCommand("SHOWMAPS", "Show map list (game host only)", true,
                    s => ShowMapList()),
                new ChatBoxCommand("FRAMESENDRATE", "Change order lag / FrameSendRate (default 7) (game host only)", true,
                    s => SetFrameSendRate(s)),
                new ChatBoxCommand("MAXAHEAD", "Change MaxAhead (default 0) (game host only)", true,
                    s => SetMaxAhead(s)),
                new ChatBoxCommand("PROTOCOLVERSION", "Change ProtocolVersion (default 2) (game host only)", true,
                    s => SetProtocolVersion(s)),
                new ChatBoxCommand("LOADMAP", "Load custom map with given filename", true, s => LoadCustomMap($"Maps\\Custom\\{s}", true)),
                new ChatBoxCommand("RANDOMSTARTS", "Enables completely random starting locations (Tiberian Sun based games only)", true,
                    s => SetStartingLocationClearance(s)),
            };
        }

        protected XNACheckBox[] ReadyBoxes;

        protected ChatListBox lbChatMessages;
        protected XNASuggestionTextBox tbChatInput;
        protected XNAClientButton btnLockGame;

        protected bool IsHost = false;

        protected bool Locked = false;

        protected EnhancedSoundEffect sndJoinSound;
        protected EnhancedSoundEffect sndLeaveSound;
        protected EnhancedSoundEffect sndMessageSound;
        protected EnhancedSoundEffect sndGetReadySound;

        protected TopBar TopBar;

        protected int FrameSendRate { get; set; } = 7;

        /// <summary>
        /// Controls the MaxAhead parameter. The default value of 0 means that 
        /// the value is not written to spawn.ini, which allows the spawner the
        /// calculate and assign the MaxAhead value.
        /// </summary>
        protected int MaxAhead { get; set; }

        protected int ProtocolVersion { get; set; } = 2;

        protected List<ChatBoxCommand> chatBoxCommands;

        private FileSystemWatcher fsw;

        private bool gameSaved = false;

        private string[] allowedGameModes = ClientConfiguration.Instance.GetAllowedGameModes.Split(',');


        /// <summary>
        /// Allows derived classes to add their own chat box commands.
        /// </summary>
        /// <param name="command">The command to add.</param>
        protected void AddChatBoxCommand(ChatBoxCommand command)
        {
            chatBoxCommands.Add(command);
        }

        public override void Initialize()
        {
            Name = "MultiplayerGameLobby";

            base.Initialize();

            InitPlayerOptionDropdowns();

            ReadyBoxes = new XNACheckBox[MAX_PLAYER_COUNT];

            int readyBoxX = GameOptionsIni.GetIntValue(Name, "PlayerReadyBoxX", 7);
            int readyBoxY = GameOptionsIni.GetIntValue(Name, "PlayerReadyBoxY", 4);

            for (int i = 0; i < MAX_PLAYER_COUNT; i++)
            {
                XNACheckBox chkPlayerReady = new XNACheckBox(WindowManager);
                chkPlayerReady.Name = "chkPlayerReady" + i;
                chkPlayerReady.Checked = false;
                chkPlayerReady.AllowChecking = false;
                chkPlayerReady.ClientRectangle = new Rectangle(readyBoxX, ddPlayerTeams[i].Y + readyBoxY,
                    0, 0);

                PlayerOptionsPanel.AddChild(chkPlayerReady);

                chkPlayerReady.DisabledClearTexture = chkPlayerReady.ClearTexture;
                chkPlayerReady.DisabledCheckedTexture = chkPlayerReady.CheckedTexture;

                ReadyBoxes[i] = chkPlayerReady;
                ddPlayerSides[i].AddItem("Spectator", AssetLoader.LoadTexture("spectatoricon.png"));
            }

            ddGameMode.ClientRectangle = new Rectangle(
                MapPreviewBox.X - 12 - ddGameMode.Width,
                MapPreviewBox.Y, ddGameMode.Width,
                ddGameMode.Height);

            lblGameModeSelect.ClientRectangle = new Rectangle(
                btnLaunchGame.X, ddGameMode.Y + 1,
                lblGameModeSelect.Width, lblGameModeSelect.Height);

            lbMapList.ClientRectangle = new Rectangle(btnLaunchGame.X, 
                MapPreviewBox.Y + 23,
                MapPreviewBox.X - btnLaunchGame.X - 12,
                MapPreviewBox.Height - 23);

            lbChatMessages = new ChatListBox(WindowManager);
            lbChatMessages.Name = "lbChatMessages";
            lbChatMessages.ClientRectangle = new Rectangle(lbMapList.X, 
                GameOptionsPanel.Y,
               lbMapList.Width, GameOptionsPanel.Height - 24);
            lbChatMessages.DrawMode = PanelBackgroundImageDrawMode.STRETCHED;
            lbChatMessages.BackgroundTexture = AssetLoader.CreateTexture(new Color(0, 0, 0, 128), 1, 1);
            lbChatMessages.LineHeight = 16;
            lbChatMessages.DrawOrder = -1;
            lbChatMessages.UpdateOrder = -1;

            tbChatInput = new XNASuggestionTextBox(WindowManager);
            tbChatInput.Name = "tbChatInput";
            tbChatInput.Suggestion = "Type here to chat..";
            tbChatInput.ClientRectangle = new Rectangle(lbChatMessages.X, 
                lbChatMessages.Bottom + 3,
                lbChatMessages.Width, 21);
            tbChatInput.MaximumTextLength = 150;
            tbChatInput.EnterPressed += TbChatInput_EnterPressed;
            tbChatInput.DrawOrder = 1;
            tbChatInput.UpdateOrder = 1;

            btnLockGame = new XNAClientButton(WindowManager);
            btnLockGame.Name = "btnLockGame";
            btnLockGame.ClientRectangle = new Rectangle(btnLaunchGame.Right + 12,
                btnLaunchGame.Y, 133, 23);
            btnLockGame.Text = "Lock Game";
            btnLockGame.LeftClick += BtnLockGame_LeftClick;

            AddChild(lbChatMessages);
            AddChild(tbChatInput);
            AddChild(btnLockGame);

            MapPreviewBox.LocalStartingLocationSelected += MapPreviewBox_LocalStartingLocationSelected;
            MapPreviewBox.StartingLocationApplied += MapPreviewBox_StartingLocationApplied;

            InitializeWindow();

            sndJoinSound = new EnhancedSoundEffect("joingame.wav");
            sndLeaveSound = new EnhancedSoundEffect("leavegame.wav");
            sndMessageSound = new EnhancedSoundEffect("message.wav");
            sndGetReadySound = new EnhancedSoundEffect("getready.wav", 0.0, 0.0, 5.0f);

            if (SavedGameManager.AreSavedGamesAvailable())
            {
                fsw = new FileSystemWatcher(ProgramConstants.GamePath + "Saved Games", "*.NET");
                fsw.Created += fsw_Created;
                fsw.Changed += fsw_Created;
                fsw.EnableRaisingEvents = false;
            }
            else
                Logger.Log("MultiplayerGameLobby: Saved games are not available!");

            CenterOnParent();

            // To move the lblMapAuthor label into its correct position
            // if it was moved in the theme description INI file
            LoadDefaultMap();
        }

        private void fsw_Created(object sender, FileSystemEventArgs e)
        {
            AddCallback(new Action<FileSystemEventArgs>(FSWEvent), e);
        }

        private void FSWEvent(FileSystemEventArgs e)
        {
            Logger.Log("FSW Event: " + e.FullPath);

            if (Path.GetFileName(e.FullPath) == "SAVEGAME.NET")
            {
                if (!gameSaved)
                {
                    bool success = SavedGameManager.InitSavedGames();

                    if (!success)
                        return;
                }

                gameSaved = true;

                SavedGameManager.RenameSavedGame();
            }
        }

        protected override void StartGame()
        {
            if (fsw != null)
                fsw.EnableRaisingEvents = true;

            base.StartGame();
        }

        protected override void GameProcessExited()
        {
            gameSaved = false;

            if (fsw != null)
                fsw.EnableRaisingEvents = false;

            base.GameProcessExited();

            if (IsHost)
            {
                GenerateGameID();
                DdGameMode_SelectedIndexChanged(null, EventArgs.Empty); // Refresh ranks
            }
        }

        private void GenerateGameID()
        {
            int i = 0;

            while (i < 20)
            {
                string s = DateTime.Now.Day.ToString() +
                    DateTime.Now.Month.ToString() +
                    DateTime.Now.Hour.ToString() +
                    DateTime.Now.Minute.ToString();

                UniqueGameID = int.Parse(i.ToString() + s);

                if (StatisticsManager.Instance.GetMatchWithGameID(UniqueGameID) == null)
                    break;

                i++;
            }
        }

        private void BtnLockGame_LeftClick(object sender, EventArgs e)
        {
            HandleLockGameButtonClick();
        }

        protected virtual void HandleLockGameButtonClick()
        {
            if (Locked)
                UnlockGame(true);
            else
                LockGame();
        }

        protected abstract void LockGame();

        protected abstract void UnlockGame(bool manual);

        private void TbChatInput_EnterPressed(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(tbChatInput.Text))
                return;

            if (tbChatInput.Text.StartsWith("/"))
            {
                string text = tbChatInput.Text;
                string command;
                string parameters;

                int spaceIndex = text.IndexOf(' ');

                if (spaceIndex == -1)
                {
                    command = text.Substring(1).ToUpper();
                    parameters = string.Empty;
                }
                else
                {
                    command = text.Substring(1, spaceIndex - 1);
                    parameters = text.Substring(spaceIndex + 1);
                }
                
                tbChatInput.Text = string.Empty;

                foreach (var chatBoxCommand in chatBoxCommands)
                {
                    if (command.ToUpper() == chatBoxCommand.Command)
                    {
                        if (!IsHost && chatBoxCommand.HostOnly)
                        {
                            AddNotice(string.Format("/{0} is for game hosts only.", chatBoxCommand.Command));
                            return;
                        }

                        chatBoxCommand.Action(parameters);
                        return;
                    }
                }

                // The user typed a nonexistant command
                AddNotice("Possible commands:");
                foreach (var chatBoxCommand in chatBoxCommands)
                {
                    AddNotice(string.Format("/{0}: {1}", 
                        chatBoxCommand.Command, chatBoxCommand.Description));
                }

                return;
            }

            SendChatMessage(tbChatInput.Text);
            tbChatInput.Text = string.Empty;
        }

        private void SetFrameSendRate(string value)
        {
            bool success = int.TryParse(value, out int intValue);

            if (!success)
            {
                AddNotice("Command syntax: /FrameSendRate <number>");
                return;
            }

            FrameSendRate = intValue;
            AddNotice("FrameSendRate has been changed to " + intValue);

            OnGameOptionChanged();
            ClearReadyStatuses();
        }

        private void SetMaxAhead(string value)
        {
            bool success = int.TryParse(value, out int intValue);

            if (!success)
            {
                AddNotice("Command syntax: /MaxAhead <number>");
                return;
            }

            MaxAhead = intValue;
            AddNotice("MaxAhead has been changed to " + intValue);

            OnGameOptionChanged();
            ClearReadyStatuses();
        }

        private void SetProtocolVersion(string value)
        {
            bool success = int.TryParse(value, out int intValue);

            if (!success)
            {
                AddNotice("Command syntax: /ProtocolVersion <number>.");
                return;
            }

            if (!(intValue == 0 || intValue == 2))
            {
                AddNotice("ProtocolVersion only allows values 0 and 2.");
                return;
            }

            ProtocolVersion = intValue;
            AddNotice("ProtocolVersion has been changed to " + intValue);

            OnGameOptionChanged();
            ClearReadyStatuses();
        }

        private void SetStartingLocationClearance(string value)
        {
            bool removeStartingLocations = Conversions.BooleanFromString(value, RemoveStartingLocations);

            SetRandomStartingLocations(removeStartingLocations);

            OnGameOptionChanged();
            ClearReadyStatuses();
        }

        /// <summary>
        /// Enables or disables completely random starting locations and informs
        /// the user accordingly.
        /// </summary>
        /// <param name="newValue">The new value of completely random starting locations.</param>
        protected void SetRandomStartingLocations(bool newValue)
        {
            if (newValue != RemoveStartingLocations)
            {
                RemoveStartingLocations = newValue;
                if (RemoveStartingLocations)
                    AddNotice("The game host has enabled completely random starting locations (only works for regular maps)..");
                else
                    AddNotice("The game host has disabled completely random starting locations.");
            }
        }

        protected abstract void SendChatMessage(string message);

        /// <summary>
        /// Changes the game lobby's UI depending on whether the local player is the host.
        /// </summary>
        /// <param name="isHost">Determines whether the local player is the host of the game.</param>
        protected void Refresh(bool isHost)
        {
            IsHost = isHost;
            Locked = false;

            UpdateMapPreviewBoxEnabledStatus();
            //MapPreviewBox.EnableContextMenu = IsHost;

            btnLaunchGame.Text = IsHost ? "Launch Game" : "I'm Ready";

            if (IsHost)
            {
                ShowMapList();

                btnLockGame.Text = "Lock Game";
                btnLockGame.Enabled = true;
                btnLockGame.Visible = true;

                foreach (GameLobbyDropDown dd in DropDowns)
                {
                    dd.InputEnabled = true;
                    dd.SelectedIndex = dd.UserDefinedIndex;
                }

                foreach (GameLobbyCheckBox checkBox in CheckBoxes)
                {
                    checkBox.InputEnabled = true;
                    checkBox.Checked = checkBox.UserDefinedValue;
                }

                GenerateGameID();
            }
            else
            {
                HideMapList();

                btnLockGame.Enabled = false;
                btnLockGame.Visible = false;

                foreach (GameLobbyDropDown dd in DropDowns)
                    dd.InputEnabled = false;

                foreach (GameLobbyCheckBox checkBox in CheckBoxes)
                    checkBox.InputEnabled = false;
            }

            LoadDefaultMap();

            lbChatMessages.Clear();
            lbChatMessages.TopIndex = 0;

            if (SavedGameManager.GetSaveGameCount() > 0)
            {
                lbChatMessages.AddItem("Multiplayer saved games from a previous match have been detected. " +
                    "The saved games of the previous match will be deleted if you create new saves during this match.",
                    Color.Yellow, true);
            }
        }

        private void HideMapList()
        {
            lbChatMessages.ClientRectangle = new Rectangle(lbMapList.X,
                PlayerOptionsPanel.Y,
                lbMapList.Width,
                MapPreviewBox.Bottom - PlayerOptionsPanel.Y);
            lbChatMessages.Name = "lbChatMessages_Player";

            tbChatInput.ClientRectangle = new Rectangle(lbChatMessages.X,
                lbChatMessages.Bottom + 3,
                lbChatMessages.Width, 21);
            tbChatInput.Name = "tbChatInput_Player";

            ddGameMode.Disable();
            lblGameModeSelect.Disable();
            lbMapList.Disable();
            tbMapSearch.Disable();

            lbChatMessages.GetAttributes(ThemeIni);
            tbChatInput.GetAttributes(ThemeIni);
            lbMapList.GetAttributes(ThemeIni);
        }

        private void ShowMapList()
        {
            lbMapList.ClientRectangle = new Rectangle(btnLaunchGame.X,
                MapPreviewBox.Y + 23,
                MapPreviewBox.X - btnLaunchGame.X - 12,
                MapPreviewBox.Height - 23);

            lbChatMessages.ClientRectangle = new Rectangle(lbMapList.X,
                GameOptionsPanel.Y,
                lbMapList.Width, GameOptionsPanel.Height - 26);
            lbChatMessages.Name = "lbChatMessages_Host";

            tbChatInput.ClientRectangle = new Rectangle(lbChatMessages.X,
                lbChatMessages.Bottom + 3,
                lbChatMessages.Width, 21);
            tbChatInput.Name = "tbChatInput_Host";

            ddGameMode.Enable();
            lblGameModeSelect.Enable();
            lbMapList.Enable();
            tbMapSearch.Enable();

            lbChatMessages.GetAttributes(ThemeIni);
            tbChatInput.GetAttributes(ThemeIni);
            lbMapList.GetAttributes(ThemeIni);
        }

        private void MapPreviewBox_LocalStartingLocationSelected(object sender, LocalStartingLocationEventArgs e)
        {
            int mTopIndex = Players.FindIndex(p => p.Name == ProgramConstants.PLAYERNAME);

            if (mTopIndex == -1 || Players[mTopIndex].SideId == ddPlayerSides[0].Items.Count - 1)
                return;

            ddPlayerStarts[mTopIndex].SelectedIndex = e.StartingLocationIndex;
        }

        private void MapPreviewBox_StartingLocationApplied(object sender, EventArgs e)
        {
            ClearReadyStatuses();
            CopyPlayerDataToUI();
            BroadcastPlayerOptions();
        }

        /// <summary>
        /// Handles the user's click on the "Launch Game" / "I'm Ready" button.
        /// If the local player is the game host, checks if the game can be launched and then
        /// launches the game if it's allowed. If the local player isn't the game host,
        /// sends a ready request.
        /// </summary>
        protected override void BtnLaunchGame_LeftClick(object sender, EventArgs e)
        {
            if (!IsHost)
            {
                RequestReadyStatus();
                return;
            }

            if (!Locked)
            {
                LockGameNotification();
                return;
            }

            List<int> occupiedColorIds = new List<int>();
            foreach (PlayerInfo player in Players)
            {
                if (occupiedColorIds.Contains(player.ColorId) && player.ColorId > 0)
                {
                    SharedColorsNotification();
                    return;
                }

                occupiedColorIds.Add(player.ColorId);
            }

            if (AIPlayers.Count(pInfo => pInfo.SideId == ddPlayerSides[0].Items.Count - 1) > 0)
            {
                AISpectatorsNotification();
                return;
            }

            if (Map.EnforceMaxPlayers)
            {
                foreach (PlayerInfo pInfo in Players)
                {
                    if (pInfo.StartingLocation == 0)
                        continue;

                    if (Players.Concat(AIPlayers).ToList().Find(
                        p => p.StartingLocation == pInfo.StartingLocation && 
                        p.Name != pInfo.Name) != null)
                    {
                        SharedStartingLocationNotification();
                        return;
                    }
                }

                for (int aiId = 0; aiId < AIPlayers.Count; aiId++)
                {
                    int startingLocation = AIPlayers[aiId].StartingLocation;

                    if (startingLocation == 0)
                        continue;

                    int index = AIPlayers.FindIndex(aip => aip.StartingLocation == startingLocation);

                    if (index > -1 && index != aiId)
                    {
                        SharedStartingLocationNotification();
                        return;
                    }
                }

                int totalPlayerCount = Players.Count(p => p.SideId < ddPlayerSides[0].Items.Count - 1)
                    + AIPlayers.Count;

                if (totalPlayerCount < Map.MinPlayers)
                {
                    InsufficientPlayersNotification();
                    return;
                }

                if (Map.EnforceMaxPlayers && totalPlayerCount > Map.MaxPlayers)
                {
                    TooManyPlayersNotification();
                    return;
                }
            }

            int iId = 0;
            foreach (PlayerInfo player in Players)
            {
                iId++;

                if (player.Name == ProgramConstants.PLAYERNAME)
                    continue;

                if (!player.Verified)
                {
                    NotVerifiedNotification(iId - 1);
                    return;
                }

                if (!player.Ready)
                {
                    if (player.IsInGame)
                    {
                        StillInGameNotification(iId - 1);
                    }
                    else
                    {
                        GetReadyNotification();
                    }

                    return;
                }
            }

            HostLaunchGame();
        }

        protected virtual void LockGameNotification()
        {
            AddNotice("You need to lock the game room before launching the game.");
        }

        protected virtual void SharedColorsNotification()
        {
            AddNotice("Multiple human players cannot share the same color.");
        }

        protected virtual void AISpectatorsNotification()
        {
            AddNotice("AI players don't enjoy spectating matches. They want some action!");
        }

        protected virtual void SharedStartingLocationNotification()
        {
            AddNotice("Multiple players cannot share the same starting location on this map.");
        }

        protected virtual void NotVerifiedNotification(int playerIndex)
        {
            if (playerIndex > -1 && playerIndex < Players.Count)
            {
                AddNotice(string.Format("Unable to launch game; player {0} hasn't been verified.", Players[playerIndex].Name));
            }
        }

        protected virtual void StillInGameNotification(int playerIndex)
        {
            if (playerIndex > -1 && playerIndex < Players.Count)
            {
                AddNotice("Unable to launch game; player " + Players[playerIndex].Name + " is still playing the game you started previously.");
            }
        }

        protected virtual void GetReadyNotification()
        {
            AddNotice("The host wants to start the game but cannot because not all players are ready!");
            sndGetReadySound.Play();
        }

        protected virtual void InsufficientPlayersNotification()
        {
            if (Map != null)
                AddNotice("Unable to launch game: this map cannot be played with fewer than " + Map.MinPlayers + " players.");
        }

        protected virtual void TooManyPlayersNotification()
        {
            if (Map != null)
                AddNotice("Unable to launch game: this map cannot be played with more than " + Map.MaxPlayers + " players.");
        }

        public virtual void Clear()
        {
            if (!IsHost)
                AIPlayers.Clear();

            Players.Clear();
        }

        protected override void OnGameOptionChanged()
        {
            base.OnGameOptionChanged();

            ClearReadyStatuses();
            CopyPlayerDataToUI();
        }

        protected abstract void HostLaunchGame();

        protected override void BtnLeaveGame_LeftClick(object sender, EventArgs e)
        {
            throw new NotImplementedException();
        }

        protected override void CopyPlayerDataFromUI(object sender, EventArgs e)
        {
            if (PlayerUpdatingInProgress)
                return;

            if (IsHost)
            {
                base.CopyPlayerDataFromUI(sender, e);
                BroadcastPlayerOptions();
                return;
            }

            int mTopIndex = Players.FindIndex(p => p.Name == ProgramConstants.PLAYERNAME);

            if (mTopIndex == -1)
                return;

            int requestedSide = ddPlayerSides[mTopIndex].SelectedIndex;
            int requestedColor = ddPlayerColors[mTopIndex].SelectedIndex;
            int requestedStart = ddPlayerStarts[mTopIndex].SelectedIndex;
            int requestedTeam = ddPlayerTeams[mTopIndex].SelectedIndex;

            RequestPlayerOptions(requestedSide, requestedColor, requestedStart, requestedTeam);
        }

        protected override void CopyPlayerDataToUI()
        {
            if (Players.Count + AIPlayers.Count > MAX_PLAYER_COUNT)
                return;

            base.CopyPlayerDataToUI();

            if (IsHost)
            {
                for (int pId = 1; pId < Players.Count; pId++)
                {
                    ddPlayerNames[pId].AllowDropDown = true;
                }
            }

            for (int pId = 0; pId < Players.Count; pId++)
            {
                ReadyBoxes[pId].Checked = Players[pId].Ready;
            }

            for (int aiId = 0; aiId < AIPlayers.Count; aiId++)
            {
                ReadyBoxes[aiId + Players.Count].Checked = true;
            }

            for (int i = AIPlayers.Count + Players.Count; i < MAX_PLAYER_COUNT; i++)
            {
                ReadyBoxes[i].Checked = false;
            }
        }

        protected abstract void BroadcastPlayerOptions();

        protected abstract void RequestPlayerOptions(int side, int color, int start, int team);

        protected abstract void RequestReadyStatus();

        protected void AddNotice(string message)
        {
            AddNotice(message, Color.White);
        }

        protected abstract void AddNotice(string message, Color color);

        protected override bool AllowPlayerOptionsChange()
        {
            return IsHost;
        }

        protected override void ChangeMap(GameMode gameMode, Map map)
        {
            base.ChangeMap(gameMode, map);

            ClearReadyStatuses();

            //if (IsHost)
            //    OnGameOptionChanged();
        }

        protected override void WriteSpawnIniAdditions(IniFile iniFile)
        {
            base.WriteSpawnIniAdditions(iniFile);
            iniFile.SetIntValue("Settings", "FrameSendRate", FrameSendRate);
            if (MaxAhead > 0)
                iniFile.SetIntValue("Settings", "MaxAhead", MaxAhead);
            iniFile.SetIntValue("Settings", "Protocol", ProtocolVersion);
        }

        protected override int GetDefaultMapRankIndex(Map map)
        {
            if (map.MaxPlayers > 3)
                return StatisticsManager.Instance.GetCoopRankForDefaultMap(map.Name, map.MaxPlayers);

            if (StatisticsManager.Instance.HasWonMapInPvP(map.Name, GameMode.UIName, map.MaxPlayers))
                return 2;

            return -1;
        }

        /// <summary>
        /// Attempts to load a custom map.
        /// </summary>
        /// <param name="mapPath">The path to the map file relative to the game directory.</param>
        /// <param name="userInvoked">Whether this function was invoked by the user. 
        /// If true, displays notifications regarding the success of the map loading.</param>
        /// <returns>The map if loading it was succesful, otherwise false.</returns>
        protected Map LoadCustomMap(string mapPath, bool userInvoked)
        {
            Logger.Log("Loading custom map " + mapPath);
            Map map = new Map(mapPath, false);

            if (map.SetInfoFromMap(ProgramConstants.GamePath + mapPath + ".map"))
            {
                foreach (GameMode gm in GameModes)
                {
                    if (gm.Maps.Find(m => m.SHA1 == map.SHA1) != null)
                    {
                        Logger.Log("Custom map " + mapPath + " is already loaded!");

                        if (userInvoked)
                        {
                            AddNotice($"Map {mapPath} is already loaded.");
                        }

                        return null;
                    }
                }

                Logger.Log("Map " + mapPath + " added succesfully.");

                foreach (string gameMode in map.GameModes)
                {
                    GameMode gm = GameModes.Find(g => g.UIName == gameMode);

                    if (gm == null)
                    {
                        if (!allowedGameModes.Contains(gameMode))
                            continue;

                        gm = new GameMode(gameMode);
                        GameModes.Add(gm);
                    }

                    gm.Maps.Add(map);
                }

                if (userInvoked)
                {
                    AddNotice($"Map {mapPath} loaded succesfully.");
                }

                return map;
            }

            if (userInvoked)
            {
                AddNotice($"Loading map {mapPath} failed!");
            }

            Logger.Log("Loading map " + mapPath + " failed!");
            return null;
        }

        public void SwitchOn()
        {
            Enabled = true;
            Visible = true;
        }

        public void SwitchOff()
        {
            Enabled = false;
            Visible = false;
        }

        public abstract string GetSwitchName();

        protected override void UpdateMapPreviewBoxEnabledStatus()
        {
            if (Map != null && GameMode != null)
            {
                bool disablestartlocs = (Map.ForceRandomStartLocations || GameMode.ForceRandomStartLocations);
                MapPreviewBox.EnableContextMenu = disablestartlocs ? false : IsHost;
                MapPreviewBox.EnableStartLocationSelection = !disablestartlocs;
            }
            else
            {
                MapPreviewBox.EnableContextMenu = IsHost;
                MapPreviewBox.EnableStartLocationSelection = true;
            }
        }
    }
}
