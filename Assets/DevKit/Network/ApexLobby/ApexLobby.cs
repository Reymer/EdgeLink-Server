using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VARLive.ApexNetwork;

public static class ErrorCode
{
    public const int CreateRoomFailed_SameNameExists = 2000;
    public const int CreateRoomFailed_RoomNameNotValid = 2001;
    public const int CreateRoomFailed_RoomNameIsKeywords = 2002;
    public const int CreateRoomFailed_RoomNameContainIllegalWord = 2003;
    public const int CreateRoomFailed_RoomNameLengthTooLong = 2004;

    public const int JoinRoomFailed_RoomNotFound = 3000;
    public const int JoinRoomFailed_JoinSameRoom = 3001;
    public const int JoinRoomFailed_RoomClose = 3002;
    public const int JoinRoomFailed_WrongPassword = 3003;
}

public interface RoomNameValider
{
    bool CheckRoomName(string roomName, out int errorCode);
}

public class BuiltInRoomNameValider : RoomNameValider
{
    public bool CheckRoomName(string roomName, out int errorCode)
    {
        string[] illegalWord = WordDetection.GetillegalWord();
        foreach (var word in illegalWord)
        {
            if (roomName.Contains(word))
            {
                errorCode = ErrorCode.CreateRoomFailed_RoomNameContainIllegalWord;
                Debug.LogError($"房名包含非法字符[ { roomName} ]");
                return false;
            }
        }

        int maxRoomNameLength = WordDetection.GetMaxRoomNameLength();
        if (roomName.Length > maxRoomNameLength)
        {
            Debug.LogError("房間名稱過長");
            errorCode = ErrorCode.CreateRoomFailed_RoomNameLengthTooLong;
            return false;
        }

        errorCode = -1;
        return true;
    }
}

public class ApexLobby
{
    #region Callback
    private static List<ApexLobbyCallback> callbacks = new List<ApexLobbyCallback>();

    /// <summary>
    /// 註冊Callback事件
    /// </summary>
    public static void RegisterApexLobbyCallback(ApexLobbyCallback callback)
    {
        if (callbacks.Contains(callback) == false)
        {
            callbacks.Add(callback);
        }
    }

    /// <summary>
    /// 解註冊Callback事件
    /// </summary>
    public static void UnregisterApexLobbyCallback(ApexLobbyCallback callback)
    {
        if (callbacks.Contains(callback))
        {
            callbacks.Remove(callback);
        }
    }

    private static void Callback_OnConnected()
    {
        foreach (var callback in callbacks)
        {
            callback.OnConnected();
        }
    }

    private static void Callback_OnDisconnected()
    {
        foreach (var callback in callbacks)
        {
            callback.OnDisconnected();
        }
    }

    private static void Callback_OnJoinLobby()
    {
        foreach (var callback in callbacks)
        {
            callback.OnJoinLobby();
        }
    }

    private static void Callback_OnCreateRoom(ApexRoom room)
    {
        foreach (var callback in callbacks)
        {
            callback.OnCreateRoom(room);
        }
    }

    private static void Callback_OnCreateRoomFailed(int errorCode)
    {
        foreach (var callback in callbacks)
        {
            callback.OnCreateRoomFailed(errorCode);
        }
    }

    private static void Callback_OnJoinedRoom()
    {
        foreach (var callback in callbacks)
        {
            callback.OnJoinedRoom();
        }
    }

    private static void Callback_OnJoinRoomFailed(int errorCode)
    {
        foreach (var callback in callbacks)
        {
            callback.OnJoinRoomFailed(errorCode);
        }
    }

    private static void Callback_OnPlayerEnteredRoom(User newPlayer)
    {
        foreach (var callback in callbacks)
        {
            callback.OnPlayerEnteredRoom(newPlayer);
        }
    }

    private static void Callback_OnLeftRoom()
    {
        foreach (var callback in callbacks)
        {
            callback.OnLeftRoom();
        }
    }

    private static void Callback_OnDeleteRoom(ApexRoom room)
    {
        foreach (var callback in callbacks)
        {
            callback.OnRoomDeleted(room);
        }
    }

    private static void Callback_OnPlayerLeftRoom(User otherPlayer)
    {
        foreach (var callback in callbacks)
        {
            callback.OnPlayerLeftRoom(otherPlayer);
        }
    }

    private static void Callback_OnRoomListUpdate(List<ApexRoom> rooms)
    {
        foreach (var callback in callbacks)
        {
            callback.OnRoomListUpdate(rooms);
        }
    }

    private static void Callback_OnRoomMasterSwitched(User newMaster)
    {
        foreach (var callback in callbacks)
        {
            callback.OnRoomMasterSwitched(newMaster);
        }
    }
    #endregion

    /// <summary>
    /// 玩家當前所在的房間，不在房間的話會回傳null
    /// </summary>
    public ApexRoom CurrentRoom
    {
        get
        {
            string roomName = GetUserStayRoom(LocalUser.UserID);
            ApexRoom currentRoom = GetRoom(roomName);
            return currentRoom;
        }
    }
    
    /// <summary>
    /// 本地玩家
    /// </summary>
    public User LocalUser
    {
        get
        {
            if (apex == null)
                return null;
            else
                return apex.me;
        }
    }

    /// <summary>
    /// 大廳提供的網路頻道
    /// </summary>
    public Channel LobbyChannel
    {
        get
        {
            return lobby;
        }
    }

    private struct SyncCreateRoomData
    {
        public int masterUserID;
        public string guid;
        public string roomName;
        public int[] usersInRoom;
        public int maxUser;
        public int roomStatus;
        public string password;
        public string customizedData;
        public bool isTraining;
    }

    private const string LobbyChannelName = "presence-Lobby";

    private Apex apex;
    private PresenceChannel lobby;
    private List<User> lobbyUsers = new List<User>();
    private Dictionary<string, ApexRoom> rooms = new Dictionary<string, ApexRoom>();
    private RoomNameValider roomNameValider = new BuiltInRoomNameValider();

    public ApexLobby()
    {
    }

    /// <summary>
    /// 建構並自動建立Apex
    /// </summary>
    /// <param name="ipAddress"></param>
    /// <param name="appKey"></param>
    /// <param name="playerName"></param>
    public ApexLobby(string ipAddress, string appKey, string playerName)
    {
        Init(ipAddress, appKey, playerName);
    }

    /// <summary>
    /// 手動建立Apex
    /// </summary>
    /// <param name="ipAddress">ApexCore的IP</param>
    /// <param name="appKey">用於辨識App的名稱</param>
    /// <param name="playerName">玩家名稱</param>
    public void Init(string ipAddress, string appKey, string playerName)
    {
        if (apex != null)
        {
            apex.UnregisterOnConnectEvent(OnConnect);
            apex.UnregisterOnDisconnectEvent(OnDisconnect);
        }
        apex = new Apex(ipAddress, appKey, playerName: playerName);
        apex.RegisterOnConnectEvent(OnConnect);
        apex.RegisterOnDisconnectEvent(OnDisconnect);

    }

    /// <summary>
    /// 開始連線，成功時會呼叫ApexLobbyCallback的OnConnected()，並自動加入大廳，
    /// 如果成功加入大廳，會呼叫OnJoinLobby()
    /// </summary>
    public void StartConnect()
    {
        if (apex != null)
            apex.Connect();
    }

    /// <summary>
    /// 斷開連線，斷開後會呼叫ApexLobbyCallback的OnDisconnected()
    /// </summary>
    public void Disconnect()
    {
        if (apex != null)
        {
            lobby.UnbindInMainThread<int, string, string, int, string, string, bool>("SyncCreateRoom", SyncCreateRoom);
            lobby.UnbindInMainThread<int, string>("SyncJoinRoom", SyncJoinRoom);
            lobby.UnbindInMainThread<int, string>("SyncLeftRoom", SyncLeftRoom);
            lobby.UnbindInMainThread<string, int>("SyncRoomStatus", SyncRoomStatus);
            lobby.UnbindInMainThread<SyncCreateRoomData[]>("SyncRoomList", SyncRoomList);
            lobby.UnregisterOnSubscribeEvent(OnJoinLobby);
            apex.Unsubscribe(lobby);

            rooms.Clear();
            apex.Disconnect();
        }
    }

    private void OnDisconnect(Exception obj)
    {
        string roomName = GetUserStayRoom(LocalUser.UserID);
        SyncLeftRoom(LocalUser.UserID, roomName);
        Callback_OnDisconnected();
    }

    private void OnConnect()
    {
        apex.StartUdp();
        lobby = apex.CreateChannel(LobbyChannelName) as PresenceChannel;
        lobby.RegisterOnSubscribeEvent(OnJoinLobby);
        lobby.RegisterOnAddMemeberEvent(OnAddMember);
        lobby.RegisterOnRemoveMemeberEvent(OnRemoveMember);
        lobby.BindInMainThread<int, string, string, int, string, string, bool>("SyncCreateRoom", SyncCreateRoom);
        lobby.BindInMainThread<int, string>("SyncJoinRoom", SyncJoinRoom);
        lobby.BindInMainThread<int, string>("SyncLeftRoom", SyncLeftRoom);
        lobby.BindInMainThread<string, int>("SyncRoomStatus", SyncRoomStatus);
        lobby.BindInMainThread<SyncCreateRoomData[]>("SyncRoomList", SyncRoomList);
        apex.Subscribe(lobby);

        Callback_OnConnected();
    }

    private void OnJoinLobby()
    {
        UpdateLobbyUsers();
        Callback_OnJoinLobby();
    }

    private void OnAddMember(User newUser)
    {
        UpdateLobbyUsers();
        SendRoomList();
    }

    private void OnRemoveMember(User otherUser)
    {
        string roomName = GetUserStayRoom(otherUser.UserID);
        if (string.IsNullOrEmpty(roomName) == false)
        {
            RemoveUserFromRoom(otherUser, roomName);
        }

        UpdateLobbyUsers();
    }

    private void UpdateLobbyUsers()
    {
        lobbyUsers.Clear();
        foreach (var member in lobby.members)
        {
            lobbyUsers.Add(member);
        }
    }

    /// <summary>
    /// 判斷是否為本地玩家
    /// </summary>
    public bool IsMe(int userID)
    {
        return LocalUser.UserID == userID;
    }

    /// <summary>
    /// 判斷是否不為本地玩家
    /// </summary>
    public bool IsNotMe(int userID)
    {
        return LocalUser.UserID != userID;
    }

    #region 開房
    /// <summary>
    /// 創建一個房間，房間名稱不能相同，創建成功的話會呼叫ApexLobbyCallback的OnCreateRoom()，
    /// 失敗的話會呼叫OnCreateRoomFailed(int errorCode)，如果創建房間成功，玩家會自動加入該房間並呼叫OnJoinedRoom()
    /// </summary>
    /// <param name="roomName">房間名稱</param>
    public void CreateRoom(string roomName, string org_id, int maxUser, string password = "")
    {
        if (IsRoomNameValid(roomName))
        {
            string guid = Guid.NewGuid().ToString();
            lobby.Trigger("SyncCreateRoom", LocalUser.UserID, guid, roomName, maxUser, password, org_id);
        }
    }

    /// <summary>
    /// 設定自訂的房間名稱驗證器
    /// </summary>
    public void SetRoomNameValider(RoomNameValider customValider)
    {
        roomNameValider = customValider;
    }

    private bool IsRoomNameValid(string roomName)
    {
        if (roomNameValider.CheckRoomName(roomName, out int errorCode) == false)
        {
            Callback_OnCreateRoomFailed(errorCode);
            return false;
        }
        if (string.IsNullOrEmpty(roomName))
        {
            Debug.LogError("Create room failed, room name not valid");
            Callback_OnCreateRoomFailed(ErrorCode.CreateRoomFailed_RoomNameNotValid);
            return false;
        }
        if (roomName == LobbyChannelName)
        {
            Debug.LogError("Create room failed, room name is used by lobby system");
            Callback_OnCreateRoomFailed(ErrorCode.CreateRoomFailed_RoomNameIsKeywords);
            return false;
        }
        if (rooms.ContainsKey(roomName))
        {
            Debug.LogError("Create room failed, same room name");
            Callback_OnCreateRoomFailed(ErrorCode.CreateRoomFailed_SameNameExists);
            return false;
        }
        return true;
    }


    private void SyncCreateRoom(int createrID, string guid, string roomName, int maxUser, string password, string org_id, bool isTraining)
    {
        if (rooms.ContainsKey(roomName) == false)
        {
            ApexRoom newRoom = ApexRoom.Create(apex, guid, roomName, createrID, maxUser, password, org_id, isTraining, OnCreateRoom);
            rooms.Add(roomName, newRoom);
        }
    }

    private void OnCreateRoom(string roomName, int createUserID)
    {
        if (rooms.ContainsKey(roomName))
        {
            if (IsMe(createUserID))
            {
                Callback_OnCreateRoom(rooms[roomName]);
            }
        }
        if (IsMe(createUserID))
        {
            JoinRoom(roomName);
            SendRoomList();
        }
    }

    private void SendRoomList()
    {
        List<SyncCreateRoomData> createDatas = new List<SyncCreateRoomData>();
        foreach (var room in rooms.Values)
        {
            SyncCreateRoomData createData = new SyncCreateRoomData()
            {
                masterUserID = room.GetMasterID(),
                guid = room.GetGuid(),
                roomName = room.GetName(),
                password = room.GetPassword(),
                usersInRoom = room.GetUsers(),
                customizedData = room.GetCustomizedData(),
                maxUser = room.MaxUser,
                roomStatus = (int)room.RoomStatus,
                isTraining = room.GetIsTraining(),
            };
            createDatas.Add(createData);
        }

        lobby.Trigger("SyncRoomList", createDatas.ToArray());
    }

    private void SyncRoomList(SyncCreateRoomData[] createDatas)
    {
        for (int i = 0; i < createDatas.Length; i++)
        {
            SyncCreateRoom(createDatas[i].masterUserID, createDatas[i].guid, createDatas[i].roomName, createDatas[i].maxUser, createDatas[i].password, createDatas[i].customizedData, createDatas[i].isTraining);
            SyncInRoomUsers(createDatas[i]);
            SwitchRoomStatus(createDatas[i].roomName, createDatas[i].roomStatus);
        }
        UpdateRoomList();
    }

    /// <summary>
    /// 要求大廳送出房間列表
    /// </summary>
    public void UpdateRoomList()
    {
        List<ApexRoom> roomList = rooms.Values.ToList();
        Callback_OnRoomListUpdate(roomList);
    }

    private void SyncInRoomUsers(SyncCreateRoomData createData)
    {
        if (createData.usersInRoom != null && createData.usersInRoom.Length > 0)
        {
            for (int i = 0; i < createData.usersInRoom.Length; i++)
            {
                User user = GetUser(createData.usersInRoom[i]);
                ApexRoom room = GetRoom(createData.roomName);
                if (user != null && room != null)
                {
                    room.AddUser(user);
                    room.UpdateRoomStatus();
                }
            }
        }
    }

    /// <summary>
    /// 取得玩家
    /// </summary>
    public User GetUser(int userID)
    {
        for (int i = 0; i < lobbyUsers.Count; i++)
        {
            if (lobbyUsers[i].UserID == userID)
            {
                return lobbyUsers[i];
            }
        }
        return null;
    }

    /// <summary>
    /// 取得大廳中所有的使用者
    /// </summary>
    public List<User> GetUsers()
    {
        return lobbyUsers;
    }

    /// <summary>
    /// 取得房間
    /// </summary>
    public ApexRoom GetRoom(string roomName)
    {
        if (rooms.ContainsKey(roomName))
        {
            return rooms[roomName];
        }
        return null;
    }
    #endregion

    #region 加入房間
    /// <summary>
    /// 加入房間，加房成功會呼叫ApexLobbyCallback的OnJoinedRoom()，
    /// 失敗的話會呼叫ApexLobbyCallback的OnJoinRoomFailed(int errorCode)，
    /// 如果玩家已經在一個房間裡，玩家會先離開當前的房間再加入新房間，
    /// 當一位玩家成功加入房間時，其他在房間裡的玩家會收到OnPlayerEnteredRoom(User newPlayer)的callback
    /// </summary>
    /// <param name="roomName">房間名稱</param>
    public void JoinRoom(string roomName)
    {
        if (rooms.ContainsKey(roomName) == false)
        {
            Callback_OnJoinRoomFailed(ErrorCode.JoinRoomFailed_RoomNotFound);
            Debug.LogError("Join room failed, not found");
            return;
        }

        //判斷房間是否還在開放狀態
        if (RoomStatusVaild(roomName) == false)
        {
            Callback_OnJoinRoomFailed(ErrorCode.JoinRoomFailed_RoomClose);
            return;
        }

        //判斷玩家所在房間和要加入的房間是不是同一個
        string otherRoom = GetUserStayRoom(LocalUser.UserID);
        if (otherRoom == roomName)
        {
            Callback_OnJoinRoomFailed(ErrorCode.JoinRoomFailed_JoinSameRoom);
            return;
        }

        //先確認玩家是否不在房間內
        bool isOutside = IsUserOutside(LocalUser.UserID);
        if (isOutside)
        {
            //玩家沒有在任何房間內，正常進房
            lobby.Trigger("SyncJoinRoom", LocalUser.UserID, roomName);
        }
        else
        {
            //如果玩家在另一個房間的話，先退出
            LeftRoom();

            lobby.Trigger("SyncJoinRoom", LocalUser.UserID, roomName);
        }
    }

    private bool IsUserOutside(int userID)
    {
        string roomName = GetUserStayRoom(userID);
        return string.IsNullOrEmpty(roomName);
    }

    private void SyncJoinRoom(int userID, string roomName)
    {
        User user = GetUser(userID);
        if (user != null)
        {
            if (RoomStatusVaild(roomName))
            {
                AddUserToRoom(user, roomName);
            }
            else
            {
                if (IsMe(userID))
                {
                    //如果因為封包延遲或是Race造成錯誤，傳送一個離開房間的回朔用封包
                    Callback_OnJoinRoomFailed(ErrorCode.JoinRoomFailed_RoomClose);
                    LeftRoom(roomName);
                }
            }
        }
    }

    private bool RoomStatusVaild(string roomName)
    {
        if (rooms.ContainsKey(roomName))
        {
            return rooms[roomName].RoomStatus == RoomStatus.Open;
        }
        else
        {
            return false;
        }
    }

    private void AddUserToRoom(User user, string roomName)
    {
        if (rooms.ContainsKey(roomName))
        {
            if (rooms[roomName].IsUserExists(user.UserID) == false)
            {
                rooms[roomName].AddUser(user);
                rooms[roomName].UpdateRoomStatus();
                if (IsMe(user.UserID))
                {
                    Callback_OnJoinedRoom();
                }
                else if (rooms[roomName].IsUserExists(LocalUser.UserID) && IsNotMe(user.UserID))
                {
                    Callback_OnPlayerEnteredRoom(user);
                }
                UpdateRoomList();
            }

        }
    }
    #endregion

    #region 離開房間
    /// <summary>
    /// 離開當前房間，成功的話會呼叫ApexLobbyCallback的OnLeftRoom()，
    /// 並且其他在房間裡的玩家會收到OnPlayerLeftRoom(User otherPlayer)的callback，
    /// 如果離開的玩家是房主，系統會選出一位新的房主並呼叫OnRoomMasterSwitched()的callback，
    /// 如果玩家不在房間裡，這個功能不會做任何事，
    /// 當一個房間完全沒有玩家在裡面時，房間會被自動刪除
    /// </summary>
    public void LeftRoom()
    {
        string roomName = GetUserStayRoom(LocalUser.UserID);
        LeftRoom(roomName);
    }

    private void LeftRoom(string roomName)
    {
        if (string.IsNullOrEmpty(roomName) == false)
        {
            lobby.Trigger("SyncLeftRoom", LocalUser.UserID, roomName);
        }
        else
        {
            Debug.LogWarning("User not in the room");
        }
    }

    private string GetUserStayRoom(int userID)
    {
        foreach (var room in rooms)
        {
            if (room.Value.IsUserExists(userID))
            {
                return room.Key;
            }
        }
        return string.Empty;
    }

    private void SyncLeftRoom(int userID, string roomName)
    {
        if (string.IsNullOrEmpty(roomName) == false)
        {
            User user = GetUser(userID);
            if (user != null)
            {
                RemoveUserFromRoom(user, roomName);
            }
        }
    }

    private void RemoveUserFromRoom(User user, string roomName)
    {
        if (rooms.ContainsKey(roomName))
        {
            ApexRoom room = rooms[roomName];
            if (room.IsUserExists(user.UserID) == true)
            {
                if (IsMe(user.UserID))
                {
                    Callback_OnLeftRoom();
                }
                else if (room.IsUserExists(LocalUser.UserID))
                {
                    Callback_OnPlayerLeftRoom(user);
                }
                room.RemoveUser(user);
                room.UpdateRoomStatus();
            }
            if (room.GetMasterID() == user.UserID)
            {
                //如果離開房間的是房主，嘗試選出一位新的房主
                ElectNewRoomMaster(room);
            }
            if (room.GetUsersCount() <= 0)
            {
                DeleteRoom(roomName);
            }
            UpdateRoomList();
        }
    }

    private void ElectNewRoomMaster(ApexRoom room)
    {
        if (room.GetUsersCount() > 0)
        {
            //選出新房主的條件是該房間內UserID最小的玩家
            int[] users = room.GetUsers();
            int newMasterID = users[0];
            for (int i = 1; i < users.Length; i++)
            {
                if (users[i] < newMasterID)
                {
                    newMasterID = users[i];
                }
            }
            room.SwitchMaster(newMasterID);
            User newMaster = GetUser(newMasterID);
            Callback_OnRoomMasterSwitched(newMaster);
        }
    }

    private void DeleteRoom(string roomName)
    {
        if (rooms.ContainsKey(roomName))
        {
            ApexRoom deleteRoom = rooms[roomName];
            deleteRoom.DeleteRoom();
            rooms.Remove(roomName);

            Callback_OnDeleteRoom(deleteRoom);
        }
    }
    #endregion

    #region 手動開放或關閉房間
    /// <summary>
    /// 手動開放房間，房間在開放狀態允許玩家加入，每次玩家加入後都會更新房間狀態
    /// </summary>
    public void OpenRoom(string roomName)
    {
        LobbyChannel.Trigger("SyncRoomStatus", roomName, (int)RoomStatus.Open);
    }

    /// <summary>
    /// 手動關閉房間，房間在關閉狀態不允許玩家加入，每次玩家加入後都會更新房間狀態
    /// </summary>
    public void CloseRoom(string roomName)
    {
        LobbyChannel.Trigger("SyncRoomStatus", roomName, (int)RoomStatus.Close);
    }

    private void SyncRoomStatus(string roomName, int status)
    {
        SwitchRoomStatus(roomName, status);
        UpdateRoomList();
    }

    private void SwitchRoomStatus(string roomName, int status)
    {
        RoomStatus roomStatus = (RoomStatus)status;
        switch (roomStatus)
        {
            case RoomStatus.Close:
                {
                    SyncCloseRoom(roomName);
                }
                break;
            case RoomStatus.Open:
                {
                    SyncOpenRoom(roomName);
                }
                break;
        }
    }

    private void SyncOpenRoom(string roomName)
    {
        if (rooms.ContainsKey(roomName))
        {
            rooms[roomName].OpenRoom();
            //rooms[roomName].SetIsTraining(false);
        }
    }

    private void SyncCloseRoom(string roomName)
    {
        if (rooms.ContainsKey(roomName))
        {
            rooms[roomName].CloseRoom();
        }
    }
    #endregion
}
