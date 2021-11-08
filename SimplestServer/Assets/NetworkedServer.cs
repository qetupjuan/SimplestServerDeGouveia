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
    string saveDataPath;

    int playerWaitingForMatchWithId = -1;

    LinkedList<GameRoom> gameRooms;

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
        saveDataPath = Application.dataPath + Path.DirectorySeparatorChar + "playerAccountData.txt";
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

        string[] cvs = msg.Split(',');

        int signifier = int.Parse(cvs[0]);
        string n = cvs[1];
        string p = cvs[2];

        if (signifier == ClientToServerSignifiers.CreateAccount)
        {
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
            Debug.Log("client is waiting to join game");
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
            }
        }
        else if (signifier == ClientToServerSignifiers.SelectedTicTacToeSquare)
        {
            GameRoom gr = GetGameRoomFromClientID(id);

            if (gr != null)
            {
                if (gr.playerId1 == id)
                    SendMessageToClient(ServerToClientSignifiers.OpponentPlay + ",Opponent played", gr.playerId2);
                else
                    SendMessageToClient(ServerToClientSignifiers.OpponentPlay + ",Opponent played", gr.playerId1);
            }

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

        StreamWriter sw = new StreamWriter(saveDataPath, true);
        foreach (PlayerAccount pa in playerAccounts)
        {
            sw.WriteLine(pa.name + "," + pa.password);
        }
        sw.Close();
    }

    private void LoadAccountData()
    {
        if (File.Exists(saveDataPath) == false)
            return;

        string line = "";
        StreamReader sr = new StreamReader(saveDataPath);
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

}


public static class ClientToServerSignifiers
{
    public const int CreateAccount = 1;
    public const int Login = 2;
    public const int JoinGameRoomQueue = 3;
    public const int SelectedTicTacToeSquare = 4;
}

public static class ServerToClientSignifiers
{
    public const int LoginComplete = 1;
    public const int LoginFailed = 2;

    public const int AccountCreated = 3;
    public const int AccountCreationFailed = 4;

    public const int OpponentPlay = 5;
    public const int GameStart = 6;
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

    public GameRoom(int id1, int id2)
    {
        playerId1 = id1;
        playerId2 = id2;
    }
}