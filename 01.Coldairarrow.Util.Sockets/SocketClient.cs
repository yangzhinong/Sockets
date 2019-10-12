using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Coldairarrow.Util.Sockets
{
    /// <summary>
    /// Socket客户端
    /// </summary>
    public class SocketClient
    {
        private readonly ManualResetEvent readLenDone = new ManualResetEvent(false);
        private bool isConnectToServer = false;

        /// <summary>
        /// 是否已连接到服务器， 并且可以发送数据
        /// </summary>
        public bool IsConnectToServer { get => isConnectToServer && IsSocketConnected(); }

        #region 构造函数

        /// <summary>
        /// 构造函数,连接服务器IP地址默认为本机127.0.0.1
        /// </summary>
        /// <param name="port">监听的端口</param>
        public SocketClient(int port)
        {
            _ip = "127.0.0.1";
            _port = port;
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="ip">监听的IP地址</param>
        /// <param name="port">监听的端口</param>
        public SocketClient(string ip, int port)
        {
            _ip = ip;
            _port = port;
        }

        #endregion 构造函数

        #region 内部成员

        private Socket _socket = null;
        private readonly string _ip = "";
        private readonly int _port = 0;
        private bool _isRec = true;

        private bool IsSocketConnected()
        {
            bool part1 = _socket.Poll(1000, SelectMode.SelectRead);
            bool part2 = (_socket.Available == 0);
            if (part1 && part2)
                return false;
            else
                return true;
        }

        ///// <summary>
        ///// 开始接受客户端消息
        ///// </summary>
        //public void StartRecMsg()
        //{
        //    try
        //    {
        //        byte[] container = new byte[1024 * 1024 * 2];
        //        _socket.BeginReceive(container, 0, container.Length, SocketFlags.None, asyncResult =>
        //        {
        //            try
        //            {
        //                int length = _socket.EndReceive(asyncResult);

        //                //马上进行下一轮接受，增加吞吐量
        //                if (length > 0 && _isRec && IsSocketConnected())
        //                    StartRecMsg();

        //                if (length > 0)
        //                {
        //                    byte[] recBytes = new byte[length];
        //                    Array.Copy(container, 0, recBytes, 0, length);

        //                    //处理消息
        //                    HandleRecMsg?.BeginInvoke(recBytes, this,null,null);
        //                }
        //                else
        //                    Close();
        //            }
        //            catch (Exception ex)
        //            {
        //                HandleException?.BeginInvoke(ex,null,null);
        //                Close();
        //            }
        //        }, null);
        //    }
        //    catch (Exception ex)
        //    {
        //        HandleException?.BeginInvoke(ex,null,null);
        //        Close();
        //    }
        //}

        /// <summary>
        /// 开始接受客户端消息
        /// </summary>
        public void StartRecMsg()
        {
            try
            {
                StateObject state = new StateObject
                {
                    workSocket = _socket
                };
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
                    var bodyLen = BitConverter.ToInt32(state.headBuffer, 0);
                    state.dataLen = bodyLen;
                    if (bodyLen > 0)
                    {
                        state.data = new byte[bodyLen];
                    }
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
                    if (state.dataRecviedLen < state.data.Length)
                    {
                        _socket.BeginReceive(state.buffer, 0, StateObject.BufferSize, SocketFlags.None, new AsyncCallback(ReadBodyCallback), state);
                    }
                    else
                    {
                        try
                        {
                            HandleRecMsg?.BeginInvoke(state.data, this, null, null);
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

        #endregion 内部成员

        #region 外部接口

        /// <summary>
        /// 开始服务，连接服务端
        /// </summary>
        public void StartClient()
        {
            try
            {
                //实例化 套接字 （ip4寻址协议，流式传输，TCP协议）
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                //创建 ip对象
                IPAddress address = IPAddress.Parse(_ip);
                //创建网络节点对象 包含 ip和port
                IPEndPoint endpoint = new IPEndPoint(address, _port);
                //将 监听套接字  绑定到 对应的IP和端口
                _socket.BeginConnect(endpoint, asyncResult =>
                {
                    try
                    {
                        _socket.EndConnect(asyncResult);
                        isConnectToServer = true;
                        //开始接受服务器消息
                        StartRecMsg();

                        HandleClientStarted?.BeginInvoke(this, null, null);
                    }
                    catch (Exception ex)
                    {
                        HandleException?.BeginInvoke(ex, null, null);
                    }
                }, null);
            }
            catch (Exception ex)
            {
                isConnectToServer = false;
                HandleException?.BeginInvoke(ex, null, null);
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
                        HandleSendMsg?.BeginInvoke(bytes, this, null, null);
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
        /// 发送字符串（使用自定义编码）
        /// </summary>
        /// <param name="msgStr">字符串消息</param>
        /// <param name="encoding">使用的编码</param>
        private void Send(string msgStr, Encoding encoding)
        {
            Send(encoding.GetBytes(msgStr));
        }

        /// <summary>
        /// 传入自定义属性
        /// </summary>
        public object Property { get; set; }

        /// <summary>
        /// 关闭与服务器的连接
        /// </summary>
        public void Close()
        {
            try
            {
                _isRec = false;
                _socket.Disconnect(false);
                HandleClientClose?.BeginInvoke(this, null, null);
            }
            catch (Exception ex)
            {
                HandleException?.BeginInvoke(ex, null, null);
            }
            finally
            {
                isConnectToServer = false;
                _socket.Dispose();
                GC.Collect();
            }
        }

        #endregion 外部接口

        #region 事件处理

        /// <summary>
        /// 客户端连接建立后回调
        /// </summary>
        public Action<SocketClient> HandleClientStarted { get; set; }

        /// <summary>
        /// 处理接受消息的委托
        /// </summary>
        public Action<byte[], SocketClient> HandleRecMsg { get; set; }

        /// <summary>
        /// 客户端连接发送消息后回调
        /// </summary>
        public Action<byte[], SocketClient> HandleSendMsg { get; set; }

        /// <summary>
        /// 客户端连接关闭后回调
        /// </summary>
        public Action<SocketClient> HandleClientClose { get; set; }

        /// <summary>
        /// 异常处理程序
        /// </summary>
        public Action<Exception> HandleException { get; set; }

        #endregion 事件处理
    }
}