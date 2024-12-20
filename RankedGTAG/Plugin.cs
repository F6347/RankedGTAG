using System;
using System.Collections.Generic;
using System.Linq;
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

        int roundsCounter;

        float mmr;

        float delay = 69420;

        float points;

        bool inInfectionRoom;

        NetPlayer localPlayer;

        NetPlayer lastPlayerTagged;

        NetPlayer lastActuallyTaggedPlayer;

        NetPlayer[] playerList;
        NetPlayer[] allPlayers;

        Dictionary<NetPlayer, object[]> vrrigsPositions = new Dictionary<NetPlayer, object[]>();

        Dictionary<NetPlayer, float> allPlayersPoints = new Dictionary<NetPlayer, float>();

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
                watchText.text = connectedToWifi ? "\n\nUPDATE!" : "\nCONNECT TO WIFI!";
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
                    try { var vrrig = GetPlayersVRRig(player); }
                    catch { return; }

                    Vector3 vrrigOldPos = (Vector3)vrrigsPositions[player][1];

                    vrrigsPositions[player][0] = (GetPlayersVRRig(player).transform.position.Abs() - vrrigOldPos).magnitude > 2f;

                    vrrigsPositions[player][1] = GetPlayersVRRig(player).transform.position.Abs();
                }
            }


            var otherPlayer = GorillaTagger.Instance.otherPlayer;

            if ((otherPlayer ?? lastPlayerTagged) != lastPlayerTagged && !GorillaLocomotion.Player.Instance.disableMovement)
            {
                lastActuallyTaggedPlayer = otherPlayer;
                points += allPlayersPoints[otherPlayer] / 20 + 30;
                Debug.Log($"{localPlayer.NickName} has tagged {otherPlayer.NickName}!!");
            }

            lastPlayerTagged = otherPlayer;

            foreach (var player in playerList)
            {
                bool isPlayerMoving = false;
                
                try { isPlayerMoving = (bool)vrrigsPositions[player][0]; } catch { }

                if (tagManager.currentInfected.Contains(player) || !isPlayerMoving) continue;
                
                allPlayersPoints[player] += Time.deltaTime;
                
            }

            if (!tagManager.IsInfected(localPlayer)) points += Time.deltaTime * ((bool)vrrigsPositions[localPlayer][0] ? 1 : 0.25f);

            if (tagManager.currentInfected.Count == NetworkSystem.Instance.RoomPlayerCount) OnRoundRestarted();


            
            watchText.text = $"MMR: {Round(mmr, 2)}\nPOINTS: {Round(points)} \n{(tagManager.IsInfected(localPlayer) ? $"LAST TAG:        {(lastActuallyTaggedPlayer == null ? "NONE" : lastActuallyTaggedPlayer.NickName)}" : (bool)vrrigsPositions[localPlayer][0] ? "" : "MOVE!")}";
            if (lastActuallyTaggedPlayer != null) SetMaterialColor(GetPlayersVRRig(lastActuallyTaggedPlayer).playerColor);
            else { SetMaterialColor(Color.clear); }
        }

        

        async void OnJoinedLobby()
        {
            if (!NetworkSystem.Instance.GameModeString.Contains("INFECTION")) return;

            Debug.Log("joined code");

            

            localPlayer = NetworkSystem.Instance.LocalPlayer;

            playerList = NetworkSystem.Instance.PlayerListOthers;
            allPlayers = NetworkSystem.Instance.AllNetPlayers;

            await Task.Delay(500);

            SetWatchActive(true);

            points = PlayerPrefs.GetFloat("StartingPlayerPoints");

            foreach (var playerRig in GameObject.FindGameObjectsWithTag("GorillaPlayer"))
            {
                var playerVRRig = playerRig.GetComponent<VRRig>();

                vrrigsPositions.TryAdd(playerVRRig.OwningNetPlayer, new object[] { false, Vector3.zero });

                allPlayersPoints.TryAdd(playerVRRig.OwningNetPlayer, 0);
            }

            NetworkSystem.Instance.OnReturnedToSinglePlayer += OnLeftLobby;

            NetworkSystem.Instance.OnPlayerJoined += OnPlayerJoined;
            NetworkSystem.Instance.OnPlayerLeft += OnPlayerLeft;

            inInfectionRoom = true;
        }

        void OnLeftLobby()
        {
            allPlayersPoints.Clear();
            vrrigsPositions.Clear();
            SetWatchActive(false);
            roundsCounter = 0;

            NetworkSystem.Instance.OnReturnedToSinglePlayer -= OnLeftLobby;

            NetworkSystem.Instance.OnPlayerJoined -= OnPlayerJoined;
            NetworkSystem.Instance.OnPlayerLeft -= OnPlayerLeft;

            inInfectionRoom = false;
        }

        void OnPlayerJoined(NetPlayer player)
        {
            playerList = NetworkSystem.Instance.PlayerListOthers;
            allPlayers = NetworkSystem.Instance.AllNetPlayers;
 

            vrrigsPositions.TryAdd(player, new object[] { false, Vector3.zero });

            allPlayersPoints.TryAdd(player, 0);
        }

        void OnPlayerLeft(NetPlayer player)
        {
            playerList = NetworkSystem.Instance.PlayerListOthers;
            allPlayers = NetworkSystem.Instance.AllNetPlayers;

            allPlayersPoints.Remove(player);
            vrrigsPositions.Remove(player);
        }
       
        async void OnRoundRestarted()
        {
            await Task.Delay(500);

            // verifies two times to verify if the round is actually over.
            if (points == -0.01f || tagManager.currentInfected.Count != NetworkSystem.Instance.RoomPlayerCount) return;

            Debug.Log("round finished");

            Debug.Log (points);

            mmr += points / 10 - mmr + 3;
            roundsCounter++;

            PlayerPrefs.SetFloat("PlayerMMR", mmr);
            PlayerPrefs.SetFloat("StartingPlayerPoints", points);

            points = -0.01f;

            Debug.Log(mmr);

            var allPlayersPointsCopy = new Dictionary<NetPlayer, float>();

            foreach (var point in allPlayersPoints)
            {
                allPlayersPointsCopy.Add(point.Key, point.Value);
            }

            
            foreach (var point in allPlayersPointsCopy)
            {
                Debug.Log(point.Key.NickName + ' ' + point.Value.ToString());
                if (roundsCounter > 1) allPlayersPoints[point.Key] /= 2;
                else allPlayersPoints[point.Key] = 0;
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
            SetMaterialColor(Color.clear);

            huntComputer.text.transform.Translate(new Vector3(0, setActive ? 0.005f : -0.005f, 0));
            huntComputer.text.transform.localScale = new Vector3(setActive ? 0.00055f : 0.0007f, setActive ? 0.00055f : 0.0007f, setActive ? 0.00055f : 0.0007f);

            huntComputer.enabled = !setActive;
            huntWatch.SetActive(setActive);
        }

        VRRig GetPlayersVRRig(NetPlayer player) 
        { 
            return GameObject.FindGameObjectsWithTag("GorillaPlayer").FirstOrDefault(plyrRig => plyrRig.GetComponent<VRRig>().OwningNetPlayer == player).GetComponent<VRRig>(); 
        }

        void SetMaterialColor(Color color)
        {
            if (color.a != 0f)
            {
                watchColor.gameObject.SetActive(true);
                watchColor.color = color;
            }
            else watchColor.gameObject.SetActive(false);
        }
    }
}
