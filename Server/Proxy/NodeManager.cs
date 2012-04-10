﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Common;
using Common.Network;
using Common.Packets;
using Marshal;

namespace Proxy
{
    // Carefull, the proxy is the nodeID 1, so we should add/sub 2 to every nodeID
    public static class NodeManager
    {
        public enum NodeStatus
        {
            NodeAlive = 0,
            NodeUnderHighLoad = 1,
            NodeWaitingHeartbeat = 2,
        }

        public static Dictionary<int, Node> nodes = new Dictionary<int, Node>();
        private static Dictionary<int, NodeStatus> nodeStatus = new Dictionary<int, NodeStatus>();

        public static void AddNode(Node node)
        {
            Log.Debug("Node", "Node connected with id " + nodes.Count);

            // Send a notification with the nodeInfo
            NodeInfo info = new NodeInfo();

            info.nodeID = nodes.Count + 2;
            info.solarSystems.Items.Add(new PyNone()); // This will let the node load the solarSystems it wants

            node.Notify(info.Encode());

            nodes.Add(nodes.Count, node);
            nodeStatus.Add(nodes.Count, NodeStatus.NodeAlive);
        }

        public static int GetRandomNode()
        {
            bool found = false;
            int nodeID = 0;

            if (nodes.Count == 0)
            {
                return nodes.Count;
            }

            while (found == false)
            {
                Random rnd = new Random();
                nodeID = rnd.Next(nodes.Count);

                try
                {
                    if (nodeStatus[nodeID] == NodeStatus.NodeAlive)
                    {
                        found = true;
                    }
                }
                catch (Exception)
                {

                }
            }

            return nodeID + 2; // Add two because the proxy is nodeID 1, the first node should be nodeID 2
        }

        public static int GetNodeID(Node node)
        {
            int nodeID = 0;

            foreach (Node itnode in nodes.Values)
            {
                if (itnode == node)
                {
                    return nodeID + 2;
                }

                nodeID++;
            }

            return -1;
        }

        public static int RegisterNode(Node node)
        {
            int nodeID = nodes.Count;

            nodes.Add(nodeID, node);
            
            return nodeID + 2;
        }

        public static bool IsNodeUnderHighLoad(int nodeID)
        {
            return nodeStatus[nodeID - 2] == NodeStatus.NodeUnderHighLoad;
        }

        public static void HeartbeatNodes()
        {
            // Check if an older heartbeat left a node waiting
            foreach (int nodeID in nodeStatus.Keys)
            {
                if (nodeStatus[nodeID] == NodeStatus.NodeWaitingHeartbeat)
                {
                    // Node dead, load all the solarSystems into other node and transfer the clients
                    try
                    {
                        nodes.Remove(nodeID);
                    }
                    catch (Exception)
                    {

                    }
                }
            }

            PyTuple data = new PyTuple();

            data.Items.Add(new PyLongLong(DateTime.Now.ToFileTime()));

            NotifyNodes(new PyObjectData("machoNet.PingReq", data));
        }

        public static void NotifyNodes(PyObject notify)
        {
            foreach (int nodeID in nodes.Keys)
            {
                NotifyNode(nodeID + 2, notify);
            }
        }

        public static bool NotifyNode(int nodeID, PyObject data)
        {
            nodeID = nodeID - 2;
            try
            {
                nodes[nodeID].Notify(data);
                return true;
            }
            catch (Exception)
            {
                Log.Error("NodeManager", "Trying to send data to a non-existing node");
                return false;
            }
        }
    }
}
