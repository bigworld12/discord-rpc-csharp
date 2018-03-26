using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEngine;

namespace DiscordRPC.IO
{
	class ThreadedNamedPipeClientStream
	{
		public NamedPipeClientStream Stream { get { return _stream; } }
		private NamedPipeClientStream _stream;

		private object frame_lock = new object();
		private PipeFrame frame;
		private bool hasframe = false;

		public Thread thread;
		public volatile bool isAborting = false;
		private byte[] buffer = new byte[PipeFrame.MAX_SIZE];

		private IAsyncResult _async;

		public ThreadedNamedPipeClientStream(string serverName, string pipe)
		{
			_stream = new NamedPipeClientStream(serverName, pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
		}

		public void Connect() 
		{
			_stream.Connect();
			isAborting = false;

			//BeginRead();

			thread = new Thread(MainLoop);
			thread.IsBackground = true;
			thread.Start();
		}

		public void Close()
		{
			//Tell everyone we have aborted
			isAborting = true;
			thread.Suspend();
			thread.Abort();
		}

		#region Async
		private void BeginRead()
		{
			if (isAborting) return;
			try
			{
				_async = _stream.BeginRead(buffer, 0, buffer.Length, new AsyncCallback(EndRead), _stream);
			} catch (Exception) { }
		}
		private void EndRead(IAsyncResult result)
		{
			try
			{
				//Attempt to read the bytes. 
				int bytesRead = 0;
				bytesRead = _stream.EndRead(result);

				if (bytesRead > 0)
				{
					//We read some bytes, so read the stream
					using (MemoryStream mem = new MemoryStream(buffer, 0, bytesRead))
					{
						//Attempt to extract the frame from the stream
						PipeFrame f;
						if (TryReadFrame(mem, out f))
						{
							//Enqueue the frame we just read
							lock (frame_lock)
							{
								frame = f;
								hasframe = true;
							}

						}
						else
						{
							//Something went wrong. We could have potentially aborted.
							Debug.LogError("Something went wrong while trying to read a frame");
						}
					}
				}

				//We havn't closed, so start reading again
				if (!isAborting)
					BeginRead();

			}
			catch (Exception)
			{
				return;
			}
		}
		#endregion

		private void MainLoop()
		{
			//Just wait a little bit for the connection to establish
			do { Thread.Sleep(250); } while (!_stream.IsConnected);

			try
			{
				while (!isAborting && _stream.IsConnected)
				{
					Debug.Log("Attempting to read...");
					int bytesRead = _stream.Read(buffer, 0, buffer.Length);

					Debug.Log("Read " + bytesRead + " bytes");
					if (bytesRead > 0)
					{
						using (MemoryStream mem = new MemoryStream(buffer, 0, bytesRead))
						{
							//Attempt to extract the frame from the stream
							PipeFrame f;
							if (TryReadFrame(mem, out f))
							{
								//Enqueue the frame we just read
								lock (frame_lock)
								{
									frame = f;
									hasframe = true;
								}

							}
							else
							{
								//Something went wrong. We could have potentially aborted.
								Debug.LogError("Something went wrong while trying to read a frame");
							}
						}
					}

					Thread.Sleep(1000);
				}

			}
			catch (ThreadAbortException)
			{
				Thread.ResetAbort();
			}

			Debug.Log("A Closing Stream");
			_stream.Flush();

			Debug.Log("B Closing Stream");
			_stream.WaitForPipeDrain();

			Debug.Log("C Closing Stream");
			_stream.Close();

			Debug.Log("D Stream Closed");
		}

		private bool TryReadFrame(Stream stream, out PipeFrame frame)
		{
			//Set the pipe frame to default
			frame = default(PipeFrame);

			//Try to read the values
			uint op;
			if (!TryReadUInt32(stream, out op))
			{
				Debug.LogError("Bad OpCode");
				return false;
			}

			uint len;
			if (!TryReadUInt32(stream, out len))
			{
				Debug.LogError("Bad Length");
				return false;
			}


			//Read the data. This could potentially cause issues if we ever get anything greater than a int.
			//TODO: Better implementation of this read using uints
			byte[] buff = new byte[len];
			int bytesread = stream.Read(buff, 0, buff.Length);

			if (bytesread != len)
			{
				Debug.LogError("Bad Data");
				return false;
			}

			//Create the frame
			frame = new PipeFrame()
			{
				Opcode = (Opcode)op,
				Data = buff
			};

			//Success!
			return true;
		}
		private bool TryReadUInt32(Stream stream, out uint value)
		{
			//Read the bytes
			byte[] bytes = new byte[4];
			int cnt = stream.Read(bytes, 0, bytes.Length);
			if (cnt != 4)
			{
				Debug.LogError("Did not ready 4 bytes!");
				value = 0;
				return false;
			}

			//Convert to int
			if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
			value = BitConverter.ToUInt32(bytes, 0);
			return true;
		}


		/// <summary>
		/// Writes the handshake to the connection
		/// </summary>
		/// <param name="version">Version of the IPC protocol</param>
		/// <param name="client">The client ID</param>
		/// <returns></returns>
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
			_stream.Write(buffer, 0, buffer.Length);
			Debug.Log("S: Done Frame");
			return true;
		}

		private void EndWriteCallback(IAsyncResult result)
		{
			_stream.EndWrite(result);
		}

		/// <summary>
		/// Gets the bytes of a uint32 value in LE format.
		/// </summary>
		/// <param name="uint32"></param>
		/// <returns></returns>
		private byte[] ConvertBytes(uint uint32)
		{
			byte[] bytes = BitConverter.GetBytes(uint32);

			//If we are already in LE, we dont need to flip it
			if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);

			//Give back the bytes
			return bytes;
		}


		public bool Dequeue(out PipeFrame outframe)
		{
			lock (frame_lock)
			{
				if (hasframe)
				{
					outframe = frame;
					hasframe = false;
					return true;
				}
			}

			outframe = default(PipeFrame);
			return false;
		}


	}
}
