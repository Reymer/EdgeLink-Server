using System;
using System.Collections.Generic;
using System.Linq;
using VARLive.ApexNetwork;

public enum RoomStatus
{
    Close,
    Open,
}

/// <summary>
/// 大廳系統提供的房間功能，請注意除非你熟悉ApexLobby系統，否則<strong>不建議</strong>使用被標記為internal的方法，
/// </summary>
public class ApexRoom
{
    #region 創建房間
    /// <summary>
    /// 創建一個房間，此方法通常由ApexLobby系統呼叫
    /// </summary>
    internal static ApexRoom Create(Apex apex, string guid, string roomName, int createrID, int maxUser, string password, string customizedData, bool isTraining, Action<string, int> OnCreateCallback)
    {
        ApexRoom apexRoom = new ApexRoom();
        apexRoom.SetApex(apex);
        apexRoom.SetGuid(guid);
        apexRoom.SetName(roomName);
        apexRoom.SetMasterID(createrID);
        apexRoom.SetPassword(password);
        apexRoom.SetCustomizedData(customizedData);
        apexRoom.SetCallback(OnCreateCallback);
        apexRoom.roomChannel = apex.CreateChannel(roomName);
        apexRoom.roomChannel.RegisterOnSubscribeEvent(apexRoom.OnChannelCreated);
        apexRoom.MaxUser = Math.Max(1, maxUser);
        apexRoom.RoomStatus = RoomStatus.Open;
        apexRoom.SetIsTraining(isTraining);
        apex.Subscribe(apexRoom.roomChannel);
        return apexRoom;
    }

    /// <summary>
    /// 這個房間提供的網路頻道
    /// </summary>
    public Channel RoomChannel
    {
        get { return roomChannel; }
    }

    /// <summary>
    /// 房間狀態
    /// </summary>
    public RoomStatus RoomStatus
    {
        get; private set;
    }

    public bool RoomTraining
    {
        get; private set;
    }

    /// <summary>
    /// 房間最大玩家數量
    /// </summary>
    public int MaxUser
    {
        get; private set;
    }

    private Action<string, int> OnCreateSuccess;
    private Apex apex;
    private Channel roomChannel;
    private Dictionary<int, User> users = new Dictionary<int, User>();

    private string guid;
    private string roomName;
    private string password;
    private string customizedData;
    private int masterUserID;

    private void SetApex(Apex apex)
    {
        this.apex = apex;
    }

    private void SetGuid(string guid)
    {
        this.guid = guid;
    }

    private void SetName(string roomName)
    {
        this.roomName = roomName;
    }

    private void SetMasterID(int userID)
    {
        masterUserID = userID;
    }

    private void SetPassword(string password)
    {
        this.password = password;
    }

    private void SetCustomizedData(string customizedData)
    {
        this.customizedData = customizedData;
    }

    private void SetCallback(Action<string, int> createRoomCallback)
    {
        OnCreateSuccess = createRoomCallback;
    }

    public void SetIsTraining(bool isTraining)
    {
        this.RoomTraining = isTraining;
    }

    private void OnChannelCreated()
    {
        OnCreateSuccess?.Invoke(roomName, masterUserID);
    }
    #endregion

    #region 內部使用方法
    /// <summary>
    /// 加入一位玩家至房間，此方法通常由ApexLobby系統呼叫
    /// </summary>
    /// <param name="user"></param>
    internal void AddUser(User user)
    {
        if (users.ContainsKey(user.UserID) == false)
        {
            users.Add(user.UserID, user);
        }
    }

    /// <summary>
    /// 從房間移除一位玩家，此方法通常由ApexLobby系統呼叫
    /// </summary>
    /// <param name="user"></param>
    internal void RemoveUser(User user)
    {
        if (users.ContainsKey(user.UserID))
        {
            users.Remove(user.UserID);
        }
    }

    /// <summary>
    /// 解除這個房間的功能，此方法通常由ApexLobby系統呼叫
    /// </summary>
    /// <param name="user"></param>
    internal void DeleteRoom()
    {
        roomChannel.UnregisterOnSubscribeEvent(OnChannelCreated);
        apex.Unsubscribe(roomChannel);
    }

    /// <summary>
    /// 指定切換房間的房主，此方法通常由ApexLobby系統呼叫
    /// </summary>
    /// <param name="user"></param>
    internal void SwitchMaster(int newMasterID)
    {
        if (newMasterID != masterUserID)
        {
            SetMasterID(newMasterID);
        }
    }

    /// <summary>
    /// 更新房間狀態，由系統呼叫
    /// </summary>
    internal void UpdateRoomStatus()
    {
        int usersCount = GetUsersCount();
        if (usersCount >= MaxUser)
        {
            RoomStatus = RoomStatus.Close;
        }
        else
        {
            if (RoomTraining)
                RoomStatus = RoomStatus.Close;
            else
                RoomStatus = RoomStatus.Open;
        }
    }

    /// <summary>
    /// 開放房間，由系統呼叫
    /// </summary>
    internal void OpenRoom()
    {
        RoomStatus = RoomStatus.Open;
    }

    /// <summary>
    /// 關閉房間，由系統呼叫
    /// </summary>
    internal void CloseRoom()
    {
        RoomStatus = RoomStatus.Close;
    }
    #endregion

    #region 外部使用方法
    /// <summary>
    /// 取得房主ID
    /// </summary>
    public int GetMasterID()
    {
        return masterUserID;
    }

    /// <summary>
    /// 取得房間名稱
    /// </summary>
    public string GetName()
    {
        return roomName;
    }

    /// <summary>
    /// 取得房間密碼
    /// </summary>
    public string GetPassword()
    {
        return password;
    }

    /// <summary>
    /// 取得房間GUID
    /// </summary>
    public string GetGuid()
    {
        return guid;
    }

    /// <summary>
    /// 取得組織Org_ID
    /// </summary>
    public string GetCustomizedData()
    {
        return customizedData;
    }

    /// <summary>
    /// 取得房間是否正在訓練
    /// </summary>
    public bool GetIsTraining()
    {
        return RoomTraining;
    }

    /// <summary>
    /// 取得目前在房間裡的所有玩家ID
    /// </summary>
    public int[] GetUsers()
    {
        return users.Keys.ToArray();
    }

    /// <summary>
    /// 取得目前在房間裡的特定玩家
    /// </summary>
    public User GetUser(int userID)
    {
        if (users.ContainsKey(userID))
        {
            return users[userID];
        }
        return null;
    }

    /// <summary>
    /// 取得房間的玩家人數
    /// </summary>
    public int GetUsersCount()
    {
        return users.Count;
    }

    /// <summary>
    /// 判斷一位玩家是不是在這個房間內
    /// </summary>
    public bool IsUserExists(int userID)
    {
        return users.ContainsKey(userID);
    }

    /// <summary>
    /// 判斷密碼是否正確
    /// </summary>
    public bool IsPasswordCorrect(string password)
    {
        return this.password == password;
    }
    #endregion
}
