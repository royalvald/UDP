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
using UDPTran.Save;

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
        //private Socket socket1;
        //发送缓冲区
        private Dictionary<int, DataPool> sendOutPool;
        //接收缓冲区
        private Dictionary<int, DataPool> ReceivePool;
        //重发请求缓冲区
        private Dictionary<int, ResendPool> ResendBufferPool = new Dictionary<int, ResendPool>();
        private Dictionary<int, List<int>> processBuffer = new Dictionary<int, List<int>>();
        //IP地址设定
        private IPEndPoint RemoteIPEndPoint;
        //private IPEndPoint RemotePoint;
        private Thread ServiceStart;

        private DataPool dataPool;
        private ResendPool pool;

        private PacketUtil packetUtil = new PacketUtil();
        public Dispatcher(string IP, int port)
        {           

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

        public void setHostIPEndPoint(string IP,int port)
        {
            //自身IP初始化
            IPAddress selfAddress = IPAddress.Parse(IP);
            hostIPEndPoint = new IPEndPoint(selfAddress, 8090);
        }


        //服务开始
        private void Service(object objects)
        {
            //初始化socket
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(hostIPEndPoint);

            //socket1 = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            //socket1.Bind(new IPEndPoint(hostIPEndPoint.Address, 8080));
            //socket1.Ttl=100;

            //设置监听的端口号
            IPEndPoint ReceiveEndPoint = new IPEndPoint(IPAddress.Any, 0);
            EndPoint AbReceiveEndPoint = (EndPoint)ReceiveEndPoint;

            //存储一些关于数据包的信息
            byte[] TempInfo = new byte[2052];

            Thread PackProcess;
            int dataSize;
            object ReceiveTempData;


            while (true)
            {

                dataSize = socket.ReceiveFrom(TempInfo, ref AbReceiveEndPoint);
                byte[] infoByte = new byte[2052];
                TempInfo.CopyTo(infoByte, 0);
                if (dataSize == 2052)
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

        //收到的网络数据包分为两种，一种是包含文件信息数据，另外一种就是包含请求的，所以应该分开处理
        /// <summary>
        /// 受到数据包处理方式
        /// </summary>
        /// <param name="TempData"></param>
        private void PacketProcess(object TempData)
        {


            int PackID;
            Dictionary<int, int> PackCheck;
            DataPool dataPool = null;


            //获取包装类中的数据
            byte[] bytes = ((ReceiveData)TempData).bytes;
            EndPoint endPoint = ((ReceiveData)TempData).endPoint;


            //调用工具类处理数据包数据
            // PacketUtil packetUtil = new PacketUtil();
            //获取数据包的ID,用作分类,获取数据池
            PackID = packetUtil.GetID(bytes);
            int index = packetUtil.GetIndex(bytes);




            //先判断总数据池中有没有相关ID的数据池
            lock (this)
            {

                if (ResendBufferPool.ContainsKey(PackID))
                {
                    if (processBuffer.ContainsKey(PackID))
                        processBuffer[PackID].Add(index);
                    else
                    {
                        processBuffer.Add(PackID, new List<int>());
                        processBuffer[PackID].Add(index);
                    }
                }
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


            //Console.WriteLine(dataPool.Count);

            //检测是否可以进行拼包操作

            if (dataPool.Count == dataPool.TotalCount)
            {
                if (packetUtil.TotalCheckBool(dataPool.dic))
                {


                    FileStream f1 = File.Create(@"H:\test.rar");


                    int count = packetUtil.GetCount(dataPool.dic[0]);
                    int contextLength = packetUtil.GetContexLength(dataPool.dic[0]);
                    int i = 0;
                    while (i < count - 1)
                    {
                        f1.Write(dataPool.dic[i], 12, 2040);
                        i++;
                        if (i % 100 == 0)
                        {
                            f1.Flush();
                        }
                    }
                    f1.Write(dataPool.dic[count - 1], 12, contextLength);

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
            //Console.WriteLine("pack lost processing");
            //获取数据
            byte[] bytes = ((ReceiveData)objects).bytes;
            EndPoint tempEndPoint = ((ReceiveData)objects).endPoint;
            IPEndPoint tempIPEndPoint = (IPEndPoint)tempEndPoint;
            IPEndPoint tran = new IPEndPoint(tempIPEndPoint.Address, 8090);

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


        private void processReByte(byte[] bytes, EndPoint endPoint)
        {
            PacketUtil packetUtil = new PacketUtil();
            int id = packetUtil.GetID(bytes);
            int index = packetUtil.GetIndex(bytes);
            byte[] infoBytes = sendOutPool[id].dic[index];

            Socket socket1 = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            //socket1.Bind(new IPEndPoint(hostIPEndPoint.Address, 8080));
            socket1.SendTo(infoBytes, infoBytes.Length, SocketFlags.None, endPoint);
            socket1.Dispose();
            Thread.Sleep(1);


            /*Console.WriteLine("已发送请求");
            Console.WriteLine(id);
            Console.WriteLine(index);*/
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


        /*private void ProcessPreTag(object objects)
        {
            byte[] bytes = ((ReceiveData)objects).bytes;
            EndPoint endPoint = ((ReceiveData)objects).endPoint;

            PreTag preTag = (PreTag)(BytesToObject(bytes));



            ReceivePool.Add(preTag.ID, new DataPool(preTag.ID, new Dictionary<int, byte[]>(), preTag.MyEndPoint));
            ResponseTag(endPoint, true);
        }
        */


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
            Console.WriteLine(dataPool.dic.Count);
            //发送数据
            Send(dataPool, (EndPoint)RemoteIPEndPoint);
        }





        /// <summary>
        /// 填入相关信息即可放入到重发缓冲池
        /// </summary>
        /// <param name="packID"></param>
        /// <param name="index"></param>
        /// <param name="endPoint"></param>
        private void ResendProcess(int packID, int index, EndPoint endPoint, Socket socket1)

        {
            //重发请求只需要包含大包的ID和大包内的小包的索引
            byte[] InfoBytes = new byte[6];
            byte[] bytes;

            //对写入的ID和Index进行特殊处理
            Int16 PID = (short)packID;


            //写入ID
            bytes = BitConverter.GetBytes(PID);
            Array.Copy(bytes, 0, InfoBytes, 0, 2);

            //写入Index
            bytes = BitConverter.GetBytes(index);
            Array.Copy(bytes, 0, InfoBytes, 2, 4);

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
            IPEndPoint iPEndPoint = (IPEndPoint)endPoint;
            EndPoint tran = (EndPoint)(new IPEndPoint(iPEndPoint.Address, 8090));
            socket1.SendTo(InfoBytes, tran);
        }











        //发送单个信息的方法
        private void Send(DataPool dataPool, EndPoint endPoint)
        {
            IPEndPoint RemoteIPEndPoint = (IPEndPoint)endPoint;
            Socket socket1 = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket1.Bind(new IPEndPoint(hostIPEndPoint.Address, 8080));
            int i = 0;
            foreach (var item in dataPool.dic)
            {
                socket1.SendTo(item.Value, item.Value.Length, SocketFlags.None, endPoint);
                //Thread.Sleep(1);
                if (i % 5 == 0)
                    Thread.Sleep(1);
                i++;
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
            Socket socket1 = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket1.Bind(new IPEndPoint(hostIPEndPoint.Address, 8080));
            int i = 0;
            foreach (var item in dic)
            {
                ResendProcess(item.Value, item.Key, endPoint, socket1);
                if(i%5==0)
                {
                    Thread.Sleep(1);
                }
                //Thread.Sleep(1);
            }
            socket1.Dispose();
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
                PreCheck();
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

                    ResendProcess(item.Value);
                    Console.WriteLine("lost processing");
                    /*foreach (var items in packetUtil.TotalCheck(item.Value.dic))
                    {
                        Console.WriteLine(items.Value);
                        Console.WriteLine(items.Key);
                    }*/

                }

                item.Value.leftTime -= 1000;
            }

            //发送整个重发缓冲池的数据
            //SendInfo(ResendPool);
        }

        private void PreCheck()
        {
            foreach (var item in ResendBufferPool.Keys)
            {
                List<int> list ;
                if(processBuffer.ContainsKey(item))
                {
                   
                    list = processBuffer[item].ToList();
                    foreach (var items in list)
                    {
                        ResendBufferPool[item].dic.Remove(items);
                    }
                }
            }
        }
        
        private void ResendProcess(DataPool dataPool)
        {
            int id = dataPool.IDNumber;
            ResendPool pool;
            if (ResendBufferPool.ContainsKey(id))
            {
                pool = ResendBufferPool[id];
                ProcessLostPacket(pool.dic, pool.RemotePoint);
            }
            else
            {
                pool = new ResendPool(this.hostIPEndPoint, dataPool.endPoint, packetUtil.TotalCheck(dataPool.dic), dataPool.IDNumber);
                ResendBufferPool.Add(dataPool.IDNumber, pool);
                ProcessLostPacket(pool.dic, pool.RemotePoint);
            }
        }

    }
}

