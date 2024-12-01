using System.Collections.Generic;
using UnityEngine;
using VARLive.ApexNetwork;

/// <summary>
/// ApexLobbyCallback的Monobehaviour實作版本，會在Awake的時候自動註冊事件，並在OnDestroy的時候解註冊
/// 如果需要Awake的話，請覆寫OnAwake()，
/// 如果需要OnDestroy的話，請覆寫OnDestroyed()
/// </summary>
public class ApexLobbyCallbackBehaviour : MonoBehaviour, ApexLobbyCallback
{
    #region 自動註冊和解註冊事件
    private void Awake()
    {
        ApexLobby.RegisterApexLobbyCallback(this);
        OnAwake();
    }
    protected virtual void OnAwake() { }

    private void OnDestroy()
    {
        ApexLobby.UnregisterApexLobbyCallback(this);
        OnDestroyed();
    }
    protected virtual void OnDestroyed() { }
    #endregion

    #region Callback實作
    /// <summary>
    /// 當連線成功時的callback，你可以在這裡做一些初始化，注意在這個時候還沒進入大廳，
    /// 無法取得大廳的玩家和房間列表
    /// </summary>
    public virtual void OnConnected()
    {
    }

    /// <summary>
    /// 斷線的時候會呼叫的callback，如果玩家在房間內，會先觸發OnLeftRoom()
    /// </summary>
    public virtual void OnDisconnected()
    {
    }

    /// <summary>
    /// 成功加入大廳的callback，可以在這裡取得玩家的列表，也可以開始進行創建房間和加入房間的動作，
    /// 請注意剛加入的時候房間列表不會馬上更新，如果要取得房間列表建議使用OnRoomListUpdate(List<ApexRoom> rooms)
    /// </summary>
    public virtual void OnJoinLobby()
    {
    }

    /// <summary>
    /// 創建房間成功時的callback，創建成功時創房的玩家會馬上加入房間並觸發OnJoinedRoom()，
    /// 注意在這裡使用ApexLobby.CurrentRoom仍然會回傳null，因為玩家並還沒加入房間
    /// </summary>
    public virtual void OnCreateRoom(ApexRoom room)
    {
    }

    /// <summary>
    /// 創建房間失敗時候的callback
    /// </summary>
    /// <param name="errorCode"></param>
    public virtual void OnCreateRoomFailed(int errorCode)
    {
    }

    /// <summary>
    /// 當加入房間成功時的callback，可以用ApexLobby.CurrentRoom取得當前所在房間，
    /// 加入成功時房間裡的其他玩家會收到OnPlayerEnteredRoom(User newPlayer)的callback
    /// </summary>
    public virtual void OnJoinedRoom()
    {
    }

    /// <summary>
    /// 加入房間失敗時候的callback
    /// </summary>
    /// <param name="errorCode"></param>
    public virtual void OnJoinRoomFailed(int errorCode)
    {
    }

    /// <summary>
    /// 離開房間的callback，離開時房間裡的其他玩家會收到OnPlayerLeftRoom(User otherPlayer)的callback
    /// </summary>
    public virtual void OnLeftRoom()
    {
    }

    /// <summary>
    /// 有新玩家加入所在房間時的callback，注意自己加入房間時並不會觸發這個callback而是OnJoinedRoom()
    /// </summary>
    public virtual void OnPlayerEnteredRoom(User newPlayer)
    {
    }

    /// <summary>
    /// 有玩家離開所在房間時的callback，注意自己離開房間時並不會觸發這個callback而是OnLeftRoom()
    /// </summary>
    public virtual void OnPlayerLeftRoom(User otherPlayer)
    {
    }

    /// <summary>
    /// 當房間列表有更新時的callback，建議在這裡取得並更新房間列表的顯示
    /// </summary>
    public virtual void OnRoomListUpdate(List<ApexRoom> rooms)
    {
    }

    /// <summary>
    /// 當房主離開房間時，系統會指派一位新的房主
    /// </summary>
    public virtual void OnRoomMasterSwitched(User newMaster)
    {
    }

    /// <summary>
    /// 當有房間被移除時的callback，當有玩家離開房間時會先觸發OnPlayerLeftRoom(User otherPlayer)，如果房間裡沒有玩家了，則會觸發OnRoomDeleted(ApexRoom room)
    /// </summary>
    public virtual void OnRoomDeleted(ApexRoom room)
    {
    }
    #endregion
}