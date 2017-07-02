﻿using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Game.Hurtworld
{
    /// <summary>
    /// Game hooks and wrappers for the core Hurtworld plugin
    /// </summary>
    public partial class HurtworldCore : CSPlugin
    {
        #region Player Hooks

        /// <summary>
        /// Called when the player attempts to craft an item
        /// </summary>
        /// <param name="crafter"></param>
        /// <param name="recipe"></param>
        [HookMethod("ICanCraft")]
        private object ICanCraft(Crafter crafter, ICraftable recipe)
        {
            var session = Player.Find(crafter.networkView.owner);
            if (session == null) return null;

            // Call game hook
            return (bool)Interface.Call("CanCraft", session, recipe);
        }

        /// <summary>
        /// Called when a user is attempting to connect
        /// </summary>
        /// <param name="session"></param>
        /// <returns></returns>
        [HookMethod("IOnUserApprove")]
        private object IOnUserApprove(PlayerSession session)
        {
            session.Identity.Name = session.Identity.Name ?? "Unnamed";
            var id = session.SteamId.ToString();
            var ip = session.Player.ipAddress;

            Covalence.PlayerManager.PlayerJoin(session);

            var loginSpecific = Interface.Call("CanClientLogin", session);
            var loginCovalence = Interface.Call("CanUserLogin", session.Identity.Name, id, ip);
            var canLogin = loginSpecific ?? loginCovalence;

            if (canLogin is string || (canLogin is bool && !(bool)canLogin))
            {
                GameManager.Instance.KickPlayer(id, canLogin is string ? canLogin.ToString() : "Connection was rejected"); // TODO: Localization
                return true;
            }

            var approvedSpecific = Interface.Call("OnUserApprove", session);
            var approvedCovalence = Interface.Call("OnUserApproved", session.Identity.Name, id, ip);
            return approvedSpecific ?? approvedCovalence;
        }

        /// <summary>
        /// Called when the player has connected
        /// </summary>
        /// <param name="name"></param>
        /// <param name="player"></param>
        [HookMethod("IOnPlayerConnected")]
        private void IOnPlayerConnected(uLink.NetworkPlayer player)
        {
            var session = Player.Find(player);
            if (session == null) return;

            // Update player's permissions group and name
            if (permission.IsLoaded)
            {
                var id = session.SteamId.ToString();
                permission.UpdateNickname(id, session.Identity.Name);
                var defaultGroups = Interface.Oxide.Config.Options.DefaultGroups;
                if (!permission.UserHasGroup(id, defaultGroups.Players)) permission.AddUserGroup(id, defaultGroups.Players);
                if (session.IsAdmin && !permission.UserHasGroup(id, defaultGroups.Administrators)) permission.AddUserGroup(id, defaultGroups.Administrators);
            }

            // Call game hook
            Interface.Call("OnPlayerConnected", session);

            // Let covalence know
            Covalence.PlayerManager.PlayerConnected(session);
            var iplayer = Covalence.PlayerManager.FindPlayerById(session.SteamId.ToString());
            if (iplayer != null) Interface.Call("OnUserConnected", iplayer);
        }

        /// <summary>
        /// Called when the player has disconnected
        /// </summary>
        /// <param name="session"></param>
        [HookMethod("OnPlayerDisconnected")]
        private void OnPlayerDisconnected(PlayerSession session)
        {
            // Let covalence know
            var iplayer = Covalence.PlayerManager.FindPlayerById(session.SteamId.ToString());
            if (iplayer != null) Interface.Call("OnUserDisconnected", iplayer, "Unknown");
            Covalence.PlayerManager.PlayerDisconnected(session);
        }

        /// <summary>
        /// Called when the server receives input from the player
        /// </summary>
        /// <param name="player"></param>
        /// <param name="input"></param>
        [HookMethod("IOnPlayerInput")]
        private void IOnPlayerInput(uLink.NetworkPlayer player, InputControls input)
        {
            var session = Player.Find(player);
            if (session != null) Interface.Call("OnPlayerInput", session, input);
        }

        /// <summary>
        /// Called when the player attempts to suicide
        /// </summary>
        /// <param name="player"></param>
        [HookMethod("IOnPlayerSuicide")]
        private object IOnPlayerSuicide(uLink.NetworkPlayer player)
        {
            var session = Player.Find(player);
            return session != null ? Interface.Call("OnPlayerSuicide", session) : null;
        }

        /// <summary>
        /// Called when the player attempts to suicide
        /// </summary>
        /// <param name="player"></param>
        [HookMethod("IOnPlayerVoice")]
        private object IOnPlayerVoice(uLink.NetworkPlayer player)
        {
            var session = Player.Find(player);
            return session != null ? Interface.Call("OnPlayerVoice", session) : null;
        }

        #endregion

        #region Entity Hooks

        /// <summary>
        /// Called when an entity takes damage
        /// </summary>
        /// <param name="effect"></param>
        /// <param name="target"></param>
        /// <param name="source"></param>
        [HookMethod("IOnTakeDamage")]
        private void IOnTakeDamage(EntityEffectFluid effect, EntityStats target, EntityEffectSourceData source)
        {
            if (effect == null || target == null || source?.Value == 0) return;
            var entity = target.GetComponent<AIEntity>();
            if (entity != null)
            {
                Interface.CallHook("OnEntityTakeDamage", entity, source);
                return;
            }
            var networkView = target.GetComponent<uLinkNetworkView>();
            if (networkView == null) return;
            var session = GameManager.Instance.GetSession(networkView.owner);
            if (session != null) Interface.CallHook("OnPlayerTakeDamage", session, source);
        }

        #endregion

        #region Structure Hooks

        /// <summary>
        /// Called when a single door is used
        /// </summary>
        /// <param name="door"></param>
        /// <returns></returns>
        [HookMethod("IOnSingleDoorUsed")]
        private void IOnSingleDoorUsed(DoorSingleServer door)
        {
            var player = door.LastUsedBy;
            if (player == null) return;

            var session = Player.Find((uLink.NetworkPlayer)player);
            if (session != null) Interface.Call("OnSingleDoorUsed", door, session);
        }

        /// <summary>
        /// Called when a double door is used
        /// </summary>
        /// <param name="door"></param>
        /// <returns></returns>
        [HookMethod("IOnDoubleDoorUsed")]
        private void IOnDoubleDoorUsed(DoubleDoorServer door)
        {
            var player = door.LastUsedBy;
            if (player == null) return;

            var session = Player.Find((uLink.NetworkPlayer)player);
            if (session != null) Interface.Call("OnDoubleDoorUsed", door, session);
        }

        /// <summary>
        /// Called when a garage door is used
        /// </summary>
        /// <param name="door"></param>
        /// <returns></returns>
        [HookMethod("IOnGarageDoorUsed")]
        private void IOnGarageDoorUsed(GarageDoorServer door)
        {
            var player = door.LastUsedBy;
            if (player == null) return;

            var session = Player.Find((uLink.NetworkPlayer)player);
            if (session != null) Interface.Call("OnGarageDoorUsed", door, session);
        }

        #endregion

        #region Vehicle Hooks

        /// <summary>
        /// Called when a player tries to enter a vehicle
        /// </summary>
        /// <param name="session"></param>
        /// <param name="go"></param>
        /// <returns></returns>
        [HookMethod("ICanEnterVehicle")]
        private object ICanEnterVehicle(PlayerSession session, GameObject go)
        {
            return Interface.Call("CanEnterVehicle", session, go.GetComponent<VehiclePassenger>());
        }

        /// <summary>
        /// Called when a player tries to exit a vehicle
        /// </summary>
        /// <param name="vehicle"></param>
        /// <returns></returns>
        [HookMethod("ICanExitVehicle")]
        private object ICanExitVehicle(VehiclePassenger vehicle)
        {
            var session = Player.Find(vehicle.networkView.owner);
            return session != null ? Interface.Call("CanExitVehicle", session, vehicle) : null;
        }

        /// <summary>
        /// Called when a player enters a vehicle
        /// </summary>
        /// <param name="player"></param>
        /// <param name="vehicle"></param>
        /// <returns></returns>
        [HookMethod("IOnEnterVehicle")]
        private void IOnEnterVehicle(uLink.NetworkPlayer player, VehiclePassenger vehicle)
        {
            var session = Player.Find(player);
            Interface.Call("OnEnterVehicle", session, vehicle);
        }

        /// <summary>
        /// Called when a player exits a vehicle
        /// </summary>
        /// <param name="player"></param>
        /// <param name="vehicle"></param>
        /// <returns></returns>
        [HookMethod("IOnExitVehicle")]
        private void IOnExitVehicle(uLink.NetworkPlayer player, VehiclePassenger vehicle)
        {
            var session = Player.Find(player);
            Interface.Call("OnExitVehicle", session, vehicle);
        }

        #endregion
    }
}
