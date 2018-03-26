using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ThreadManager : MonoBehaviour {

	public ThreadConnection connection;

	public bool state = false;
	public string somestring = "";
	
	private void OnEnable()
	{
		if (!Application.isPlaying) return;
		StartThread();
	}

	private void OnDisable()
	{
		StopThread();
	}

	private void Update()
	{
		if (connection == null) return;

		state = connection.isAborting;
		somestring = connection.GetSomeString();

		if (Input.GetKeyDown(KeyCode.A))
		{
			StopThread();
		}

		if (Input.GetKeyDown(KeyCode.S))
		{
			connection.SetSomeString("Some Other String");
		}

		if (Input.GetKeyDown(KeyCode.Space))
		{
			connection.SetSomeString("exit");
		}
	}

	public void StartThread()
	{
		if (connection != null) return;

		Debug.Log("Creating Thread...");
		connection = new ThreadConnection();
		connection.Start();
	}

	public void StopThread()
	{
		if (connection == null) return;

		Debug.Log("Stopping Thread...");
		connection.Close();
	}
}
