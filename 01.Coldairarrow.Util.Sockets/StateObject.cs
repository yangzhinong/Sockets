using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;

namespace Coldairarrow.Util.Sockets
{
    internal class StateObject
    {
        // Client socket.
        public Socket workSocket = null;
        // Size of receive buffer.
        public  const int BufferSize = 1024; 
        // Receive buffer.
        public byte[] buffer= new byte[BufferSize];
        public byte[] headBuffer = new byte[HeadBufferSize];
        public const int HeadBufferSize = 4;
        // Received data string.
        public byte[] data ;
        public int dataLen = 0; //内容长度
        public int dataRecviedLen = 0; //已接收的长度
     }
}
