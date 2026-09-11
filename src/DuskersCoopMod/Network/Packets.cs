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
}
