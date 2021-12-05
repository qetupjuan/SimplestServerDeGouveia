using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;
using UnityEngine.UI;

public class NetworkedServer : MonoBehaviour
{
    int maxConnections = 1000;
    int reliableChannelID;
    int unreliableChannelID;
    int hostID;
    int socketPort = 5491;

    List<PlayerAccount> playerAccounts;
    string playerAccountsFilePath;

    int playerWaitingForMatchWithId = -1;

    LinkedList<GameRoom> gameRooms;
    LinkedList<PlayerAccount> loggedInPlayerAccounts;
    GameRoom gr;

    // Start is called before the first frame update
    void Start()
    {
        NetworkTransport.Init();
        ConnectionConfig config = new ConnectionConfig();
        reliableChannelID = config.AddChannel(QosType.Reliable);
        unreliableChannelID = config.AddChannel(QosType.Unreliable);
        HostTopology topology = new HostTopology(config, maxConnections);
        hostID = NetworkTransport.AddHost(topology, socketPort, null);

        playerAccounts = new List<PlayerAccount>();
        playerAccountsFilePath = Application.dataPath + Path.DirectorySeparatorChar + "PlayerAccounts.txt";
        LoadAccountData();
        gameRooms = new LinkedList<GameRoom>();
    }

    // Update is called once per frame
    void Update()
    {
        int recHostID;
        int recConnectionID;
        int recChannelID;
        byte[] recBuffer = new byte[1024];
        int bufferSize = 1024;
        int dataSize;
        byte error = 0;

        NetworkEventType recNetworkEvent = NetworkTransport.Receive(out recHostID, out recConnectionID, out recChannelID, recBuffer, bufferSize, out dataSize, out error);

        switch (recNetworkEvent)
        {
            case NetworkEventType.Nothing:
                break;
            case NetworkEventType.ConnectEvent:
                Debug.Log("Connection, " + recConnectionID);
                break;
            case NetworkEventType.DataEvent:
                string msg = Encoding.Unicode.GetString(recBuffer, 0, dataSize);
                ProcessRecievedMsg(msg, recConnectionID);
                break;
            case NetworkEventType.DisconnectEvent:
                Debug.Log("Disconnection, " + recConnectionID);
                break;
        }

    }

    public void SendMessageToClient(string msg, int id)
    {
        byte error = 0;
        byte[] buffer = Encoding.Unicode.GetBytes(msg);
        NetworkTransport.Send(hostID, id, reliableChannelID, buffer, msg.Length * sizeof(char), out error);
    }

    private void ProcessRecievedMsg(string msg, int id)
    {
        Debug.Log("msg received = " + msg + ".  connection id = " + id + " frame: " + Time.frameCount);
        string[] csv = msg.Split(',');
        int signifier = int.Parse(csv[0]);

        if (signifier == ClientToServerSignifiers.CreateAccount)
        {
            string n = csv[1];
            string p = csv[2];
            bool nameIsInUse = searchAccountsByName(n, out PlayerAccount temp);

            if (nameIsInUse)
            {
                SendMessageToClient(ServerToClientSignifiers.AccountCreationFailed + ", name already in use", id);
            }
            else
            {
                SaveNewUser(new PlayerAccount(n, p));
                SendMessageToClient(ServerToClientSignifiers.AccountCreated + ",Account created", id);
            }
        }
        else if (signifier == ClientToServerSignifiers.Login)
        {
            string n = csv[1];
            string p = csv[2];
            PlayerAccount accountToCheck = null;
            searchAccountsByName(n, out accountToCheck);

            if (accountToCheck == null)
                SendMessageToClient(ServerToClientSignifiers.LoginFailed + "," + "could not find user", id);
            else if (p == accountToCheck.password)
                //login
                SendMessageToClient(ServerToClientSignifiers.LoginComplete + "," + "Logging in", id);
            else //incorrect password
                SendMessageToClient(ServerToClientSignifiers.LoginFailed + "," + "incorrect password", id);

        }
        else if (signifier == ClientToServerSignifiers.JoinGameRoomQueue)
        {
            Debug.Log("waiting to join match");
            if (playerWaitingForMatchWithId == -1)
            {
                playerWaitingForMatchWithId = id;
            }
            else
            {
                GameRoom gr = new GameRoom(playerWaitingForMatchWithId, id);
                gameRooms.AddLast(gr);
                SendMessageToClient(ServerToClientSignifiers.GameStart + "", gr.playerId1);
                SendMessageToClient(ServerToClientSignifiers.GameStart + "", gr.playerId2);
                playerWaitingForMatchWithId = -1;

                //decide who gets the first turn
                bool player1GoesFirst = Random.Range(0, 2) == 0;
                if (player1GoesFirst)
                    SendMessageToClient(ServerToClientSignifiers.ChosenAsPlayerOne + "", gr.playerId1);
                else
                    SendMessageToClient(ServerToClientSignifiers.ChosenAsPlayerOne + "", gr.playerId2);
            }
        }
        else if (signifier == ClientToServerSignifiers.SelectedTicTacToeSquare)
        {
            string newMsg = ServerToClientSignifiers.OpponentChoseASquare + "," + csv[1];
            RelayMessageFromPlayerToOtherPlayer(id, newMsg);

        }
        else if (signifier == ClientToServerSignifiers.LeavingGameRoom)
        {
            GameRoom gr = GetGameRoomFromClientID(id);
            if (gr != null && !gr.gameHasEnded)
            {
                string newMsg = ServerToClientSignifiers.OpponentLeftRoomEarly + "";
                RelayMessageFromPlayerToOtherPlayer(id, newMsg, gr);
            }
        }
        else if (signifier == ClientToServerSignifiers.WonTicTacToe)
        {
            string newMsg = ServerToClientSignifiers.OpponentWonTicTacToe + "";
            RelayMessageFromPlayerToOtherPlayer(id, newMsg);
        }
        else if (signifier == ClientToServerSignifiers.GameTied)
        {
            string newMsg = ServerToClientSignifiers.GameTied + "";
            RelayMessageFromPlayerToOtherPlayer(id, newMsg);
        }
    }

    private bool searchAccountsByName(string name, out PlayerAccount account)
    {
        bool nameIsInUse = false;
        account = null;
        foreach (PlayerAccount pa in playerAccounts)
        {
            if (name == pa.name)
            {
                nameIsInUse = true;
                account = pa;
                break;
            }
        }

        return nameIsInUse;
    }

    private void SaveNewUser(PlayerAccount newPlayerAccount)
    {
        playerAccounts.Add(newPlayerAccount);

        StreamWriter sw = new StreamWriter(playerAccountsFilePath, true);
        foreach (PlayerAccount pa in playerAccounts)
        {
            sw.WriteLine(pa.name + "," + pa.password);
        }
        sw.Close();
    }

    private void LoadAccountData()
    {
        if (File.Exists(playerAccountsFilePath) == false)
            return;

        string line = "";
        StreamReader sr = new StreamReader(playerAccountsFilePath);
        while ((line = sr.ReadLine()) != null)
        {
            string[] cvs = line.Split(',');
            playerAccounts.Add(new PlayerAccount(cvs[0], cvs[1]));
        }
        sr.Close();
    }

    private GameRoom GetGameRoomFromClientID(int id)
    {
        foreach (GameRoom gr in gameRooms)
        {
            if (gr.playerId1 == id || gr.playerId2 == id)
                return gr;
        }
        return null;
    }

    void RelayMessageFromPlayerToOtherPlayer(int fromId, string msg, GameRoom gr = null)
    {
        if (gr == null)
            gr = GetGameRoomFromClientID(fromId);

        if (gr != null)
        {
            int toID = (fromId == gr.playerId1) ? gr.playerId2 : gr.playerId1;
            SendMessageToClient(msg, toID);
        }
    }
}

public class PlayerAccount
{
    public string name, password;

    public PlayerAccount()
    {
    }
    public PlayerAccount(string name, string password)
    {
        this.name = name;
        this.password = password;
    }
}
public class GameRoom
{
    public int playerId1, playerId2;
    public bool gameHasEnded = false;
    public GameRoom(int id1, int id2)
    {
        playerId1 = id1;
        playerId2 = id2;
    }
}

public static class ClientToServerSignifiers
{
    public const int CreateAccount = 1;

    public const int LoginAccount = 2;

    public const int JoinQueue = 3;

    public const int GameButtonPressed = 4;

    public const int ChatMessageSent = 5;

    public const int JoinAsObserver = 6;

    public const int LeaveRoom = 7;

    public const int GetReplay = 8;
}

public static class ServertoClientSignifiers
{
    public const int LoginComplete = 1;

    public const int LoginFailed = 2;

    public const int AccountCreationComplete = 3;

    public const int AccountCreationFailed = 4;

    public const int OpponentPlay = 5;

    public const int GameStart = 6;

    public const int SendChatMessage = 7;

    public const int BackToMainMenu = 8;

    public const int SendReplay = 9;
}
