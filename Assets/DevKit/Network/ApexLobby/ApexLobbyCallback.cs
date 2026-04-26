using System.Collections;
using System.Collections.Generic;
using VARLive.ApexNetwork;

public interface ApexLobbyCallback
{
    void OnConnected();
    void OnDisconnected();
    void OnJoinLobby();
    void OnRoomListUpdate(List<ApexRoom> rooms);
    void OnCreateRoom(ApexRoom room);
    void OnCreateRoomFailed(int errorCode);
    void OnJoinedRoom();
    void OnJoinRoomFailed(int errorCode);
    void OnLeftRoom();
    void OnRoomDeleted(ApexRoom room);
    void OnPlayerEnteredRoom(User newPlayer);
    void OnPlayerLeftRoom(User otherPlayer);
    void OnRoomMasterSwitched(User newMaster);
}
