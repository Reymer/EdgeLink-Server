using VARLive.ApexNetwork;
using System;
using UnityEngine;

public class CubeController : MonoBehaviour {
	public float speed = 5f;
	public User user;

	private void Start() {
		if ( ApexController.apex.me != user ) {
			ApexController.gameChannel.BindInMainThread<int, Vector3>( "cube", UpdatePosition );
		}
	}

	private void OnDestroy() {
		if ( ApexController.apex.me != user ) {
			ApexController.gameChannel.UnbindInMainThread<int, Vector3>( "cube", UpdatePosition );
		}
	}

	private void UpdatePosition( int UserID, Vector3 position ) {
		if ( UserID != user.UserID )
			return;

		transform.position = position;
	}

	private void Update() {
		if ( ApexController.apex.me == user ) {
			float horizontal = Input.GetAxis( "Horizontal" );
			float vertical = Input.GetAxis( "Vertical" );
			transform.Translate( new Vector3( horizontal, vertical ) * speed * Time.deltaTime );
			
			ApexController.gameChannel.TriggerUdp( "cube", user.UserID, transform.position );
		}
	}
}