using VARLive.ApexNetwork;
using System.Collections.Generic;
using UnityEngine;

public class ApexController : MonoBehaviour {

	public static Apex apex;
	public static PresenceChannel gameChannel;

	public GameObject playerPrefab;
	public GameObject player;
	public Dictionary<int, GameObject> otherPlayers = new Dictionary<int, GameObject>();

	void Awake() {
		apex = new Apex( "127.0.0.1", "" );
		apex.RegisterOnConnectEvent( Apex_onConnect );
		apex.Connect();

		playerPrefab.SetActive( false );
	}

	private void Apex_onConnect() {
		Debug.Log( "Apex Controller Connect!" );

		gameChannel = apex.CreateChannel( "presence-game-example" ) as PresenceChannel;
		gameChannel.RegisterOnSubscribeEvent( OnJoinGame );
		gameChannel.RegisterOnAddMemeberEvent( OnAddMember );
		gameChannel.RegisterOnRemoveMemeberEvent( OnRemoveMember );
		apex.Subscribe( gameChannel );
		apex.StartUdp();
	}

	private void OnJoinGame() {
		Debug.Log( "Player Count = " + gameChannel.members.Count );
		Debug.LogFormat( "My UserID: {0}, Address: {1}, MachineName: {2}", apex.me.UserID, apex.me.Address, apex.me.MachineName );

		player = Instantiate( playerPrefab, Vector3.zero, Quaternion.identity );
		player.SetActive( true );
		player.GetComponentInChildren<TextMesh>().text = "YOU";
		player.GetComponent<CubeController>().user = apex.me;

		for ( int i = 0; i < gameChannel.members.Count; i++ ) {
			if ( gameChannel.members[ i ].Equals( apex.me ) )
				continue;

			GameObject otherPlayer = Instantiate( playerPrefab, Vector3.zero, Quaternion.identity );
			otherPlayer.SetActive( true );
			otherPlayer.GetComponentInChildren<TextMesh>().text = gameChannel.members[ i ].UserID.ToString();
			otherPlayer.GetComponent<CubeController>().user = gameChannel.members[ i ];
			otherPlayers.Add( gameChannel.members[ i ].UserID, otherPlayer );
		}
	}

	private void OnAddMember( User user ) {
		Debug.LogFormat( "Player Enter! UserID: {0}, MachineName: {1}", user.UserID, user.MachineName );

		GameObject otherPlayer = Instantiate( playerPrefab, Vector3.zero, Quaternion.identity );
		otherPlayer.SetActive( true );
		otherPlayer.GetComponentInChildren<TextMesh>().text = user.UserID.ToString();
		otherPlayer.GetComponent<CubeController>().user = user;
		otherPlayers.Add( user.UserID, otherPlayer );
	}

	private void OnRemoveMember( User user ) {
		Debug.LogFormat( "Player Leave! UserID: {0}", user.UserID );

		if ( !otherPlayers.ContainsKey( user.UserID ) )
			return;

		Destroy( otherPlayers[ user.UserID ] );
		otherPlayers.Remove( user.UserID );
	}

	void OnApplicationQuit() {
		apex.Disconnect();
	}
}