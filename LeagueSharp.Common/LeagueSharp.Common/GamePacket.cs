﻿#region

using System;
using System.IO;
using System.Text;

#endregion

namespace LeagueSharp.Common
{
    /// <summary>
    /// This class makes easier to handle packets.
    /// </summary>
    public class GamePacket
    {
        private readonly BinaryReader Br;
        private readonly BinaryWriter Bw;
        private readonly MemoryStream Ms;
        private readonly byte[] rawPacket;

        public GamePacket(byte[] data)
        {
            Ms = new MemoryStream(data);
            Br = new BinaryReader(Ms);
            Bw = new BinaryWriter(Ms);
            Br.BaseStream.Position = 0;
            Bw.BaseStream.Position = 0;
            rawPacket = data;
        }

        public GamePacket(byte header)
        {
            Ms = new MemoryStream();
            Br = new BinaryReader(Ms);
            Bw = new BinaryWriter(Ms);
            Br.BaseStream.Position = 0;
            Bw.BaseStream.Position = 0;
            WriteByte(header);
        }

        public long Position
        {
            get { return Br.BaseStream.Position; }
            set { Br.BaseStream.Position = value; }
        }

        /// <summary>
        /// Returns the packet size.
        /// </summary>
        public long Size()
        {
            return Br.BaseStream.Length;
        }

        /// <summary>
        /// Reads a byte from the packet and increases the position by 1.
        /// </summary>
        public byte ReadByte()
        {
            return Br.ReadBytes(1)[0];
        }

        /// <summary>
        /// Reads and returns a double byte.
        /// </summary>
        public short ReadShort()
        {
            return BitConverter.ToInt16(Br.ReadBytes(2), 0);
        }

        /// <summary>
        /// Reads and returns a float.
        /// </summary>
        public float ReadFloat()
        {
            return BitConverter.ToSingle(Br.ReadBytes(4), 0);
        }

        /// <summary>
        /// Reads and returns an integer.
        /// </summary>
        public int ReadInteger()
        {
            return BitConverter.ToInt32(Br.ReadBytes(4), 0);
        }


        /// <summary>
        /// Writes a byte.
        /// </summary>
        public void WriteByte(byte b)
        {
            Bw.Write(b);
        }

        /// <summary>
        /// Writes a short.
        /// </summary>
        public void WriteShort(short s)
        {
            Bw.Write(s);
        }

        /// <summary>
        /// Writes a float.
        /// </summary>
        public void WriteFloat(float f)
        {
            Bw.Write(f);
        }

        /// <summary>
        /// Writes an integer.
        /// </summary>
        public void WriteInteger(int i)
        {
            Bw.Write(i);
        }

        /// <summary>
        /// Sends the packet
        /// </summary>
        public void Send(PacketChannel channel = PacketChannel.C2S,
            PacketProtocolFlags flags = PacketProtocolFlags.Reliable)
        {
            Game.SendPacket(Ms.ToArray(), channel, flags);
        }

        /// <summary>
        /// Receives the packet.
        /// </summary>
        public void Process()
        {
            // Game.ProcessPacket(Ms.ToArray());
        }

        /// <summary>
        /// Dumps the packet.
        /// </summary>
        public string Dump()
        {
            var result = new StringBuilder(rawPacket.Length * 3);
            foreach (var b in rawPacket)
                result.AppendFormat("{0:X2} ", b);
            return result.ToString();
        }

        /// <summary>
        /// Saves the packet dump to a file
        /// </summary>
        public void SaveToFile(string filePath = "E:\\PacketLog.txt")
        {
            var w = File.AppendText(filePath);
            w.WriteLine(Dump());
            w.Close();
        }
    }
}