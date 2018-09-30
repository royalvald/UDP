using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UDPTran;

namespace UDPTran
{
    /// <summary>
    /// 基础沟通组件
    /// </summary>
    class Dispatcher
    {
        //ip地址
        private IPEndPoint hostIPEndPoint;
       
        //通信socket
        private Socket socket;
        private Socket socket1;
        //发送缓冲区
        private Dictionary<int, DataPool> sendOutPool;
        //接收缓冲区
        private Dictionary<int, DataPool> ReceivePool;
        //重发请求缓冲区
        private Dictionary<int, DataPool> ResendPool = new Dictionary<int, DataPool>();
        //IP地址设定
        private IPEndPoint RemoteIPEndPoint;
        //private IPEndPoint RemotePoint;
        private Thread ServiceStart;

        public Dispatcher(string IP, int port)
        {
            //自身IP初始化
            IPAddress selfAddress = IPAddress.Parse("192.168.109.35");
            hostIPEndPoint = new IPEndPoint(selfAddress, 8090);


            //初始化接受IP
            IPAddress iPAddress = IPAddress.Parse(IP);
            RemoteIPEndPoint = new IPEndPoint(iPAddress, port);

            //接收池与发送池初始化
            sendOutPool = new Dictionary<int, DataPool>();
            ReceivePool = new Dictionary<int, DataPool>();

            //服务启动
            ServiceStart = new Thread(Service);
            ServiceStart.Start();

            //丢包检查系统启动
            Thread checkThread = new Thread(CheckLostPack);
            checkThread.Start();
        }

        //for test
        public Dispatcher(string IP, int port, string words)
        {
            IPAddress selfAddress = IPAddress.Parse("192.168.152.32");
            hostIPEndPoint = new IPEndPoint(selfAddress, 8090);


            //初始化接受IP
            IPAddress iPAddress = IPAddress.Parse(IP);
            RemoteIPEndPoint = new IPEndPoint(iPAddress, port);
            //接收池与发送池初始化
            sendOutPool = new Dictionary<int, DataPool>();
            ReceivePool = new Dictionary<int, DataPool>();

            ServiceStart = new Thread(ServiceTest);

            ServiceStart.Start();
        }

        //服务开始
        private void Service(object objects)
        {
            //初始化socket
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(hostIPEndPoint);

            socket1 = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket1.Bind(new IPEndPoint(hostIPEndPoint.Address,8080));
            //设置监听的端口号
            IPEndPoint ReceiveEndPoint = new IPEndPoint(IPAddress.Any, 0);
            EndPoint AbReceiveEndPoint = (EndPoint)ReceiveEndPoint;

            //存储一些关于数据包的信息
            byte[] TempInfo = new byte[2048];

            Thread PackProcess;
            int dataSize;
            object ReceiveTempData;


            while (true)
            {
                dataSize = socket.ReceiveFrom(TempInfo, ref AbReceiveEndPoint);
                byte[] infoByte = new byte[2048];
                TempInfo.CopyTo(infoByte, 0);
                if (dataSize == 2048)
                {
                    ReceiveTempData = new ReceiveData(infoByte, AbReceiveEndPoint);
                    PackProcess = new Thread(PacketProcess);
                    PackProcess.Start(ReceiveTempData);
                }
                else
                {
                    ReceiveTempData = new ReceiveData(infoByte, AbReceiveEndPoint);
                    PackProcess = new Thread(ProcessRequest);
                    PackProcess.Start(ReceiveTempData);
                }
            }
        }
        //测试服务，主要是测试数据基本传输
        private void ServiceTest()
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(hostIPEndPoint);

            //设置监听的端口号
            IPEndPoint ReceiveEndPoint = new IPEndPoint(IPAddress.Any, 0);
            EndPoint AbReceiveEndPoint = (EndPoint)ReceiveEndPoint;

            byte[] TempInfo = new byte[2048];
            Thread PackProcess;
            int dataSize;
            object ReceiveTempData;


            while (true)
            {
                dataSize = socket.ReceiveFrom(TempInfo, ref AbReceiveEndPoint);

                Console.WriteLine(Encoding.UTF8.GetString(TempInfo) + AbReceiveEndPoint.ToString());
            }

        }
        //收到的网络数据包分为两种，一种是包含文件信息数据，另外一种就是包含请求的，所以应该分开处理
        /// <summary>
        /// 受到数据包处理方式
        /// </summary>
        /// <param name="TempData"></param>
        private void PacketProcess(object TempData)
        {


            int PackID;
            Dictionary<int, int> PackCheck;
            DataPool dataPool;


            //获取包装类中的数据
            byte[] bytes = ((ReceiveData)TempData).bytes;
            EndPoint endPoint = ((ReceiveData)TempData).endPoint;


            //调用工具类处理数据包数据
            PacketUtil packetUtil = new PacketUtil();
            //获取数据包的ID,用作分类,获取数据池
            PackID = packetUtil.GetID(bytes);


            //先判断总数据池中有没有相关ID的数据池
            lock (this)
            {
                if (ReceivePool.ContainsKey(PackID))
                {
                    dataPool = ReceivePool[PackID];

                    dataPool.AddBytes(bytes);
                    dataPool.CountPlus();
                    dataPool.RefreshTime();
                }
                else
                {
                    ReceivePool.Add(PackID, new DataPool(PackID, new Dictionary<int, byte[]>(), endPoint, 30000));
                    dataPool = ReceivePool[PackID];
                    dataPool.AddBytes(bytes);
                    dataPool.CountPlus();
                    dataPool.RefreshTime();
                }
            }




            //检测是否可以进行拼包操作

            if (dataPool.Count == dataPool.TotalCount)
            {
                if (packetUtil.TotalCheckBool(dataPool.dic))
                {
                   
                    
                    FileStream f1 = File.Create(@"F:test.txt");

                    
                    int count = packetUtil.GetCount(dataPool.dic[0]);
                    int contextLength = packetUtil.GetContexLength(dataPool.dic[0]);
                    int i = 0;
                    while(i<count-1)
                    {
                        f1.Write(dataPool.dic[i], 8, 2040);
                        i++;
                    }
                    f1.Write(dataPool.dic[count-1],8,contextLength);
                    
                    f1.Close();
                    Console.WriteLine("finshed");


                    //完成后删除相关接收缓冲池
                    ReceivePool.Remove(PackID);
                }
                else
                {
                    PackCheck = packetUtil.TotalCheck(dataPool.dic);
                    ProcessLostPacket(PackCheck, dataPool.endPoint);
                }

            }





        }


        //收到请求包的处理方式
        private void ProcessRequest(object objects)
        {
            Console.WriteLine("pack lost processing");
            //获取数据
            byte[] bytes = ((ReceiveData)objects).bytes;
            EndPoint tempEndPoint = ((ReceiveData)objects).endPoint;
            IPEndPoint tempIPEndPoint = (IPEndPoint)tempEndPoint;
            IPEndPoint tran = new IPEndPoint(tempIPEndPoint.Address, 8090);
            //反序列化
            //Request request = (Request)(BytesToObject(bytes));
            /*if (request.requestType == RequestType.PreTag)
            {
                //PreTag preTag = (PreTag)request;
                ProcessPreTag(objects);

            }
            else if (request.requestType == RequestType.ReSend)
            {
                //ReSend reSend = (ReSend)request;
                ProcessResend(objects);
            }*/
            //ProcessResend(objects);
            processReByte(bytes, tran);
        }

        /// <summary>
        /// 受到重发请求数据包处理方法
        /// </summary>
        /// <param name="TempData"></param>
        private void ProcessResend(object TempData)
        {
            //标记大数据包的ID和包内index
            int packID, Index;

            //获取包装类中的信息
            byte[] TempBytes = ((ReceiveData)TempData).bytes;
            EndPoint endPoint = ((ReceiveData)TempData).endPoint;

            //调用工具类获取信息
            PacketUtil packetUtil = new PacketUtil();
            packID = packetUtil.GetID(TempBytes);
            Index = packetUtil.GetIndex(TempBytes);

            //添加进缓冲池准备发送
            ResendTo(packID, Index, endPoint);

        }


        private void processReByte(byte[] bytes,EndPoint endPoint)
        {
            PacketUtil packetUtil = new PacketUtil();
            int id = packetUtil.GetID(bytes);
            int index = packetUtil.GetIndex(bytes);
            byte[] infoBytes = sendOutPool[id].dic[index];
            socket1.SendTo(infoBytes, endPoint);
        }

        /// <summary>
        /// 处理接收到的重传请求
        /// </summary>
        /// <param name="ID"></param>
        /// <param name="Index"></param>
        /// <param name="RemoteEndPoint"></param>
        private void ResendTo(int ID, int Index, EndPoint RemoteEndPoint)
        {
            byte[] bytes;
            if (sendOutPool.ContainsKey(ID))
            {
                bytes = sendOutPool[ID].dic[Index];
                socket.SendTo(bytes, bytes.Length, SocketFlags.None, RemoteIPEndPoint);
            }
            else
            {

            }
        }


        private void ProcessPreTag(object objects)
        {
            byte[] bytes = ((ReceiveData)objects).bytes;
            EndPoint endPoint = ((ReceiveData)objects).endPoint;

            PreTag preTag = (PreTag)(BytesToObject(bytes));



            ReceivePool.Add(preTag.ID, new DataPool(preTag.ID, new Dictionary<int, byte[]>(), preTag.MyEndPoint));
            ResponseTag(endPoint, true);
        }



        //发送二进制数据
        public void InfoSend(byte[] Info)
        {
            //首先调用工具类对数据进行分片,然后接受返回的list
            PacketUtil packetUtil = new PacketUtil();
            List<byte[]> InfoList = packetUtil.InfoToPacket(Info);

            //先发送头部信息
            //SendTag(InfoList[0], RemoteIPEndPoint);
            //获取大包ID放入发送缓冲池
            int ID = packetUtil.GetID(InfoList[0]);

            DataPool dataPool = new DataPool(ID, new Dictionary<int, byte[]>(), (EndPoint)RemoteIPEndPoint);

            dataPool.AddList(InfoList);
            sendOutPool.Add(ID, dataPool);

            //发送数据
            Send(dataPool, (EndPoint)RemoteIPEndPoint);
        }





        /// <summary>
        /// 填入相关信息即可放入到重发缓冲池
        /// </summary>
        /// <param name="packID"></param>
        /// <param name="index"></param>
        /// <param name="endPoint"></param>
        private void ResendProcess(int packID, int index, EndPoint endPoint)

        {
            //重发请求只需要包含大包的ID和大包内的小包的索引
            byte[] InfoBytes = new byte[4];
            byte[] bytes = new byte[2];

            //对写入的ID和Index进行特殊处理
            Int16 PID = (short)packID;
            Int16 Index = (short)index;

            //写入ID
            bytes = BitConverter.GetBytes(PID);
            Array.Copy(bytes, 0, InfoBytes, 0, 2);

            //写入Index
            bytes = BitConverter.GetBytes(Index);
            Array.Copy(bytes, 0, InfoBytes, 2, 2);

            //DataPool dataPool;

            //存入请求缓冲池
            /*
            if (ResendPool.ContainsKey(packID))
            {
                dataPool = ResendPool[packID];
                dataPool.AddBytes(InfoBytes);
            }
            else
            {
                dataPool = new DataPool(packID, new Dictionary<int, byte[]>(), endPoint);
                dataPool.AddBytes(InfoBytes);

                ResendPool.Add(packID, dataPool);

            }*/
            socket1.SendTo(InfoBytes, endPoint);
            

        }


        private void DataPoolResend(DataPool dataPool)
        {
            
        }



        /// <summary>
        /// 发送头部信息
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="endPoint"></param>
        private void SendTag(byte[] bytes, EndPoint endPoint)
        {
            int ID;
            PacketUtil packetUtil = new PacketUtil();
            ID = packetUtil.GetID(bytes);
            PreTag preTag = new PreTag(RemoteIPEndPoint, endPoint, ID, RequestType.PreTag);
            byte[] TempBytes = ObjectToBytes((object)preTag);
            socket.SendTo(TempBytes, TempBytes.Length, SocketFlags.None, endPoint);
        }

        /// <summary>
        /// 类转化为字节组数
        /// </summary>
        /// <param name="objects"></param>
        /// <returns></returns>
        private byte[] ObjectToBytes(object objects)
        {
            var memoryStream = new MemoryStream();
            var formalTer = new BinaryFormatter();
            formalTer.Serialize(memoryStream, formalTer);
            byte[] bytes = memoryStream.ToArray();
            memoryStream.Close();

            return bytes;

        }

        /// <summary>
        /// 字节数组转化为类
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        private object BytesToObject(byte[] bytes)
        {
            var memoryStream = new MemoryStream(bytes);
            var formalTer = new BinaryFormatter();
            var objects = formalTer.Deserialize(memoryStream);
            memoryStream.Close();

            return objects;
        }


        //发送单个信息的方法
        private void Send(DataPool dataPool, EndPoint endPoint)
        {
            IPEndPoint RemoteIPEndPoint = (IPEndPoint)endPoint;
            foreach (var item in dataPool.dic)
            {
                socket.SendTo(item.Value, item.Value.Length, SocketFlags.None, endPoint);
                Thread.Sleep(1);
            }

        }

        private void Send(byte[] bytes, EndPoint endPoint)
        {
            IPEndPoint RemoteIPEndPoint = (IPEndPoint)endPoint;
            socket.SendTo(bytes, bytes.Length, SocketFlags.None, endPoint);
        }

        /// <summary>
        /// 发送整个缓冲池的数据
        /// </summary>
        /// <param name="dic"></param>
        /// <param name="endPoint"></param>
        private void SendInfo(Dictionary<int, DataPool> dic)
        {
            foreach (var item in dic)
            {
                foreach (var Info in item.Value.dic)
                {
                    socket.SendTo(Info.Value, Info.Value.Length, SocketFlags.None, item.Value.endPoint);
                    //Thread.Sleep(1);
                }
            }
        }

        /// <summary>
        /// 检查单个数据大包是否完整
        /// </summary>
        /// <param name="dataPool"></param>
        /// <returns></returns>
        public bool CheckPackComplete(DataPool dataPool)
        {
            //设置标记
            bool IsCompleted;
            if (dataPool.Count == dataPool.TotalCount)
                return IsCompleted = true;
            else return IsCompleted = false;
        }


        /// <summary>
        /// 拼包时候检测缺少的数据包进行重传处理
        /// </summary>
        /// <param name="dic"></param>
        /// <param name="endPoint"></param>
        public void ProcessLostPacket(Dictionary<int, int> dic, EndPoint endPoint)
        {
            foreach (var item in dic)
            {
                ResendProcess(item.Value, item.Key, endPoint);
                Thread.Sleep(1);
            }
        }


        /// <summary>
        /// 丢包检查
        /// </summary>
        public void CheckLostPack()
        {
            while (true)
            {
                //一秒一次查询过程
                Thread.Sleep(1000);
                CheckProcess();
            }
        }

        public void CheckProcess()
        {
            PacketUtil packetUtil = new PacketUtil();
            foreach (var item in ReceivePool)
            {
                if (item.Value.leftTime < 0)
                {
                    ReceivePool.Remove(item.Key);
                }

                if (item.Value.leftTime < 27000)
                {
                    ProcessLostPacket(packetUtil.TotalCheck(item.Value.dic), item.Value.endPoint);
                }

                item.Value.leftTime -= 1000;
            }

            //发送整个重发缓冲池的数据
            SendInfo(ResendPool);
        }


        private void ResponseTag(EndPoint endPoint, bool status)
        {
            PreTagResponse tagResponse;

            if (status)
            {
                tagResponse = new PreTagResponse(hostIPEndPoint, ResponseStatus.OK);

            }
            else
            {
                tagResponse = new PreTagResponse(hostIPEndPoint, ResponseStatus.Failed);
            }

            byte[] bytes = ObjectToBytes((object)tagResponse);
            socket.SendTo(bytes, bytes.Length, SocketFlags.None, endPoint);
        }

    }
}

