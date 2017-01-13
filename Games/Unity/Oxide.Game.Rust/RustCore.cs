﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Network;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Core.ServerConsole;
using Oxide.Game.Rust.Libraries;
using Oxide.Game.Rust.Libraries.Covalence;
using UnityEngine;

namespace Oxide.Game.Rust
{
    /// <summary>
    /// The core Rust plugin
    /// </summary>
    public class RustCore : CSPlugin
    {
        #region Initialization

        // The plugin manager
        private readonly PluginManager pluginManager = Interface.Oxide.RootPluginManager;

        // The permission library
        private readonly Permission permission = Interface.Oxide.GetLibrary<Permission>();
        private static readonly string[] DefaultGroups = { "default", "moderator", "admin" };

        // The command library
        private readonly Command cmdlib = Interface.Oxide.GetLibrary<Command>();

        // The covalence provider
        internal static readonly RustCovalenceProvider Covalence = RustCovalenceProvider.Instance;
        internal static readonly IServer Server = Covalence.CreateServer();

        #region Localization

        // The language library
        private readonly Lang lang = Interface.Oxide.GetLibrary<Lang>();
        private readonly Dictionary<string, string> messages = new Dictionary<string, string>
        {
            {"CommandUsageLoad", "Usage: load *|<pluginname>+"},
            {"CommandUsageGrant", "Usage: grant <group|user> <name|id> <permission>"},
            {"CommandUsageGroup", "Usage: group <add|remove|set> <name> [title] [rank]"},
            {"CommandUsageReload", "Usage: reload *|<pluginname>+"},
            {"CommandUsageRevoke", "Usage: revoke <group|user> <name|id> <permission>"},
            {"CommandUsageShow", "Usage: show <group|user> <name>\nUsage: show <groups|perms>"}, // TODO: Split this up
            {"CommandUsageUnload", "Usage: unload *|<pluginname>+"},
            {"CommandUsageUserGroup", "Usage: usergroup <add|remove> <username> <groupname>"},
            {"GroupAlreadyExists", "Group '{0}' already exists"},
            {"GroupChanged", "Group '{0}' changed"},
            {"GroupCreated", "Group '{0}' created"},
            {"GroupDeleted", "Group '{0}' deleted"},
            {"GroupNotFound", "Group '{0}' doesn't exist"},
            {"GroupParentChanged", "Group '{0}' parent changed to '{1}'"},
            {"GroupParentNotChanged", "Group '{0}' parent was not changed"},
            {"GroupParentNotFound", "Group parent '{0}' doesn't exist"},
            {"GroupPermissionGranted", "Group '{0}' granted permission '{1}'"},
            {"GroupPermissionRevoked", "Group '{0}' revoked permission '{1}'"},
            {"NoPluginsFound", "No plugins are currently available"},
            {"NotAllowed", "You are not allowed to use the '{0}' command"},
            {"PermissionNotFound", "Permission '{0}' doesn't exist"},
            {"PermissionsNotLoaded", "Unable to load permission files! Permissions will not work until resolved.\n => {0}"},
            {"PlayerLanguage", "Player language set to {0}"},
            {"PluginNotLoaded", "Plugin '{0}' not loaded."},
            {"PluginReloaded", "Reloaded plugin {0} v{1} by {2}"},
            {"PluginUnloaded", "Unloaded plugin {0} v{1} by {2}"},
            {"ServerLanguage", "Server language set to {0}"},
            {"UnknownCommand", "Unknown command: {0}"},
            {"UserAddedToGroup", "User '{0}' added to group: {1}"},
            {"UserNotFound", "User '{0}' not found"},
            {"UserPermissionGranted", "User '{0}' granted permission '{1}'"},
            {"UserPermissionRevoked", "User '{0}' revoked permission '{1}'"},
            {"UserRemovedFromGroup", "User '{0}' removed from group '{1}'"},
            {"YouAreNotAdmin", "You are not an admin"}
        };

        #endregion

        // Track when the server has been initialized
        private bool serverInitialized;

        // Cache the serverInput field info
        private readonly FieldInfo serverInputField = typeof(BasePlayer).GetField("serverInput", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly Dictionary<BasePlayer, InputState> playerInputState = new Dictionary<BasePlayer, InputState>();

        // Track if a BasePlayer.OnAttacked call is in progress
        private bool isPlayerTakingDamage;

        // Commands that a plugin can't override
        internal static IEnumerable<string> RestrictedCommands => new[]
        {
            "ownerid", "moderatorid"
        };

        /// <summary>
        /// Initializes a new instance of the RustCore class
        /// </summary>
        public RustCore()
        {
            // Set attributes
            Name = "RustCore";
            Title = "Rust";
            Author = "Oxide Team";
            Version = new VersionNumber(1, 0, 0);

            // Cheat references in the default plugin reference list
            var fpNetwork = Network.Client.disconnectReason; // Facepunch.Network
            var fpSystem = Facepunch.Math.Epoch.Current; // Facepunch.System
            var fpUnity = TimeWarning.Enabled; // Facepunch.UnityEngine
            var rustGlobal = global::Rust.Global.SteamServer; // Rust.Global
        }

        /// <summary>
        /// Checks if the permission system has loaded, shows an error if it failed to load
        /// </summary>
        /// <param name="player"></param>
        /// <returns></returns>
        private bool PermissionsLoaded(IPlayer player)
        {
            if (permission.IsLoaded) return true;
            player.Reply(lang.GetMessage("PermissionsNotLoaded", this, player.Id), permission.LastException.Message);
            return false;
        }

        #endregion

        #region Plugin Hooks

        /// <summary>
        /// Called when the plugin is initialising
        /// </summary>
        [HookMethod("Init")]
        private void Init()
        {
            // Configure remote logging
            RemoteLogger.SetTag("game", Title.ToLower());
            RemoteLogger.SetTag("game version", BuildInformation.VersionStampDays.ToString());

            // Register messages for localization
            lang.RegisterMessages(messages, this);

            // Add core general commands
            AddCovalenceCommand(new[] { "oxide.version", "version" }, "VersionCommand");
            AddCovalenceCommand(new[] { "oxide.lang", "lang" }, "LangCommand");

            // Add core plugin commands
            AddCovalenceCommand(new[] { "oxide.plugins", "plugins" }, "PluginsCommand");
            AddCovalenceCommand(new[] { "oxide.load", "load" }, "LoadCommand");
            AddCovalenceCommand(new[] { "oxide.reload", "reload" }, "ReloadCommand");
            AddCovalenceCommand(new[] { "oxide.unload", "unload" }, "UnloadCommand");

            // Add core permission commands
            AddCovalenceCommand(new[] { "oxide.grant", "grant" }, "GrantCommand");
            AddCovalenceCommand(new[] { "oxide.group", "group" }, "GroupCommand");
            AddCovalenceCommand(new[] { "oxide.revoke", "revoke" }, "RevokeCommand");
            AddCovalenceCommand(new[] { "oxide.show", "show" }, "ShowCommand");
            AddCovalenceCommand(new[] { "oxide.usergroup", "usergroup" }, "UserGroupCommand");

            // Register core permissions
            permission.RegisterPermission("oxide.plugins", this);
            permission.RegisterPermission("oxide.load", this);
            permission.RegisterPermission("oxide.reload", this);
            permission.RegisterPermission("oxide.unload", this);
            permission.RegisterPermission("oxide.grant", this);
            permission.RegisterPermission("oxide.group", this);
            permission.RegisterPermission("oxide.revoke", this);
            permission.RegisterPermission("oxide.show", this);
            permission.RegisterPermission("oxide.usergroup", this);

            // Setup the default permission groups
            if (permission.IsLoaded)
            {
                var rank = 0;
                for (var i = DefaultGroups.Length - 1; i >= 0; i--)
                {
                    var defaultGroup = DefaultGroups[i];
                    if (!permission.GroupExists(defaultGroup)) permission.CreateGroup(defaultGroup, defaultGroup, rank++);
                }
                permission.RegisterValidate(s =>
                {
                    ulong temp;
                    if (!ulong.TryParse(s, out temp)) return false;
                    var digits = temp == 0 ? 1 : (int)Math.Floor(Math.Log10(temp) + 1);
                    return digits >= 17;
                });
                permission.CleanUp();
            }
        }

        /// <summary>
        /// Called when another plugin has been loaded
        /// </summary>
        /// <param name="plugin"></param>
        [HookMethod("OnPluginLoaded")]
        private void OnPluginLoaded(Plugin plugin)
        {
            if (serverInitialized) plugin.CallHook("OnServerInitialized");
        }

        #endregion

        #region Server Hooks

        /// <summary>
        /// Called when the server is first initialized
        /// </summary>
        [HookMethod("OnServerInitialized")]
        private void OnServerInitialized()
        {
            if (serverInitialized) return;
            serverInitialized = true;

            Analytics.Collect();

            // Destroy default server console
            if (Interface.Oxide.CheckConsole() && ServerConsole.Instance != null)
            {
                ServerConsole.Instance.enabled = false;
                UnityEngine.Object.Destroy(ServerConsole.Instance);
                typeof(SingletonComponent<ServerConsole>).GetField("instance", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, null);
            }

            // Update server console window and status bars
            RustExtension.ServerConsole();
        }

        /// <summary>
        /// Called when the server is saving
        /// </summary>
        [HookMethod("OnServerSave")]
        private void OnServerSave() => Analytics.Collect();

        /// <summary>
        /// Called when the server is shutting down
        /// </summary>
        [HookMethod("IOnServerShutdown")]
        private void IOnServerShutdown()
        {
            Interface.Call("OnServerShutdown");
            Interface.Oxide.OnShutdown();
        }

        /// <summary>
        /// Called when ServerConsole is enabled
        /// </summary>
        [HookMethod("IOnEnableServerConsole")]
        private object IOnEnableServerConsole(ServerConsole serverConsole)
        {
            if (ConsoleWindow.Check(true) && !Interface.Oxide.CheckConsole(true)) return null;
            serverConsole.enabled = false;
            UnityEngine.Object.Destroy(serverConsole);
            typeof(SingletonComponent<ServerConsole>).GetField("instance", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, null);
            return false;
        }

        /// <summary>
        /// Called when ServerConsole is disabled
        /// </summary>
        [HookMethod("IOnDisableServerConsole")]
        private object IOnDisableServerConsole() => ConsoleWindow.Check(true) && !Interface.Oxide.CheckConsole(true) ? (object)null : false;

        #endregion

        #region Player Hooks

        /// <summary>
        /// Called when a user is attempting to connect
        /// </summary>
        /// <param name="connection"></param>
        /// <returns></returns>
        [HookMethod("IOnUserApprove")]
        private object IOnUserApprove(Connection connection)
        {
            var id = connection.userid.ToString();
            var ip = Regex.Replace(connection.ipaddress, @":{1}[0-9]{1}\d*", ""); // TODO: Move IP regex to utility method and make static

            // Call out and see if we should reject
            var loginSpecific = Interface.Call("CanClientLogin", connection);
            var loginCovalence = Interface.Call("CanUserLogin", connection.username, id, ip);
            var canLogin = loginSpecific ?? loginCovalence;

            // Check if player can login
            if (canLogin is string || (canLogin is bool && !(bool)canLogin))
            {
                // Reject the user with the message
                ConnectionAuth.Reject(connection, canLogin is string ? canLogin.ToString() : "Connection was rejected"); // TODO: Localization
                return true;
            }

            // Call the approval hooks
            var approvedSpecific = Interface.Call("OnUserApprove", connection);
            var approvedCovalence = Interface.Call("OnUserApproved", connection.username, id, ip);
            return approvedSpecific ?? approvedCovalence;
        }

        /// <summary>
        /// Called when the player has been initialized
        /// </summary>
        /// <param name="arg"></param>
        /// <returns></returns>
        [HookMethod("OnPlayerChat")]
        private object OnPlayerChat(ConsoleSystem.Arg arg)
        {
            // Call covalence hook
            var iplayer = Covalence.PlayerManager.FindPlayer(arg.connection.userid.ToString());
            return string.IsNullOrEmpty(arg.GetString(0)) ? null : Interface.Call("OnUserChat", iplayer, arg.GetString(0));
        }

        /// <summary>
        /// Called when the player has connected
        /// </summary>
        /// <param name="player"></param>
        [HookMethod("OnPlayerInit")]
        private void OnPlayerInit(BasePlayer player)
        {
            // Do permission stuff
            var authLevel = player.net.connection.authLevel;
            if (permission.IsLoaded && authLevel <= DefaultGroups.Length)
            {
                var id = player.UserIDString;

                // Update stored name
                permission.UpdateNickname(id, player.displayName);

                // Add player to default group
                if (!permission.UserHasGroup(id, DefaultGroups[0])) permission.AddUserGroup(id, DefaultGroups[0]);

                // Add player to group based on auth level
                if (authLevel >= 1 && !permission.UserHasGroup(id, DefaultGroups[authLevel])) permission.AddUserGroup(id, DefaultGroups[authLevel]);
            }

            // Set language for player
            lang.SetLanguage(player.net.connection.info.GetString("global.language", "en"), player.UserIDString);

            // Cache serverInput for player so that reflection only needs to be used once
            playerInputState[player] = (InputState)serverInputField.GetValue(player);

            // Let covalence know
            Covalence.PlayerManager.NotifyPlayerConnect(player);
            var iplayer = Covalence.PlayerManager.FindPlayer(player.UserIDString);
            if (iplayer != null) Interface.Call("OnUserConnected", iplayer);
        }

        /// <summary>
        /// Called when the player has disconnected
        /// </summary>
        /// <param name="player"></param>
        /// <param name="reason"></param>
        [HookMethod("OnPlayerDisconnected")]
        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            // Let covalence know
            var iplayer = Covalence.PlayerManager.FindPlayer(player.UserIDString);
            if (iplayer != null) Interface.Call("OnUserDisconnected", iplayer, reason);
            Covalence.PlayerManager.NotifyPlayerDisconnect(player);

            playerInputState.Remove(player);
        }

        /// <summary>
        /// Called when the player is respawning
        /// </summary>
        /// <param name="player"></param>
        /// <returns></returns>
        [HookMethod("OnPlayerRespawn")]
        private object OnPlayerRespawn(BasePlayer player)
        {
            // Call covalence hook
            var iplayer = Covalence.PlayerManager.FindPlayer(player.UserIDString);
            return iplayer != null ? Interface.Call("OnUserRespawn", iplayer) : null;
        }

        /// <summary>
        /// Called when the player has respawned
        /// </summary>
        /// <param name="player"></param>
        [HookMethod("OnPlayerRespawned")]
        private void OnPlayerRespawned(BasePlayer player)
        {
            // Call covalence hook
            var iplayer = Covalence.PlayerManager.FindPlayer(player.UserIDString);
            if (iplayer != null) Interface.Call("OnUserRespawned", iplayer);
        }

        /// <summary>
        /// Called when a player tick is received from a client
        /// </summary>
        /// <param name="player"></param>
        /// <returns></returns>
        [HookMethod("OnPlayerTick")]
        private object OnPlayerTick(BasePlayer player)
        {
            InputState input;
            return playerInputState.TryGetValue(player, out input) ? Interface.Call("OnPlayerInput", player, input) : null;
        }

        /// <summary>
        /// Called when a BasePlayer is attacked
        /// This is used to call OnEntityTakeDamage for a BasePlayer when attacked
        /// </summary>
        /// <param name="player"></param>
        /// <param name="info"></param>
        [HookMethod("IOnBasePlayerAttacked")]
        private object IOnBasePlayerAttacked(BasePlayer player, HitInfo info)
        {
            if (!serverInitialized || player == null || info == null || player.IsDead() || isPlayerTakingDamage) return null;
            if (Interface.Call("OnEntityTakeDamage", player, info) != null) return true;

            isPlayerTakingDamage = true;
            try
            {
                player.OnAttacked(info);
            }
            finally
            {
                isPlayerTakingDamage = false;
            }
            return true;
        }

        /// <summary>
        /// Called when a BasePlayer is hurt
        /// This is used to call OnEntityTakeDamage when a player was hurt without being attacked
        /// </summary>
        /// <param name="player"></param>
        /// <param name="info"></param>
        /// <returns></returns>
        [HookMethod("IOnBasePlayerHurt")]
        private object IOnBasePlayerHurt(BasePlayer player, HitInfo info)
        {
            return isPlayerTakingDamage ? null : Interface.Call("OnEntityTakeDamage", player, info);
        }

        /// <summary>
        /// Called when the player starts looting an entity
        /// </summary>
        /// <param name="source"></param>
        /// <param name="entity"></param>
        [HookMethod("IOnLootEntity")]
        private void IOnLootEntity(PlayerLoot source, BaseEntity entity) => Interface.Call("OnLootEntity", source.GetComponent<BasePlayer>(), entity);

        /// <summary>
        /// Called when the player starts looting an item
        /// </summary>
        /// <param name="source"></param>
        /// <param name="item"></param>
        [HookMethod("IOnLootItem")]
        private void IOnLootItem(PlayerLoot source, Item item) => Interface.Call("OnLootItem", source.GetComponent<BasePlayer>(), item);

        /// <summary>
        /// Called when the player starts looting another player
        /// </summary>
        /// <param name="source"></param>
        /// <param name="target"></param>
        [HookMethod("IOnLootPlayer")]
        private void IOnLootPlayer(PlayerLoot source, BasePlayer target) => Interface.Call("OnLootPlayer", source.GetComponent<BasePlayer>(), target);

        /// <summary>
        /// Called when a player attacks something
        /// </summary>
        /// <param name="melee"></param>
        /// <param name="info"></param>
        /// <returns></returns>
        [HookMethod("IOnPlayerAttack")]
        private object IOnPlayerAttack(BaseMelee melee, HitInfo info) => Interface.Call("OnPlayerAttack", melee.GetOwnerPlayer(), info);

        /// <summary>
        /// Called when a player revives another player
        /// </summary>
        /// <param name="tool"></param>
        /// <param name="target"></param>
        /// <returns></returns>
        [HookMethod("IOnPlayerRevive")]
        private object IOnPlayerRevive(MedicalTool tool, BasePlayer target) => Interface.Call("OnPlayerRevive", tool.GetOwnerPlayer(), target);

        #endregion

        #region Entity Hooks

        /// <summary>
        /// Called when a BaseCombatEntity takes damage
        /// This is used to call OnEntityTakeDamage for anything other than a BasePlayer
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="info"></param>
        /// <returns></returns>
        [HookMethod("IOnBaseCombatEntityHurt")]
        private object IOnBaseCombatEntityHurt(BaseCombatEntity entity, HitInfo info)
        {
            return entity is BasePlayer ? null : Interface.Call("OnEntityTakeDamage", entity, info);
        }

        #endregion

        #region Structure Hooks

        /// <summary>
        /// Called when the player attempts to use a update a sign
        /// </summary>
        /// <param name="player"></param>
        /// <param name="sign"></param>
        [HookMethod("CanUpdateSign")]
        private object CanUpdateSign(BasePlayer player, Signage sign)
        {
            // Call deprecated hooks
            var canLockSign = Interface.CallDeprecatedHook("CanLockSign", "CanUpdateSign", new DateTime(2017, 1, 3), player, sign);
            if (!sign.IsLocked() && canLockSign != null) return (bool)canLockSign;
            var canUnlockSign = Interface.CallDeprecatedHook("CanUnlockSign", "CanUpdateSign", new DateTime(2017, 1, 3), player, sign);
            if (sign.IsLocked() && canUnlockSign != null) return (bool)canUnlockSign;

            return null;
        }

        /// <summary>
        /// Called when the player attempts to use a lock
        /// </summary>
        /// <param name="player"></param>
        /// <param name="lock"></param>
        [HookMethod("CanUseLock")]
        private object CanUseLock(BasePlayer player, BaseLock @lock)
        {
            // Call deprecated hook
            var canUseDoor = Interface.CallDeprecatedHook("CanUseDoor", "CanUseLock", new DateTime(2017, 1, 3), player, @lock);
            return (bool?)canUseDoor;
        }

        /// <summary>
        /// Called when a player selects Demolish from the BuildingBlock menu
        /// </summary>
        /// <param name="block"></param>
        /// <param name="player"></param>
        /// <returns></returns>
        [HookMethod("IOnStructureDemolish")]
        private object IOnStructureDemolish(BuildingBlock block, BasePlayer player) => Interface.Call("OnStructureDemolish", block, player, false);

        /// <summary>
        /// Called when a player selects Demolish Immediate from the BuildingBlock menu
        /// </summary>
        /// <param name="block"></param>
        /// <param name="player"></param>
        /// <returns></returns>
        [HookMethod("IOnStructureImmediateDemolish")]
        private object IOnStructureImmediateDemolish(BuildingBlock block, BasePlayer player) => Interface.Call("OnStructureDemolish", block, player, true);

        #endregion

        #region Item Hooks

        /// <summary>
        /// Called when an item loses durability
        /// </summary>
        /// <param name="item"></param>
        /// <param name="amount"></param>
        /// <returns></returns>
        [HookMethod("IOnLoseCondition")]
        private object IOnLoseCondition(Item item, float amount)
        {
            var arguments = new object[] { item, amount };
            Interface.Call("OnLoseCondition", arguments);
            amount = (float)arguments[1];
            var condition = item.condition;
            item.condition -= amount;
            if ((item.condition <= 0f) && (item.condition < condition)) item.OnBroken();
            return true;
        }

        #endregion

        #region Covalence Commands

        #region Plugins Command

        /// <summary>
        /// Called when the "plugins" command has been executed
        /// </summary>
        /// <param name="player"></param>
        /// <param name="command"></param>
        /// <param name="args"></param>
        [HookMethod("PluginsCommand")]
        private void PluginsCommand(IPlayer player, string command, string[] args)
        {
            if (!player.IsAdmin && !player.HasPermission("oxide.plugins"))
            {
                player.Reply(lang.GetMessage("NotAllowed", this, player.Id), command);
                return;
            }

            var loadedPlugins = pluginManager.GetPlugins().Where(pl => !pl.IsCorePlugin).ToArray();
            var loadedPluginNames = new HashSet<string>(loadedPlugins.Select(pl => pl.Name));
            var unloadedPluginErrors = new Dictionary<string, string>();
            foreach (var loader in Interface.Oxide.GetPluginLoaders())
            {
                foreach (var name in loader.ScanDirectory(Interface.Oxide.PluginDirectory).Except(loadedPluginNames))
                {
                    string msg;
                    unloadedPluginErrors[name] = (loader.PluginErrors.TryGetValue(name, out msg)) ? msg : "Unloaded";
                }
            }

            var totalPluginCount = loadedPlugins.Length + unloadedPluginErrors.Count;
            if (totalPluginCount < 1)
            {
                player.Reply(lang.GetMessage("NoPluginsFound", this, player.Id));
                return;
            }

            var output = $"Listing {loadedPlugins.Length + unloadedPluginErrors.Count} plugins:";
            var number = 1;
            foreach (var plugin in loadedPlugins)
                output += $"\n  {number++:00} \"{plugin.Title}\" ({plugin.Version}) by {plugin.Author} ({plugin.TotalHookTime:0.00}s)";
            foreach (var pluginName in unloadedPluginErrors.Keys)
                output += $"\n  {number++:00} {pluginName} - {unloadedPluginErrors[pluginName]}";
            player.Reply(output);
        }

        #endregion

        #region Load Command

        /// <summary>
        /// Called when the "load" command has been executed
        /// </summary>
        /// <param name="player"></param>
        /// <param name="command"></param>
        /// <param name="args"></param>
        [HookMethod("LoadCommand")]
        private void LoadCommand(IPlayer player, string command, string[] args)
        {
            if (!player.IsAdmin && !player.HasPermission("oxide.load"))
            {
                player.Reply(lang.GetMessage("NotAllowed", this, player.Id), command);
                return;
            }

            if (args.Length < 1)
            {
                player.Reply(lang.GetMessage("CommandUsageLoad", this, player.Id));
                return;
            }

            if (args[0].Equals("*") || args[0].Equals("all"))
            {
                Interface.Oxide.LoadAllPlugins();
                return;
            }

            foreach (var name in args)
            {
                if (string.IsNullOrEmpty(name)) continue;
                Interface.Oxide.LoadPlugin(name);
                pluginManager.GetPlugin(name);
            }
        }

        #endregion

        #region Reload Command

        /// <summary>
        /// Called when the "reload" command has been executed
        /// </summary>
        /// <param name="player"></param>
        /// <param name="command"></param>
        /// <param name="args"></param>
        [HookMethod("ReloadCommand")]
        private void ReloadCommand(IPlayer player, string command, string[] args)
        {
            if (!player.IsAdmin && !player.HasPermission("oxide.reload"))
            {
                player.Reply(lang.GetMessage("NotAllowed", this, player.Id), command);
                return;
            }

            if (args.Length < 1)
            {
                player.Reply(lang.GetMessage("CommandUsageReload", this, player.Id));
                return;
            }

            if (args[0].Equals("*") || args[0].Equals("all"))
            {
                Interface.Oxide.ReloadAllPlugins();
                return;
            }

            foreach (var name in args)
                if (!string.IsNullOrEmpty(name)) Interface.Oxide.ReloadPlugin(name);
        }

        #endregion

        #region Unload Command

        /// <summary>
        /// Called when the "unload" command has been executed
        /// </summary>
        /// <param name="player"></param>
        /// <param name="command"></param>
        /// <param name="args"></param>
        [HookMethod("UnloadCommand")]
        private void UnloadCommand(IPlayer player, string command, string[] args)
        {
            if (!player.IsAdmin && !player.HasPermission("oxide.unload"))
            {
                player.Reply(lang.GetMessage("NotAllowed", this, player.Id));
                return;
            }

            if (args.Length < 1)
            {
                player.Reply(lang.GetMessage("CommandUsageUnload", this, player.Id));
                return;
            }

            if (args[0].Equals("*") || args[0].Equals("all"))
            {
                Interface.Oxide.UnloadAllPlugins();
                return;
            }

            foreach (var name in args)
                if (!string.IsNullOrEmpty(name)) Interface.Oxide.UnloadPlugin(name);
        }

        #endregion

        #region Version Command

        /// <summary>
        /// Called when the "version" command has been executed
        /// </summary>
        /// <param name="player"></param>
        /// <param name="command"></param>
        /// <param name="args"></param>
        [HookMethod("VersionCommand")]
        private void VersionCommand(IPlayer player, string command, string[] args)
        {
            if (player.Id != "server_console")
            {
                var format = Covalence.FormatText("[+15]Server is running [b][#ffb658]Oxide {0}[/#][/b] and [b][#ee715c]{1} {2}[/#][/b][/+]");
                player.Reply(format, OxideMod.Version, Covalence.GameName, Server.Version);
            }
            else
            {
                player.Reply($"Protocol: {Server.Protocol}\nBuild Version: {BuildInformation.VersionStampDays}\n" +
                $"Build Date: {BuildInformation.VersionStampString}\nUnity Version: {UnityEngine.Application.unityVersion}\n" +
                $"Changeset: {BuildInformation.ChangeSet}\nBranch: {BuildInformation.BranchName}\nOxide Version: {OxideMod.Version}");
            }
        }

        #endregion

        #region Lang Command

        /// <summary>
        /// Called when the "lang" command has been executed
        /// </summary>
        /// <param name="player"></param>
        /// <param name="command"></param>
        /// <param name="args"></param>
        [HookMethod("LangCommand")]
        private void LangCommand(IPlayer player, string command, string[] args)
        {
            if (args.Length < 1)
            {
                player.Reply(lang.GetMessage("CommandUsageGroup", this, player.Id));
                return;
            }

            if (player.Id != "server_console")
            {
                lang.SetLanguage(args[0], player.Id);
                player.Reply(lang.GetMessage("PlayerLanguage", this, player.Id), args[0]);
            }
            else
            {
                lang.SetServerLanguage(args[0]);
                player.Reply(lang.GetMessage("ServerLanguage", this, player.Id), lang.GetServerLanguage());
            }
        }

        #endregion

        #region Group Command

        /// <summary>
        /// Called when the "group" command has been executed
        /// </summary>
        /// <param name="player"></param>
        /// <param name="command"></param>
        /// <param name="args"></param>
        [HookMethod("GroupCommand")]
        private void GroupCommand(IPlayer player, string command, string[] args)
        {
            if (!PermissionsLoaded(player)) return;
            if (!player.IsAdmin && !player.HasPermission("oxide.group"))
            {
                player.Reply(lang.GetMessage("NotAllowed", this, player.Id), command);
                return;
            }

            if (args.Length != 2)
            {
                player.Reply(lang.GetMessage("CommandUsageGroup", this, player.Id));
                return;
            }

            var mode = args[0];
            var group = args[1];
            var title = args.Length >= 3 ? args[2] : "";
            var rank = args.Length == 4 ? int.Parse(args[3]) : 0;

            if (mode.Equals("add"))
            {
                if (permission.GroupExists(group))
                {
                    player.Reply(lang.GetMessage("GroupAlreadyExists", this, player.Id), group);
                    return;
                }
                permission.CreateGroup(group, title, rank);
                player.Reply(lang.GetMessage("GroupCreated", this, player.Id), group);
            }
            else if (mode.Equals("remove"))
            {
                if (!permission.GroupExists(group))
                {
                    player.Reply(lang.GetMessage("GroupNotFound", this, player.Id), group);
                    return;
                }
                permission.RemoveGroup(group);
                player.Reply(lang.GetMessage("GroupDeleted", this, player.Id), group);
            }
            else if (mode.Equals("set"))
            {
                if (!permission.GroupExists(group))
                {
                    player.Reply(lang.GetMessage("GroupNotFound", this, player.Id), group);
                    return;
                }
                permission.SetGroupTitle(group, args[2]);
                permission.SetGroupRank(group, int.Parse(args[3]));
                player.Reply(lang.GetMessage("GroupChanged", this, player.Id), group);
            }
            else if (mode.Equals("parent"))
            {
                if (!permission.GroupExists(group))
                {
                    player.Reply(lang.GetMessage("GroupNotFound", this, player.Id), group);
                    return;
                }
                var parent = args[2];
                if (!string.IsNullOrEmpty(parent) && !permission.GroupExists(parent))
                {
                    player.Reply(lang.GetMessage("GroupParentNotFound", this, player.Id), parent);
                    return;
                }
                if (permission.SetGroupParent(group, parent))
                    player.Reply(lang.GetMessage("GroupParentChanged", this, player.Id), group, parent);
                else
                    player.Reply(lang.GetMessage("GroupParentNotChanged", this, player.Id), group);
            }
            else player.Reply(lang.GetMessage("CommandUsageGroup", this, player.Id));
        }

        #endregion

        #region User Group Command

        /// <summary>
        /// Called when the "usergroup" command has been executed
        /// </summary>
        /// <param name="player"></param>
        /// <param name="command"></param>
        /// <param name="args"></param>
        [HookMethod("UserGroupCommand")]
        private void UserGroupCommand(IPlayer player, string command, string[] args)
        {
            if (!PermissionsLoaded(player)) return;
            if (!player.IsAdmin && !player.HasPermission("oxide.usergroup"))
            {
                player.Reply(lang.GetMessage("NotAllowed", this, player.Id), command);
                return;
            }

            if (args.Length != 3)
            {
                player.Reply(lang.GetMessage("CommandUsageUserGroup", this, player.Id));
                return;
            }

            var mode = args[0];
            var name = args[1];
            var group = args[2];

            var target = Covalence.PlayerManager.FindPlayer(name);
            if (target == null && !permission.UserIdValid(name))
            {
                player.Reply(lang.GetMessage("UserNotFound", this, player.Id), name);
                return;
            }
            var userId = name;
            if (target != null)
            {
                userId = target.Id;
                name = target.Name;
                permission.UpdateNickname(userId, name);
                name += $"({userId})";
            }

            if (!permission.GroupExists(group))
            {
                player.Reply(lang.GetMessage("GroupNotFound", this, player.Id), group);
                return;
            }

            if (mode.Equals("add"))
            {
                permission.AddUserGroup(userId, group);
                player.Reply(lang.GetMessage("UserAddedToGroup", this, player.Id), name, group);
            }
            else if (mode.Equals("remove"))
            {
                permission.RemoveUserGroup(userId, group);
                player.Reply(lang.GetMessage("UserRemovedFromGroup", this, player.Id), name, group);
            }
            else player.Reply(lang.GetMessage("CommandUsageUserGroup", this, player.Id));
        }

        #endregion

        #region Grant Command

        /// <summary>
        /// Called when the "grant" command has been executed
        /// </summary>
        /// <param name="player"></param>
        /// <param name="command"></param>
        /// <param name="args"></param>
        [HookMethod("GrantCommand")]
        private void GrantCommand(IPlayer player, string command, string[] args)
        {
            if (!PermissionsLoaded(player)) return;
            if (!player.IsAdmin && !player.HasPermission("oxide.grant"))
            {
                player.Reply(lang.GetMessage("NotAllowed", this, player.Id), command);
                return;
            }

            if (args.Length != 3)
            {
                player.Reply(lang.GetMessage("CommandUsageGrant", this, player.Id));
                return;
            }

            var mode = args[0];
            var name = args[1];
            var perm = args[2];

            if (!permission.PermissionExists(perm))
            {
                player.Reply(lang.GetMessage("PermissionNotFound", this, player.Id), perm);
                return;
            }

            if (mode.Equals("group"))
            {
                if (!permission.GroupExists(name))
                {
                    player.Reply(lang.GetMessage("GroupNotFound", this, player.Id), name);
                    return;
                }
                permission.GrantGroupPermission(name, perm, null);
                player.Reply(lang.GetMessage("GroupPermissionGranted", this, player.Id), name, perm);
            }
            else if (mode.Equals("user"))
            {
                var target = Covalence.PlayerManager.FindPlayer(name);
                if (target == null && !permission.UserIdValid(name))
                {
                    player.Reply(lang.GetMessage("UserNotFound", this, player.Id), name);
                    return;
                }
                var userId = name;
                if (target != null)
                {
                    userId = target.Id;
                    name = target.Name;
                    permission.UpdateNickname(userId, name);
                }
                permission.GrantUserPermission(userId, perm, null);
                player.Reply(lang.GetMessage("UserPermissionGranted", this, player.Id), $"{name} ({userId})", perm);
            }
            else player.Reply(lang.GetMessage("CommandUsageGrant", this, player.Id));
        }

        #endregion

        #region Revoke Command

        /// <summary>
        /// Called when the "revoke" command has been executed
        /// </summary>
        /// <param name="player"></param>
        /// <param name="command"></param>
        /// <param name="args"></param>
        [HookMethod("RevokeCommand")]
        private void RevokeCommand(IPlayer player, string command, string[] args)
        {
            if (!PermissionsLoaded(player)) return;
            if (!player.IsAdmin && !player.HasPermission("oxide.revoke"))
            {
                player.Reply(lang.GetMessage("NotAllowed", this, player.Id), command);
                return;
            }

            if (args.Length != 3)
            {
                player.Reply(lang.GetMessage("CommandUsageRevoke", this, player.Id));
                return;
            }

            var mode = args[0];
            var name = args[1];
            var perm = args[2];

            if (!permission.PermissionExists(perm))
            {
                player.Reply(lang.GetMessage("PermissionNotFound", this, player.Id), perm);
                return;
            }

            if (mode.Equals("group"))
            {
                if (!permission.GroupExists(name))
                {
                    player.Reply(lang.GetMessage("GroupNotFound", this, player.Id), name);
                    return;
                }
                permission.RevokeGroupPermission(name, perm);
                player.Reply(lang.GetMessage("GroupPermissionRevoked", this, player.Id), name, perm);
            }
            else if (mode.Equals("user"))
            {
                var target = Covalence.PlayerManager.FindPlayer(name);
                if (target == null && !permission.UserIdValid(name))
                {
                    player.Reply(lang.GetMessage("UserNotFound", this, player.Id), name);
                    return;
                }
                var userId = name;
                if (target != null)
                {
                    userId = target.Id;
                    name = target.Name;
                    permission.UpdateNickname(userId, name);
                }
                permission.RevokeUserPermission(userId, perm);
                player.Reply(lang.GetMessage("UserPermissionRevoked", this, player.Id), $"{name} ({userId})", perm);
            }
            else player.Reply(lang.GetMessage("CommandUsageRevoke", this, player.Id));
        }

        #endregion

        #region Show Command

        /// <summary>
        /// Called when the "show" command has been executed
        /// </summary>
        /// <param name="player"></param>
        /// <param name="command"></param>
        /// <param name="args"></param>
        [HookMethod("ShowCommand")]
        private void ShowCommand(IPlayer player, string command, string[] args)
        {
            if (!PermissionsLoaded(player)) return;
            if (!player.IsAdmin && !player.HasPermission("oxide.show"))
            {
                player.Reply(lang.GetMessage("NotAllowed", this, player.Id), command);
                return;
            }

            if (args.Length == 0)
            {
                player.Reply(lang.GetMessage("CommandUsageShow", this, player.Id));
                return;
            }

            var mode = args[0];
            var name = args.Length == 2 ? args[1] : "";

            if (mode.Equals("perms"))
            {
                player.Reply("Permissions:\n" + string.Join(", ", permission.GetPermissions())); // TODO: Localization
            }
            else if (mode.Equals("perm"))
            {
                if (string.IsNullOrEmpty(name))
                {
                    player.Reply(lang.GetMessage("CommandUsageShow", this, player.Id));
                    return;
                }

                var result = $"Permission '{name}' Users:\n";
                result += string.Join(", ", permission.GetPermissionUsers(name));
                result += $"\nPermission '{name}' Groups:\n";
                result += string.Join(", ", permission.GetPermissionGroups(name));
                player.Reply(result);
            }
            else if (mode.Equals("user"))
            {
                if (string.IsNullOrEmpty(name))
                {
                    player.Reply(lang.GetMessage("CommandUsageShow", this, player.Id));
                    return;
                }

                var target = Covalence.PlayerManager.FindPlayer(name);
                if (target == null && !permission.UserIdValid(name))
                {
                    player.Reply(lang.GetMessage("UserNotFound", this, player.Id), name);
                    return;
                }
                var userId = name;
                if (target != null)
                {
                    userId = target.Id;
                    name = target.Name;
                    permission.UpdateNickname(userId, name);
                    name += $" ({userId})";
                }
                var result = $"User '{name}' permissions:\n";
                result += string.Join(", ", permission.GetUserPermissions(userId));
                result += $"\nUser '{name}' groups:\n";
                result += string.Join(", ", permission.GetUserGroups(userId));
                player.Reply(result);
            }
            else if (mode.Equals("group"))
            {
                if (string.IsNullOrEmpty(name))
                {
                    player.Reply(lang.GetMessage("CommandUsageShow", this, player.Id));
                    return;
                }

                if (!permission.GroupExists(name))
                {
                    player.Reply(lang.GetMessage("GroupNotFound", this, player.Id), name);
                    return;
                }

                var result = $"Group '{name}' users:\n";
                result += string.Join(", ", permission.GetUsersInGroup(name));
                result += $"\nGroup '{name}' permissions:\n";
                result += string.Join(", ", permission.GetGroupPermissions(name));
                var parent = permission.GetGroupParent(name);
                while (permission.GroupExists(parent))
                {
                    result += $"\nParent group '{parent}' permissions:\n";
                    result += string.Join(", ", permission.GetGroupPermissions(parent));
                    parent = permission.GetGroupParent(parent);
                }
                player.Reply(result);
            }
            else if (mode.Equals("groups"))
            {
                player.Reply("Groups:\n" + string.Join(", ", permission.GetGroups())); // TODO: Localization
            }
            else player.Reply(lang.GetMessage("CommandUsageShow", this, player.Id));
        }

        #endregion

        #endregion

        #region Command Handling

        /// <summary>
        /// Called when a command was run
        /// </summary>
        /// <param name="arg"></param>
        /// <returns></returns>
        [HookMethod("IOnServerCommand")]
        private object IOnServerCommand(ConsoleSystem.Arg arg)
        {
            if (arg?.cmd == null) return null;

            // Is it a server command?
            var serverCommand = Interface.Call("OnServerCommand", arg);
            if (serverCommand != null) return true;

            // Check if from a player
            var player = arg.connection?.player as BasePlayer;
            if (player == null) return null;

            // Get the full command
            var command = arg.cmd.namefull == "chat.say" ? arg.GetString(0) : arg.cmd.namefull;
            if (string.IsNullOrEmpty(command)) return null;

            // Parse it
            string cmd;
            string[] args;
            ParseChatCommand(command, out cmd, out args);
            if (cmd == null) return null;

            // Get the covalence player
            var iplayer = Covalence.PlayerManager.FindPlayer(arg.connection.userid.ToString());
            if (iplayer == null) return null;

            // Is the command blocked?
            var blockedSpecific = Interface.Call("OnPlayerCommand", arg);
            var blockedCovalence = Interface.Call("OnUserCommand", iplayer, cmd, args);
            if (blockedSpecific != null || blockedCovalence != null) return true;

            // Is it a covalance command?
            if (Covalence.CommandSystem.HandleChatMessage(iplayer, command)) return true;

            // Is it a regular chat command?
            if (arg.cmd.namefull == "chat.say" && !cmdlib.HandleChatCommand(player, cmd, args)) iplayer.Reply(lang.GetMessage("UnknownCommand", this, iplayer.Id), cmd);

            // Handled
            arg.ReplyWith(string.Empty);
            return null;
        }

        /// <summary>
        /// Parses the specified chat command
        /// </summary>
        /// <param name="argstr"></param>
        /// <param name="cmd"></param>
        /// <param name="args"></param>
        private void ParseChatCommand(string argstr, out string cmd, out string[] args)
        {
            var arglist = new List<string>();
            var sb = new StringBuilder();
            var inlongarg = false;

            foreach (var c in argstr)
            {
                if (c == '"')
                {
                    if (inlongarg)
                    {
                        var arg = sb.ToString().Trim();
                        if (!string.IsNullOrEmpty(arg)) arglist.Add(arg);
                        sb.Clear();
                        inlongarg = false;
                    }
                    else
                    {
                        inlongarg = true;
                    }
                }
                else if (char.IsWhiteSpace(c) && !inlongarg)
                {
                    var arg = sb.ToString().Trim();
                    if (!string.IsNullOrEmpty(arg)) arglist.Add(arg);
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }
            if (sb.Length > 0)
            {
                var arg = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(arg)) arglist.Add(arg);
            }
            if (arglist.Count == 0)
            {
                cmd = null;
                args = null;
                return;
            }
            cmd = arglist[0];
            arglist.RemoveAt(0);
            args = arglist.ToArray();
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Returns the BasePlayer for the specified name, ID, or IP address string
        /// </summary>
        /// <param name="nameOrIdOrIp"></param>
        /// <returns></returns>
        public static BasePlayer FindPlayer(string nameOrIdOrIp)
        {
            BasePlayer player = null;
            foreach (var activePlayer in BasePlayer.activePlayerList)
            {
                if (string.IsNullOrEmpty(activePlayer.UserIDString)) continue;
                if (activePlayer.UserIDString.Equals(nameOrIdOrIp))
                    return activePlayer;
                if (string.IsNullOrEmpty(activePlayer.displayName)) continue;
                if (activePlayer.displayName.Equals(nameOrIdOrIp, StringComparison.OrdinalIgnoreCase))
                    return activePlayer;
                if (activePlayer.displayName.Contains(nameOrIdOrIp, CompareOptions.OrdinalIgnoreCase))
                    player = activePlayer;
                if (activePlayer.net?.connection != null && activePlayer.net.connection.ipaddress.Equals(nameOrIdOrIp))
                    return activePlayer;
            }
            foreach (var sleepingPlayer in BasePlayer.sleepingPlayerList)
            {
                if (string.IsNullOrEmpty(sleepingPlayer.UserIDString)) continue;
                if (sleepingPlayer.UserIDString.Equals(nameOrIdOrIp))
                    return sleepingPlayer;
                if (string.IsNullOrEmpty(sleepingPlayer.displayName)) continue;
                if (sleepingPlayer.displayName.Equals(nameOrIdOrIp, StringComparison.OrdinalIgnoreCase))
                    return sleepingPlayer;
                if (sleepingPlayer.displayName.Contains(nameOrIdOrIp, CompareOptions.OrdinalIgnoreCase))
                    player = sleepingPlayer;
            }
            return player;
        }

        /// <summary>
        /// Returns the BasePlayer for the specified name string
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public static BasePlayer FindPlayerByName(string name)
        {
            BasePlayer player = null;
            foreach (var activePlayer in BasePlayer.activePlayerList)
            {
                if (string.IsNullOrEmpty(activePlayer.displayName)) continue;
                if (activePlayer.displayName.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return activePlayer;
                if (activePlayer.displayName.Contains(name, CompareOptions.OrdinalIgnoreCase))
                    player = activePlayer;
            }
            foreach (var sleepingPlayer in BasePlayer.sleepingPlayerList)
            {
                if (string.IsNullOrEmpty(sleepingPlayer.displayName)) continue;
                if (sleepingPlayer.displayName.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return sleepingPlayer;
                if (sleepingPlayer.displayName.Contains(name, CompareOptions.OrdinalIgnoreCase))
                    player = sleepingPlayer;
            }
            return player;
        }

        /// <summary>
        /// Returns the BasePlayer for the specified ID ulong
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public static BasePlayer FindPlayerById(ulong id)
        {
            foreach (var activePlayer in BasePlayer.activePlayerList)
            {
                if (activePlayer.userID == id)
                    return activePlayer;
            }
            foreach (var sleepingPlayer in BasePlayer.sleepingPlayerList)
            {
                if (sleepingPlayer.userID == id)
                    return sleepingPlayer;
            }
            return null;
        }

        /// <summary>
        /// Returns the BasePlayer for the specified ID string
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public static BasePlayer FindPlayerByIdString(string id)
        {
            foreach (var activePlayer in BasePlayer.activePlayerList)
            {
                if (string.IsNullOrEmpty(activePlayer.UserIDString)) continue;
                if (activePlayer.UserIDString.Equals(id))
                    return activePlayer;
            }
            foreach (var sleepingPlayer in BasePlayer.sleepingPlayerList)
            {
                if (string.IsNullOrEmpty(sleepingPlayer.UserIDString)) continue;
                if (sleepingPlayer.UserIDString.Equals(id))
                    return sleepingPlayer;
            }
            return null;
        }

        #endregion
    }
}
