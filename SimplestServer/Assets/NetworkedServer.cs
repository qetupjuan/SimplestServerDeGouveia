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
    LinkedList<PlayerAccount> playerAccounts;

    const int PlayerAccountRecord = 1;
    string playerAccountDataPath;
    int playerWaitinginQueueID = -1;

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
        config.DisconnectTimeout = 3000;

        playerAccounts = new LinkedList<PlayerAccount>();
        playerAccountDataPath = Application.dataPath + Path.DirectorySeparatorChar + "LoginTrack.txt";

        LoadPlayerAccount();
        gameRooms = new LinkedList<GameRoom>();
        loggedInPlayerAccounts = new LinkedList<PlayerAccount>();
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
                if (playerWaitinginQueueID != -1 && playerWaitinginQueueID == recConnectionID)
                {
                    playerWaitinginQueueID = -1;
                    Debug.Log(playerWaitinginQueueID);
                }
                foreach (PlayerAccount pa in loggedInPlayerAccounts)
                {
                    if (pa.connectionID == recConnectionID)
                    {
                        loggedInPlayerAccounts.Remove(pa);
                        break;
                    }
                }
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
        Debug.Log("msg recieved = " + msg + ".  connection id = " + id);
        string[] csv = msg.Split(',');
        int signifier = int.Parse(csv[0]);

        bool errorFound = false;
        switch (signifier)
        {
            case ClientToServerSignifiers.CreateAccount:

                foreach (PlayerAccount account in playerAccounts)
                {
                    if (csv[1] == account.name)
                    {
                        SendMessageToClient(ServertoClientSignifiers.AccountCreationFailed + "", id);
                        errorFound = true;
                    }
                }
                if (!errorFound)
                {
                    PlayerAccount pa = new PlayerAccount(csv[1], csv[2]);
                    playerAccounts.AddLast(pa);
                    SendMessageToClient(ServertoClientSignifiers.AccountCreationComplete + "", id);

                    SavePlayerAccount();
                }
                break;
            case ClientToServerSignifiers.LoginAccount:

                foreach (PlayerAccount account in playerAccounts)
                {
                    if (csv[1] == account.name)
                    {
                        if (csv[2] == account.password)
                        {
                            SendMessageToClient(ServertoClientSignifiers.LoginComplete + "", id);
                            account.connectionID = id;
                            loggedInPlayerAccounts.AddLast(account);
                            errorFound = true;
                        }
                    }
                }
                if (!errorFound)
                {
                    SendMessageToClient(ServertoClientSignifiers.LoginFailed + "", id);
                }
                break;
            case ClientToServerSignifiers.JoinQueue:

                JoinQueue(id);

                break;
            case ClientToServerSignifiers.GameButtonPressed:

                GameButtonPressed(id, csv);

                break;
            case ClientToServerSignifiers.ChatMessageSent:

                ChatMessageSent(id, csv);

                break;
            case ClientToServerSignifiers.JoinAsObserver:
                if (gameRooms.First.Value != null)
                {
                    gr = gameRooms.First.Value;
                    gr.observerIDs.Add(id);
                    SendMessageToClient(ServertoClientSignifiers.GameStart + "," + gr.player1ID + "," + gr.player2ID + "," + gr.startingPlayer + "," + id, id);
                    if (gr.turnNum > 0)
                    {
                        string[] temp = gr.LoadReplay();
                        foreach (string action in temp)
                        {
                            string[] line = action.Split(',');
                            if (line[0] != null)
                                SendMessageToClient(ServertoClientSignifiers.SendReplay + "," + line[0] + "," + line[1] + "," + 1, id);
                            //SendMessageToClient(ServertoClientSignifiers.SendReplay + "," + line[0] + "," + 1, id);
                        }
                    }
                }
                break;
            case ClientToServerSignifiers.LeaveRoom:
                gr = GetGameRoomWithClientID(id);
                gr.RemovePlayer(id);

                if (gr.player1ID == 0 && gr.player2ID == 0)
                {
                    foreach (int obs in gr.observerIDs)
                    {
                        SendMessageToClient(ServertoClientSignifiers.BackToMainMenu + "", obs);
                    }
                    gameRooms.Remove(gr);
                    gr = null;
                }
                break;
            case ClientToServerSignifiers.GetReplay:
                string[] replay = gr.LoadReplay();
                foreach (string action in replay)
                {
                    string[] line = action.Split(',');
                    if (line != null)
                        SendMessageToClient(ServertoClientSignifiers.SendReplay + "," + line[0] + "," + line[1] + "," + 0, id);
                }
                break;
        }

    }
    private void JoinQueue(int id)
    {
        if (playerWaitinginQueueID == -1)
        {
            playerWaitinginQueueID = id;
        }
        else
        {
            gr = new GameRoom(playerWaitinginQueueID, id);

            foreach (PlayerAccount pa in loggedInPlayerAccounts)
            {
                if (pa.connectionID == playerWaitinginQueueID || pa.connectionID == id)
                {
                    gr.playerAccounts.Add(pa);
                }
            }

            gameRooms.AddLast(gr);
            SendMessageToClient(ServertoClientSignifiers.GameStart + "," + gr.player1ID + "," + gr.player2ID + "," + gr.startingPlayer + "," + id, gr.player1ID);
            SendMessageToClient(ServertoClientSignifiers.GameStart + "," + gr.player2ID + "," + gr.player1ID + "," + gr.startingPlayer + "," + id, gr.player2ID);
            playerWaitinginQueueID = -1;
        }
    }
    private void GameButtonPressed(int id, string[] csv)
    {
        gr = GetGameRoomWithClientID(id);

        string slot = csv[1];
        if (gr != null)
        {
            gr.turnNum++;
            gr.SaveReplay(csv);

            SendMessageToClient(ServertoClientSignifiers.OpponentPlay + "," + slot + "," + csv[2], gr.player2ID);
            SendMessageToClient(ServertoClientSignifiers.OpponentPlay + "," + slot + "," + csv[2], gr.player1ID);
            foreach (int observer in gr.observerIDs)
            {
                SendMessageToClient(ServertoClientSignifiers.OpponentPlay + "," + slot + "," + csv[2], observer);
            }
        }
    }

    private void ChatMessageSent(int id, string[] csv)
    {
        gr = GetGameRoomWithClientID(id);
        PlayerAccount tempPA = null;

        foreach (PlayerAccount pa in loggedInPlayerAccounts)
        {
            if (pa.connectionID == id)
            {
                tempPA = pa;
                break;
            }
        }
        string msg = csv[1];

        SendMessageToClient(ServertoClientSignifiers.SendChatMessage + "," + tempPA.name + "," + msg, gr.player1ID);
        SendMessageToClient(ServertoClientSignifiers.SendChatMessage + "," + tempPA.name + "," + msg, gr.player2ID);
        foreach (int observer in gr.observerIDs)
        {
            SendMessageToClient(ServertoClientSignifiers.SendChatMessage + "," + tempPA.name + "," + msg, observer);
        }
    }

    private void SavePlayerAccount()
    {
        StreamWriter sw = new StreamWriter(playerAccountDataPath);

        foreach (PlayerAccount pa in playerAccounts)
        {
            sw.WriteLine(PlayerAccountRecord + "," + pa.name + "," + pa.password);
        }

        sw.Close();
    }

    private void LoadPlayerAccount()
    {
        if (File.Exists(playerAccountDataPath))
        {
            StreamReader sr = new StreamReader(playerAccountDataPath);
            string line;

            while ((line = sr.ReadLine()) != null)
            {
                string[] csv = line.Split(',');

                int signifier = int.Parse(csv[0]);

                if (signifier == PlayerAccountRecord)
                {
                    PlayerAccount pa = new PlayerAccount(csv[1], csv[2]);
                    playerAccounts.AddLast(pa);
                }
            }

            sr.Close();
        }
    }

    private GameRoom GetGameRoomWithClientID(int id)
    {
        foreach (GameRoom gr in gameRooms)
        {
            if (gr.player1ID == id || gr.player2ID == id)
                return gr;
            foreach (int obs in gr.observerIDs)
            {
                if (obs == id)
                    return gr;
            }
        }
        return null;
    }
}

public class PlayerAccount
{
    public string name, password;

    public int connectionID;
    public PlayerAccount(string Name, string Password)
    {
        name = Name;
        password = Password;
    }
}

public class GameRoom
{
    public int player1ID, player2ID;
    public List<int> observerIDs;
    public int startingPlayer;
    public List<PlayerAccount> playerAccounts;
    public int turnNum;
    public List<string> replayActions;

    public GameRoom(int Player1ID, int Player2ID)
    {
        player1ID = Player1ID;
        player2ID = Player2ID;
        int temp = Random.Range(1, 3);
        if (temp == 1)
            startingPlayer = player1ID;
        else
            startingPlayer = player2ID;

        observerIDs = new List<int>();
        playerAccounts = new List<PlayerAccount>();
        replayActions = new List<string>();
        turnNum = 0;
    }

    public void RemovePlayer(int removedplayerID)
    {
        if (removedplayerID == player1ID)
            player1ID = 0;
        else if (removedplayerID == player2ID)
            player2ID = 0;
        else
        {
            observerIDs.Remove(removedplayerID);
        }
    }

    public void SaveReplay(string[] csv)
    {
        StreamWriter sw = new StreamWriter(Application.dataPath + Path.DirectorySeparatorChar + "replay.txt");
        replayActions.Add(csv[1] + "," + csv[2]);
        foreach (string action in replayActions)
        {
            sw.WriteLine(action);
        }
        sw.Close();
    }

    public string[] LoadReplay()
    {
        if (File.Exists(Application.dataPath + Path.DirectorySeparatorChar + "replay.txt"))
        {
            string[] csv = { "", "", "", "", "", "", "", "", "", "" };
            StreamReader sr = new StreamReader(Application.dataPath + Path.DirectorySeparatorChar + "replay.txt");

            string line;
            int i = 0;
            while ((line = sr.ReadLine()) != null)
            {
                csv[i] = line;
                i++;
            }

            sr.Close();
            return csv;
        }
        return null;
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
