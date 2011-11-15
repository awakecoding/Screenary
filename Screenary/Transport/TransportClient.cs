using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Net.Sockets;

namespace Screenary
{
	public class TransportClient
	{
		private Int32 port;
		private string hostname;
		private TcpClient tcpClient;
		private ChannelDispatcher dispatcher;
		
		public const int PDU_HEADER_SIZE = 6;
		public const int PDU_MAX_FRAG_SIZE = 0x3FFF;
		public const int PDU_MAX_PAYLOAD_SIZE = (PDU_MAX_FRAG_SIZE - PDU_HEADER_SIZE);
		
		public const UInt16 PDU_CHANNEL_SESSION = 0x00;
		public const UInt16 PDU_CHANNEL_UPDATE = 0x01;
		public const UInt16 PDU_CHANNEL_INPUT = 0x02;
		
		public const byte PDU_UPDATE_SURFACE = 0x01;
		
		public const byte PDU_FRAGMENT_SINGLE = 0x00;
		public const byte PDU_FRAGMENT_FIRST = 0x01;
		public const byte PDU_FRAGMENT_NEXT = 0x02;
		public const byte PDU_FRAGMENT_LAST = 0x03;
		
		public TransportClient(TcpClient tcpClient)
		{
			this.dispatcher = null;
			this.tcpClient = tcpClient;
		}
		
		public TransportClient(ChannelDispatcher dispatcher)
		{
			this.dispatcher = dispatcher;
			this.tcpClient = null;
		}
		
		public TransportClient(ChannelDispatcher dispatcher, TcpClient tcpClient)
		{
			this.dispatcher = dispatcher;
			this.tcpClient = tcpClient;
		}
		
		public void SetChannelDispatcher(ChannelDispatcher dispatcher)
		{
			this.dispatcher = dispatcher;
		}
		
		public bool Connect(string hostname, Int32 port)
		{
			this.hostname = hostname;
			this.port = port;
	
			if (tcpClient == null)
				tcpClient = new TcpClient();
			
			tcpClient.Connect(this.hostname, this.port);
	
			return true;
		}
		
		public bool Disconnect()
		{
			tcpClient.Close();	
			return true;
		}
		
		public bool SendPDU(byte[] buffer, UInt16 channelId, byte pduType)
		{
			int offset = 0;
			UInt16 fragSize;
			int totalSize = 0;
			
			BinaryWriter s;
			MemoryStream fstream;
			
			totalSize = (int) buffer.Length;
			byte[] fragment = new byte[PDU_MAX_FRAG_SIZE];
			fstream = new MemoryStream(fragment);
			s = new BinaryWriter(fstream);
			
			if (totalSize <= PDU_MAX_PAYLOAD_SIZE)
			{
				/* Single fragment */

				fragSize = (UInt16) totalSize;
				fstream.Seek(0, SeekOrigin.Begin);
				
				s.Write(channelId);
				s.Write(pduType);
				s.Write(PDU_FRAGMENT_SINGLE);
				s.Write((UInt16) (fragSize + PDU_HEADER_SIZE));
				s.Write(buffer);
	
				tcpClient.GetStream().Write(fragment, 0, fragSize + PDU_HEADER_SIZE);
				offset += fragSize;
				
				return true;
			}
			else
			{
				/* First fragment of a series of fragments */
				
				fragSize = (UInt16) PDU_MAX_PAYLOAD_SIZE;
				fstream.Seek(0, SeekOrigin.Begin);
				
				s.Write(channelId);
				s.Write(pduType);
				s.Write(PDU_FRAGMENT_FIRST);
				s.Write((UInt16) (fragSize + PDU_HEADER_SIZE));
				s.Write(buffer, offset, fragSize);

				tcpClient.GetStream().Write(fragment, 0, fragSize + PDU_HEADER_SIZE);
				offset += fragSize;
				
				while (offset < buffer.Length)
				{					
					if ((totalSize - offset) <= PDU_MAX_PAYLOAD_SIZE)
					{
						/* Last fragment of a series of fragments */
						
						fragSize = (UInt16) (totalSize - offset);
						fstream.Seek(0, SeekOrigin.Begin);
						
						s.Write(channelId);
						s.Write(pduType);
						s.Write(PDU_FRAGMENT_LAST);
						s.Write((UInt16) (fragSize + PDU_HEADER_SIZE));
						s.Write(buffer, offset, fragSize);
						
						tcpClient.GetStream().Write(fragment, 0, fragSize + PDU_HEADER_SIZE);
						offset += fragSize;
						
						return true;
					}
					else
					{
						/* "in between" fragment of a series of fragments */
						
						fragSize = PDU_MAX_PAYLOAD_SIZE;
						fstream.Seek(0, SeekOrigin.Begin);
						
						s.Write(channelId);
						s.Write(pduType);
						s.Write(PDU_FRAGMENT_NEXT);
						s.Write((UInt16) (fragSize + PDU_HEADER_SIZE));
						s.Write(buffer, offset, fragSize);
						
						tcpClient.GetStream().Write(fragment, 0, fragSize + PDU_HEADER_SIZE);
						offset += fragSize;
					}
				}
			}
			
			return true;
		}
		
		public bool RecvPDU()
		{
			byte pduType = 0;
			int totalSize = 0;
			byte fragFlags = 0;
			UInt16 channelId = 0;
			UInt16 fragSize = 0;
			byte[] buffer = null;
			BinaryReader s;
			
			while (tcpClient.GetStream().DataAvailable)
			{
				s = new BinaryReader(tcpClient.GetStream());
				
				channelId = s.ReadUInt16();
				pduType = s.ReadByte();
				fragFlags = s.ReadByte();
				fragSize = s.ReadUInt16();
				
				fragSize -= PDU_HEADER_SIZE;
				
				if (fragSize <= 0)
					continue;
				
				if (fragFlags == PDU_FRAGMENT_SINGLE)
				{
					/* a single fragment */
					
					buffer = new byte[fragSize];
					s.Read(buffer, 0, fragSize);
					totalSize = fragSize;
					
					return dispatcher.DispatchPDU(buffer, channelId, pduType);
				}
				else if (fragFlags == PDU_FRAGMENT_FIRST)
				{
					/* the first of a series of fragments */
					
					buffer = new byte[fragSize];
					s.Read(buffer, 0, fragSize);
					totalSize = fragSize;
				}
				else if (fragFlags == PDU_FRAGMENT_NEXT)
				{
					/* the "in between" of a series of fragments */
					
					Array.Resize(ref buffer, totalSize + fragSize);
					s.Read(buffer, totalSize, fragSize);
					totalSize += fragSize;
				}
				else if (fragFlags == PDU_FRAGMENT_LAST)
				{
					/* The last of a series of fragments */
					
					Array.Resize(ref buffer, totalSize + fragSize);
					s.Read(buffer, totalSize, fragSize);
					totalSize += fragSize;
					
					return dispatcher.DispatchPDU(buffer, channelId, pduType);
				}
			}
			
			return true;
		}
	}
}

