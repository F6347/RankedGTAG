using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using Cinemachine.Utility;
using UnityEngine;
using UnityEngine.UI;
using static System.Math;

namespace RankedGTAG
{
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        bool isWatchActive;

        GorillaTagManager tagManager;

        GameObject huntWatch;

        Text watchText;

        Image watchColor;

        float mmr;

        float delay = 11;

        float points;

        bool inInfectionRoom;

        NetPlayer localPlayer;

        NetPlayer lastPlayerTagged;

        NetPlayer lastActuallyTaggedPlayer;

        Dictionary<NetPlayer, VRRig> vrrigs = new Dictionary<NetPlayer, VRRig>();

        Dictionary<NetPlayer, object[]> vrrigsPositions = new Dictionary<NetPlayer, object[]>();

        Dictionary<NetPlayer, float> allPlayersPoints = new Dictionary<NetPlayer, float>();

        NetPlayer[] playerList;
        NetPlayer[] allPlayers;

        bool isLatestUpdate = true;

        bool connectedToWifi = true;

        ConfigEntry<bool> requiresUpdate;


        void OnEnable()
        {
            requiresUpdate = Config.Bind("General",
                "requiresUpdate",
                true,
                "Display a warning to update to use the latest version");

            if (requiresUpdate.Value)
            {
                try { isLatestUpdate = new WebClient().DownloadString("https://raw.githubusercontent.com/F6347/RankedGTAG/refs/heads/master/RankedGTAG/PluginInfo.cs").Contains(PluginInfo.Version); } // 🐀
                catch { connectedToWifi = false; }
                
            }
            
            GorillaTagger.OnPlayerSpawned(OnGameInitialized);
            HarmonyPatches.ApplyHarmonyPatches();
        }

        void OnGameInitialized()
        {

            mmr = PlayerPrefs.GetFloat("PlayerMMR");

            NetworkSystem.Instance.OnJoinedRoomEvent += OnJoinedLobby;


            huntWatch = GorillaTagger.Instance.offlineVRRig.huntComputer;
            tagManager = GameObject.Find("GT Systems/GameModeSystem/Gorilla Tag Manager").GetComponent<GorillaTagManager>(); // i'm sorry for using GameObject.Find, but I tried everything, and nothing worked. So before you do a pull request, PLEASE try it and see if "(GorillaTagManager)GorillaGameManager.instance" will actually work in this case. (i've tried that)


            if (!isLatestUpdate || !connectedToWifi)
            {
                SetWatchActive(true);
                watchText.text = connectedToWifi ? "UPDATE!" : "CONNECT TO WIFI!";
                watchColor.color = Color.red;
            }


            Update(); // this was added so Update wouldnt be grayed out, i dispise visual studio, fuck visual studio. why can i not turn that off? Words would get me banned from GTMG if i sayed how much i dispise microsoft and vs. so i will not.
        }

        void Update()
        {
            if (!inInfectionRoom) return;

            delay += Time.deltaTime;

            if (delay > 2)
            {
                delay = 0;

                foreach (var player in allPlayers)
                {
                    try { var vrrig = vrrigs[player]; }
                    catch { return; }

                    Vector3 vrrigOldPos = (Vector3)vrrigsPositions[player][1];

                    Vector3 vrrigPosDif = vrrigs[player].transform.position.Abs() - vrrigOldPos;

                    vrrigsPositions[player][0] = vrrigPosDif.magnitude > 2f;

                    vrrigsPositions[player][1] = vrrigs[player].transform.position.Abs();
                }
            }


            var otherPlayer = GorillaTagger.Instance.otherPlayer;

            if (otherPlayer != lastPlayerTagged && otherPlayer != null && !GorillaLocomotion.Player.Instance.disableMovement)
            {
                lastActuallyTaggedPlayer = otherPlayer;
                points += allPlayersPoints[otherPlayer] / 20 + 30;
                Debug.Log($"{localPlayer.NickName} has tagged {otherPlayer.NickName}!!");
            }

            lastPlayerTagged = otherPlayer;

            foreach (var player in playerList)
            {
                bool isPlayerNotMoving = true;
                
                try { isPlayerNotMoving = !(bool)vrrigsPositions[player][0]; } catch { }

                if (tagManager.currentInfected.Contains(player) || isPlayerNotMoving) continue;
                try
                {
                    allPlayersPoints[player] += Time.deltaTime;
                }
                catch
                {
                    continue;
                }
            }

            if (!tagManager.IsInfected(localPlayer)) points += Time.deltaTime * ((bool)vrrigsPositions[localPlayer][0] ? 1 : 0.5f);

            if (tagManager.currentInfected.Count == NetworkSystem.Instance.RoomPlayerCount) OnRoundRestarted();


            
            watchText.text = $"MMR: {Round(mmr, 2)}\nPOINTS: {Round(points)} \n{(tagManager.IsInfected(localPlayer) ? $"LAST TAG:        {(lastActuallyTaggedPlayer == null ? "NONE" : lastActuallyTaggedPlayer.NickName)}" : (bool)vrrigsPositions[localPlayer][0] ? "" : "MOVE!")}";
            if (lastActuallyTaggedPlayer!=null) watchColor.color = vrrigs[lastActuallyTaggedPlayer].playerColor;
        }

        

        async void OnJoinedLobby()
        {
            if (!NetworkSystem.Instance.GameModeString.Contains("INFECTION")) return;

            Debug.Log("joined code");

            SetWatchActive(true);

            await Task.Delay(500);

            points = PlayerPrefs.GetFloat("StartingPlayerPoints");

            localPlayer = NetworkSystem.Instance.LocalPlayer;

            NetworkSystem.Instance.OnReturnedToSinglePlayer += OnLeftLobby;

            NetworkSystem.Instance.OnPlayerJoined += OnPlayerJoined;
            NetworkSystem.Instance.OnPlayerLeft += OnPlayerLeft;

            

            inInfectionRoom = true;
        }

        void OnLeftLobby()
        {
            allPlayersPoints.Clear();
            vrrigs.Clear();
            vrrigsPositions.Clear();
            SetWatchActive(false);

            

            NetworkSystem.Instance.OnReturnedToSinglePlayer -= OnLeftLobby;

            NetworkSystem.Instance.OnPlayerJoined -= OnPlayerJoined;
            NetworkSystem.Instance.OnPlayerLeft -= OnPlayerLeft;

            inInfectionRoom = false;

        }

        void OnPlayerJoined(NetPlayer player)
        {
            playerList = NetworkSystem.Instance.PlayerListOthers;
            allPlayers = NetworkSystem.Instance.AllNetPlayers;

            SetAllPlayerPoints();
        }

        void OnPlayerLeft(NetPlayer player)
        {
            playerList = NetworkSystem.Instance.PlayerListOthers;
            allPlayers = NetworkSystem.Instance.AllNetPlayers;

            allPlayersPoints.Remove(player);
            vrrigs.Remove(player);
            vrrigsPositions.Remove(player);
        }

        async void SetAllPlayerPoints()
        {
            await Task.Delay(100);

            foreach (var player in GameObject.FindGameObjectsWithTag("GorillaPlayer"))
            {
                var playerVRRig = player.GetComponent<VRRig>();

                vrrigsPositions.TryAdd(playerVRRig.OwningNetPlayer, new object[] { false, Vector3.zero });

                vrrigs.TryAdd(playerVRRig.OwningNetPlayer, playerVRRig);

                allPlayersPoints.TryAdd(playerVRRig.OwningNetPlayer, 0);
                
            }
        }
       
        async void OnRoundRestarted()
        {
            await Task.Delay(500);

            // ask two times to verify if the round is actually over, evits errors
            if (points == -0.1f || tagManager.currentInfected.Count != NetworkSystem.Instance.RoomPlayerCount) return;

            Debug.Log("round finished");

            Debug.Log (points);

            mmr += points / 10 - mmr;

            PlayerPrefs.SetFloat("PlayerMMR", mmr);
            PlayerPrefs.SetFloat("StartingPlayerPoints", points);

            points = -0.1f;

            Debug.Log(mmr);

            var allPlayersPointsCopy = new Dictionary<NetPlayer, float>();

            foreach (var point in allPlayersPoints)
            {
                allPlayersPointsCopy.Add(point.Key, point.Value);
            }

            foreach (var point in allPlayersPointsCopy)
            {
                Debug.Log(point.Key.NickName + ' ' + point.Value.ToString());
                allPlayersPoints[point.Key] /= 2;
            }
        }

        void SetWatchActive(bool setActive)
        {
            if (isWatchActive==setActive) return;
            isWatchActive = setActive;
            var huntComputer = huntWatch.GetComponent<GorillaHuntComputer>();

            huntComputer.face.gameObject.SetActive(!setActive);
            huntComputer.badge.gameObject.SetActive(!setActive);
            huntComputer.hat.gameObject.SetActive(!setActive);
            huntComputer.leftHand.gameObject.SetActive(!setActive);
            huntComputer.rightHand.gameObject.SetActive(!setActive);

            watchText = huntComputer.text; 
            watchColor = huntComputer.material;

            huntComputer.text.transform.Translate(new Vector3(0, setActive ? 0.005f : -0.005f, 0));
            huntComputer.text.transform.localScale = new Vector3(setActive ? 0.00055f : 0.0007f, setActive ? 0.00055f : 0.0007f, setActive ? 0.00055f : 0.0007f);

            huntComputer.enabled = !setActive;
            huntWatch.SetActive(setActive);
        }
    }
}
