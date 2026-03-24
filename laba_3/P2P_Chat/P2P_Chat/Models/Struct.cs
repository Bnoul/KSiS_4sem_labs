using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace P2P_Chat.Models
{
    public class Chat_event
    {
        public DateTime Timestamp { get; set; }
        public string EventText { get; set; } = "";
    }

    public enum Message_tpye : byte
    {
        Text = 0,
        Name = 1,
        User_connected = 2,
        User_disconnected = 3,
        History_req = 4,
        History_res = 5
    }
}
