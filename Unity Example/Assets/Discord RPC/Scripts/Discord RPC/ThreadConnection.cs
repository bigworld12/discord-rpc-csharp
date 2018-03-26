using DiscordRPC.IO;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using UnityEngine;

public class ThreadConnection
{
	public Thread thread;

	public AutoResetEvent signal = new AutoResetEvent(false);
	public volatile bool isAborting;

	//private ThreadedNamedPipeClientStream _stream;
	private NamedPipeClientStream _stream;

	private long _counter = 0;
	
	private object somelock = new object();
	private string _somestring = "";

	private byte[] buffer = new byte[PipeFrame.MAX_SIZE];
	private IAsyncResult operation;

	private PipeFrame readFrame;
	private bool hasFrameReady = false;

	public string GetSomeString()
	{
		string tmp;
		lock (somelock) tmp = _somestring;
		return tmp; 
	}

	public void SetSomeString(string value)
	{
		lock (somelock) _somestring = value;
		//signal.Set();
	}

	#region Start / Close
	public void Start()
	{
		if (thread != null)
		{
			Debug.Log("T: Cannot start as the thread isn't null.");
			return;
		}

		isAborting = false;
		thread = new Thread(MainLoop);
		thread.IsBackground = true;
		thread.Start();
	}

	public void Close()
	{
		if (isAborting || thread == null)
		{
			Debug.Log("T: Cannot abort as we have already been aborted.");
			return;
		}

		Debug.Log("FFF Signalling");

		isAborting = true;

		//signal.Set();

		Debug.Log("FFF Waiting...");
		thread.Join();

		Debug.Log("FFF Done");
		//if (result != null) result.AsyncWaitHandle.Close();
	}
	#endregion

	private void MainLoop()
	{
		Debug.Log("T: Starting Pipe");
		//_stream = new ThreadedNamedPipeClientStream(".", "discord-ipc-0");
		_stream = new NamedPipeClientStream(".", "discord-ipc-0", PipeDirection.InOut, PipeOptions.Asynchronous);
		_stream.Connect();

		do { Thread.Sleep(100); } while (!_stream.IsConnected);

		//WRite the handshake
		Debug.Log("T: Handshaking");
		WriteHandshake(1, "424087019149328395");

		while (!isAborting && _stream.IsConnected)
		{
			try
			{
				Debug.Log("T: Waiting for frame");
				operation = _stream.BeginRead(buffer, 0, buffer.Length, null, !isAborting);
				operation.AsyncWaitHandle.WaitOne(5000);

				Debug.Log("T: Ending frame read");
				int bytes = _stream.EndRead(operation);
				Debug.Log("Read " + bytes + " bytes");

				//Write something
				Debug.Log("Waiting 10 seconds");
				Thread.Sleep(10000);

				Debug.Log("Saying Goodbye");
				WriteGoodbye(1, "424087019149328395");

				//return back
				Debug.Log("Sleeping 100 ms");
				Thread.Sleep(100);
			}
			catch (Exception e)
			{
				Debug.Log("Exception occured reading: " + e.Message);
				isAborting = true;
			}
		}

		//Close the stream
		Debug.Log("Disposing");
		_stream.Dispose();
	}

	public bool WriteHandshake(int version, string client)
	{
		Debug.Log("S: Writing Handshake: " + version + ", " + client);
		PipeFrame frame = new PipeFrame();
		frame.SetObject(Opcode.Handshake, new Handshake() { Version = version, ClientID = client });

		return WritePipeFrame(frame);
	}
	public void WriteGoodbye(int version, string client)
	{
		PipeFrame frame = new PipeFrame();
		frame.SetObject(Opcode.Close, new Handshake() { Version = version, ClientID = client });

		WritePipeFrame(frame);
	}

	public bool WritePipeFrame(PipeFrame frame)
	{
		Debug.Log("S: Writing Frame");

		//Get all the bytes
		byte[] op = ConvertBytes((uint)frame.Opcode);
		byte[] len = ConvertBytes(frame.Length);
		byte[] data = frame.Data;

		//Copy it all into a buffer
		byte[] buffer = new byte[op.Length + len.Length + data.Length];
		op.CopyTo(buffer, 0);
		len.CopyTo(buffer, op.Length);
		data.CopyTo(buffer, op.Length + len.Length);

		//Write it to the stream
		Debug.Log("S: Write Buffer Frame");
		_stream.Write(buffer, 0, buffer.Length);

		Debug.Log("S: Flush Frame");
		_stream.Flush();

		Debug.Log("S: Drain Frame");
		_stream.WaitForPipeDrain();

		Debug.Log("S: Done Frame");

		return true;
	}
	private byte[] ConvertBytes(uint uint32)
	{
		byte[] bytes = BitConverter.GetBytes(uint32);
		if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);		
		return bytes;
	}

}
