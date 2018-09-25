using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.IO;

namespace UDPTran
{
    class Program
    {
        static void Main(string[] args)
        {
            Dispatcher dispatcher = new Dispatcher("192.168.253.1", 8090);

            
            FileStream fs = new FileStream(@"F:\f1.pdf", FileMode.Open);
            StreamReader sr = new StreamReader(fs);
            StringBuilder sb = new StringBuilder();
            sb.Append(sr.ReadToEnd());
            byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
        }
    }
}
