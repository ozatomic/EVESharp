using System;
using Common.Logging;
using Common.Network;
using Common.Packets;
using PythonTypes.Types.Network;
using PythonTypes.Types.Primitives;

namespace Node.Network
{
    public class ClusterConnection
    {
        private Channel Log { get; }
        public EVEClientSocket Socket { get; }
        public NodeContainer Container { get; }

        public ClusterConnection(NodeContainer container)
        {
            this.Log = container.Logger.CreateLogChannel("ClusterConnection");
            this.Container = container;
            this.Socket = new EVEClientSocket(this.Log);
            this.Socket.SetReceiveCallback(ReceiveLowLevelVersionExchangeCallback);
            this.Socket.SetExceptionHandler(HandleException);
        }

        private void HandleException(Exception ex)
        {
            Log.Error("Exception detected: ");

            do
            {
                Log.Error(ex.Message);
                Log.Trace(ex.StackTrace);
            } while ((ex = ex.InnerException) != null);
        }

        private void ReceiveLowLevelVersionExchangeCallback(PyDataType ar)
        {
            try
            {
                LowLevelVersionExchange exchange = this.CheckLowLevelVersionExchange(ar);

                // reply with the node LowLevelVersionExchange
                LowLevelVersionExchange reply = new LowLevelVersionExchange();

                reply.codename = Common.Constants.Game.codename;
                reply.birthday = Common.Constants.Game.birthday;
                reply.build = Common.Constants.Game.build;
                reply.machoVersion = Common.Constants.Game.machoVersion;
                reply.version = Common.Constants.Game.version;
                reply.region = Common.Constants.Game.region;
                reply.isNode = true;
                reply.nodeIdentifier = "Node";

                this.Socket.Send(reply);

                // set the new handler
                this.Socket.SetReceiveCallback(ReceiveNodeInfoCallback);
            }
            catch (Exception e)
            {
                Log.Error($"Exception caught on LowLevelVersionExchange: {e.Message}");
                throw;
            }
        }

        private void ReceiveNodeInfoCallback(PyDataType ar)
        {
            if (ar is PyObjectData == false)
                throw new Exception($"Expected PyObjectData for machoNet.nodeInfo but got {ar.GetType()}");

            PyObjectData info = ar as PyObjectData;

            if (info.Name != "machoNet.nodeInfo")
                throw new Exception($"Expected PyObjectData of type machoNet.nodeInfo but got {info.Name}");

            // Update our local info
            NodeInfo nodeinfo = info;

            this.Container.NodeID = nodeinfo.nodeID;

            Log.Debug("Found machoNet.nodeInfo, our new node id is " + nodeinfo.nodeID.ToString("X4"));

            // load the specified solar systems
            this.Container.SystemManager.LoadSolarSystems(nodeinfo.solarSystems);

            // finally set the new packet handler
            this.Socket.SetReceiveCallback(ReceiveNormalPacketCallback);
        }

        private void ReceiveNormalPacketCallback(PyDataType ar)
        {
            PyPacket packet = ar;

            if (packet.Type == PyPacket.PacketType.CALL_REQ)
            {
                PyPacket result;
                PyTuple callInfo = ((packet.Payload[0] as PyTuple)[1] as PySubStream).Stream as PyTuple;

                string call = callInfo[1] as PyString;
                PyTuple args = callInfo[2] as PyTuple;
                PyDictionary sub = callInfo[3] as PyDictionary;
                PyDataType callResult = null;
                PyAddressClient source = packet.Source as PyAddressClient;

                try
                {
                    if (packet.Destination is PyAddressAny destAny)
                    {
                        Log.Trace($"Calling {destAny.Service.Value}::{call}");
                        callResult = this.Container.ServiceManager.ServiceCall(
                            destAny.Service, call, args, sub, this.Container.ClientManager.Get(packet.UserID)
                        );
                    }
                    else if (packet.Destination is PyAddressNode destNode)
                    {
                        Log.Trace($"Calling {destNode.Service.Value}::{call}");
                        callResult = this.Container.ServiceManager.ServiceCall(
                            destNode.Service, call, args, sub, this.Container.ClientManager.Get(packet.UserID)
                        );
                    }
                    else
                    {
                        Log.Error($"Received packet that wasn't directed to us");
                    }
                    
                    result = new PyPacket(PyPacket.PacketType.CALL_RSP);

                    // ensure destination has clientID in it
                    source.ClientID = (int) packet.UserID;
                    // switch source and dest
                    result.Source = packet.Destination;
                    result.Destination = source;

                    result.UserID = packet.UserID;

                    result.Payload = new PyTuple(new PyDataType[] {new PySubStream(callResult)});
                }
                catch (PyException e)
                {
                    // PyExceptions have to be relayed to the client
                    // this way it's easy to notify the user without needing any special code
                    result = new PyPacket(PyPacket.PacketType.ERRORRESPONSE);

                    source.ClientID = (int) packet.UserID;
                    result.Source = packet.Destination;
                    result.Destination = source;

                    result.UserID = packet.UserID;
                    result.Payload = new PyTuple(new PyDataType[]
                    {
                        (int) packet.Type, (int) MachoErrorType.WrappedException, new PyTuple (new PyDataType[] { new PySubStream(e) }), 
                    });
                }

                // any of the paths that lead to the send here must have the PyPacket initialized
                // so a null check is not needed
                this.Socket.Send(result);
            }
            else if (packet.Type == PyPacket.PacketType.SESSIONCHANGENOTIFICATION)
            {
                Log.Debug($"Updating session for client {packet.UserID}");

                // ensure the client is registered in the node and store his session
                if (this.Container.ClientManager.Contains(packet.UserID) == false)
                    this.Container.ClientManager.Add(packet.UserID, new Client());

                this.Container.ClientManager.Get(packet.UserID).UpdateSession(packet);
            }
        }

        protected LowLevelVersionExchange CheckLowLevelVersionExchange(PyDataType exchange)
        {
            LowLevelVersionExchange data = exchange;

            if (data.birthday != Common.Constants.Game.birthday)
                throw new Exception("Wrong birthday in LowLevelVersionExchange");

            if (data.build != Common.Constants.Game.build)
                throw new Exception("Wrong build in LowLevelVersionExchange");

            if (data.codename != Common.Constants.Game.codename + "@" + Common.Constants.Game.region)
                throw new Exception("Wrong codename in LowLevelVersionExchange");

            if (data.machoVersion != Common.Constants.Game.machoVersion)
                throw new Exception("Wrong machoVersion in LowLevelVersionExchange");

            if (data.version != Common.Constants.Game.version)
                throw new Exception("Wrong version in LowLevelVersionExchange");

            if (data.isNode == true)
            {
                if (data.nodeIdentifier != "Node")
                    throw new Exception("Wrong node string in LowLevelVersionExchange");
            }
            

            return data;
        }
    }
}