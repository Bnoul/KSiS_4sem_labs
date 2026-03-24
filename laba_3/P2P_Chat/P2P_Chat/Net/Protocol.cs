using P2P_Chat.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;


namespace P2P_Chat.Net
{
    public static class Protocol
    {
        public static void SendMessage(NetworkStream stream, Message_tpye type, string payload)
        {
            var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            byte t = (byte)type;
            byte[] data = Encoding.UTF8.GetBytes(payload ?? "");
            int len = data.Length;

            writer.Write(t);
            writer.Write(len);
            writer.Write(data);
            writer.Flush();
        }

        public static (Message_tpye Type, string Payload)? ReadMessage(NetworkStream stream)
        {
            var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

            try
            {
                byte t = reader.ReadByte();
                int len = reader.ReadInt32();
                byte[] data = reader.ReadBytes(len);
                string payload = Encoding.UTF8.GetString(data);
                return ((Message_tpye)t, payload);
            }
            catch (IOException)
            {
                return null;
            }
            catch (ObjectDisposedException)
            {
                return null;
            }
        }
    }
}
