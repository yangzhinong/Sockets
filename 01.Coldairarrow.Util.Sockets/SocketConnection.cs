using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Coldairarrow.Util.Sockets
{
    /// <summary>
    /// Socket连接,双向通信
    /// </summary>
    public class SocketConnection
    {
        //public static ManualResetEvent allDone = new ManualResetEvent(false);

        private ManualResetEvent readLenDone = new ManualResetEvent(false);

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="socket">维护的Socket对象</param>
        /// <param name="server">维护此连接的服务对象</param>
        public SocketConnection(Socket socket, SocketServer server)
        {
            _socket = socket;
            _server = server;
        }

        #endregion 构造函数

        #region 私有成员

        private readonly Socket _socket;
        private bool _isRec = true;
        private SocketServer _server = null;

        private bool IsSocketConnected()
        {
            bool part1 = _socket.Poll(1000, SelectMode.SelectRead);
            bool part2 = (_socket.Available == 0);
            if (part1 && part2)
                return false;
            else
                return true;
        }

        #endregion 私有成员

        #region 外部接口

        /// <summary>
        /// 开始接受客户端消息
        /// </summary>
        public void StartRecMsg()
        {
            try
            {
                StateObject state = new StateObject();
                state.workSocket = _socket;
                readLenDone.Reset();
                _socket.BeginReceive(state.headBuffer, 0, StateObject.HeadBufferSize, SocketFlags.None, new AsyncCallback(ReadHeadCallback), state);
                readLenDone.WaitOne();
                if (state.dataLen > 0)
                {
                    _socket.BeginReceive(state.buffer, 0, StateObject.BufferSize, SocketFlags.None, new AsyncCallback(ReadBodyCallback), state);
                }
            }
            catch (Exception ex)
            {
                HandleException?.BeginInvoke(ex, null, null);
                Close();
            }
        }

        /// <summary>
        /// 读消息长度
        /// </summary>
        /// <param name="ar"></param>
        public void ReadHeadCallback(IAsyncResult ar)
        {
            try
            {
                String content = String.Empty;
                // Retrieve the state object and the handler socket
                // from the asynchronous state object.
                StateObject state = (StateObject)ar.AsyncState;
                Socket handler = state.workSocket;
                // Read data from the client socket.
                int bytesRead = handler.EndReceive(ar);
                if (bytesRead > 0)
                {
                    state.dataLen = BitConverter.ToInt32(state.headBuffer, 0);
                    //Console.WriteLine("数据长度：" + state.dataLen);
                    if (state.dataLen > 0)
                        state.data = new byte[state.dataLen];
                    readLenDone.Set();
                }
            }
            catch (Exception ex)
            {
                HandleException?.BeginInvoke(ex, null, null);
                Close();
            }
        }

        /// <summary>
        /// 读取消息内容
        /// </summary>
        /// <param name="ar"></param>
        public void ReadBodyCallback(IAsyncResult ar)
        {
            try
            {
                String content = String.Empty;
                // Retrieve the state object and the handler socket
                // from the asynchronous state object.
                StateObject state = (StateObject)ar.AsyncState;
                Socket handler = state.workSocket;
                // Read data from the client socket.
                int bytesRead = handler.EndReceive(ar);
                if (bytesRead > 0)
                {
                    // There might be more data, so store the data received so far.
                    var bytes = new byte[bytesRead];
                    Array.Copy(state.buffer, 0, state.data, state.dataRecviedLen, bytesRead);

                    state.dataRecviedLen += bytesRead;
                    if (state.dataRecviedLen < state.dataLen)
                    {
                        _socket.BeginReceive(state.buffer, 0, StateObject.BufferSize, SocketFlags.None, new AsyncCallback(ReadBodyCallback), state);
                    }
                    else
                    {
                        try
                        {
                            HandleRecMsg?.BeginInvoke(state.data, this, _server, null, null);
                        }
                        catch (Exception ex)
                        {
                            HandleException?.Invoke(ex);
                        }
                        if (_isRec && IsSocketConnected())
                            StartRecMsg();
                    }
                }
            }
            catch (Exception ex)
            {
                HandleException?.BeginInvoke(ex, null, null);
                Close();
            }
        }

        /// <summary>
        /// 发送数据
        /// </summary>
        /// <param name="bytes">数据字节</param>
        private void Send(byte[] bytes)
        {
            try
            {
                _socket.BeginSend(bytes, 0, bytes.Length, SocketFlags.None, asyncResult =>
                {
                    try
                    {
                        int length = _socket.EndSend(asyncResult);
                        HandleSendMsg?.BeginInvoke(bytes, this, _server, null, null);
                    }
                    catch (Exception ex)
                    {
                        HandleException?.BeginInvoke(ex, null, null);
                    }
                }, null);
            }
            catch (Exception ex)
            {
                HandleException?.BeginInvoke(ex, null, null);
            }
        }

        /// <summary>
        /// 发送字符串（默认使用UTF-8编码）
        /// </summary>
        /// <param name="msgStr">字符串</param>
        private void Send(string msgStr)
        {
            Send(Encoding.UTF8.GetBytes(msgStr));
        }

        /// <summary>
        /// 发送字符串（使用自定义编码）
        /// </summary>
        /// <param name="msgStr">字符串消息</param>
        /// <param name="encoding">使用的编码</param>
        private void Send(string msgStr, Encoding encoding)
        {
            Send(encoding.GetBytes(msgStr));
        }

        /// <summary>
        /// 发送字符串-头包括长度（默认使用UTF-8编码）
        /// </summary>
        /// <param name="msgStr"></param>
        public void SendData(string msgStr)
        {
            if (string.IsNullOrEmpty(msgStr)) return;
            var len = msgStr.Length;
            if (len > 0)
            {
                var bytes = Encoding.UTF8.GetBytes(msgStr);
                SendData(bytes);
            }
        }

        /// <summary>
        /// 发送数据 头包括一个长度信息
        /// </summary>
        /// <param name="bytes"></param>
        public void SendData(byte[] bytes)
        {
            if (bytes == null) return;
            var len = bytes.Length;
            if (len > 0)
            {
                Send(BitConverter.GetBytes(len));
                Send(bytes);
            }
        }

        /// <summary>
        /// 传入自定义属性
        /// </summary>
        public object Property { get; set; }

        /// <summary>
        /// 关闭当前连接
        /// </summary>
        public void Close()
        {
            try
            {
                _isRec = false;
                _socket.Disconnect(false);
                _server.RemoveConnection(this);
                HandleClientClose?.BeginInvoke(this, _server, null, null);
            }
            catch (Exception ex)
            {
                HandleException?.BeginInvoke(ex, null, null);
            }
            finally
            {
                _socket.Dispose();
                GC.Collect();
            }
        }

        #endregion 外部接口

        #region 事件处理

        /// <summary>
        /// 客户端连接接受新的消息后调用
        /// </summary>
        public Action<byte[], SocketConnection, SocketServer> HandleRecMsg { get; set; }

        /// <summary>
        /// 客户端连接发送消息后回调
        /// </summary>
        public Action<byte[], SocketConnection, SocketServer> HandleSendMsg { get; set; }

        /// <summary>
        /// 客户端连接关闭后回调
        /// </summary>
        public Action<SocketConnection, SocketServer> HandleClientClose { get; set; }

        /// <summary>
        /// 异常处理程序
        /// </summary>
        public Action<Exception> HandleException { get; set; }

        #endregion 事件处理
    }
}