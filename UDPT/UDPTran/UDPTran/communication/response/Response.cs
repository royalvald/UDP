﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;

namespace UDPTran
{
    public enum ResponseType { PreTagResponse,SendComplete};
    public enum ResponseStatus { OK, Failed };
    class Response : TranTag
    {
        PacketUtil packetUtil = new PacketUtil();


        private void ResponseTag(EndPoint myEndPoint, EndPoint endPoint, bool status)
        {
            PreTagResponse tagResponse;

            if (status)
            {
                tagResponse = new PreTagResponse(myEndPoint, ResponseStatus.OK);

            }
            else
            {
                tagResponse = new PreTagResponse(myEndPoint, ResponseStatus.Failed);
            }

            byte[] bytes = packetUtil.ObjectToBytes((object)tagResponse);
            socket.SendTo(bytes, bytes.Length, SocketFlags.None, endPoint);
        }
    }
}
