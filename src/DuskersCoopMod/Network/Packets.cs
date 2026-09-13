using System;
using UnityEngine;

namespace DuskersCoopMod.Network
{
    [Serializable]
    public class PacketWrapper
    {
        public string type;
        public string sender;
        public string data;

        public static string Create<T>(string type, string sender, T dataObj)
        {
            PacketWrapper wrapper = new PacketWrapper
            {
                type = type,
                sender = sender,
                data = JsonUtility.ToJson(dataObj)
            };
            return JsonUtility.ToJson(wrapper);
        }

        public static PacketWrapper FromJson(string json)
        {
            try
            {
                return JsonUtility.FromJson<PacketWrapper>(json);
            }
            catch
            {
                return null;
            }
        }

        public T GetData<T>()
        {
            try
            {
                return JsonUtility.FromJson<T>(data);
            }
            catch
            {
                return default(T);
            }
        }
    }

    [Serializable]
    public class CommandData
    {
        public string command;
    }

    [Serializable]
    public class ConsoleTextData
    {
        public string text;
        public int messageType;
        public int messageFormat;
    }

    [Serializable]
    public class HandshakeData
    {
        public string playerName;
        public string version;
    }

    [Serializable]
    public class ChatData
    {
        public string message;
    }

    [Serializable]
    public class SaveSyncData
    {
        public string compressedBase64;
    }

    [Serializable]
    public class StrategicActionData
    {
        public string action;
        public string targetName;
        public string dungeonGroup;
        public int dungeonSeed;
    }

    [Serializable]
    public class GalaxyStateData
    {
        public int galaxyInternalId;
        public string galaxyName;
        public int starSystemId;
        public string starSystemName;
        public string dockedDungeonName;
        public int scrap;
        public int propulsionFuel;
        public int jumpFuel;
        public int mapState;
    }

    [Serializable]
    public class DroneSyncItem
    {
        public int droneNumber;
        public float x;
        public float y;
        public float z;
        public float rotZ;
        public float rotY;
        public float hp;
        public bool isDead;
    }

    [Serializable]
    public class DronesSyncPacket
    {
        public System.Collections.Generic.List<DroneSyncItem> drones = new System.Collections.Generic.List<DroneSyncItem>();
    }

    [Serializable]
    public class ClientDroneSyncPacket
    {
        public int droneNumber;
        public float x;
        public float y;
        public float z;
        public float rotZ;
        public float rotY;
    }

    [Serializable]
    public class DoorSyncItem
    {
        public string label;
        public bool isOpen;
    }

    [Serializable]
    public class DoorsSyncPacket
    {
        public System.Collections.Generic.List<DoorSyncItem> doors = new System.Collections.Generic.List<DoorSyncItem>();
    }
}
