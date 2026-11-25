using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using MQNetScan.Engine.Contracts;

namespace MQNetScan.Infrastructure
{
    /// <summary>尽力通过反向 DNS 与 NetBIOS 查询设备名；失败时返回空字符串。</summary>
    internal static class DeviceNameResolver
    {
        public const int NetBiosPort = 137;
        public const int NetBiosTimeoutMs = 350;
        public const int ReverseDnsTimeoutMs = 500;

        public static string Resolve(uint address)
        {
            try
            {
                IPAddress ip = NetworkAdapterService.UIntToIp(address);
                string reverse = ResolveReverseDns(ip, ReverseDnsTimeoutMs);
                if (reverse.Length > 0) { return reverse; }
                return QueryNetBiosName(ip, NetBiosTimeoutMs);
            }
            catch (Exception)
            {
                return "";
            }
        }

        internal static string ResolveReverseDns(IPAddress address, int timeoutMs)
        {
            if (address == null) { return ""; }
            IAsyncResult operation = null;
            try
            {
                operation = Dns.BeginGetHostEntry(address, null, null);
                if (!operation.AsyncWaitHandle.WaitOne(timeoutMs, false)) { return ""; }
                IPHostEntry entry = Dns.EndGetHostEntry(operation);
                if (entry == null) { return ""; }
                string name = Clean(entry.HostName);
                if (name.Length == 0 || string.Equals(name, address.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    return "";
                }
                return StripDomain(name);
            }
            catch (Exception) { return ""; }
            finally
            {
                if (operation != null && operation.AsyncWaitHandle != null) { operation.AsyncWaitHandle.Close(); }
            }
        }

        internal static string QueryNetBiosName(IPAddress address, int timeoutMs)
        {
            if (address == null || address.AddressFamily != AddressFamily.InterNetwork) { return ""; }
            try
            {
                byte[] request = BuildNodeStatusRequest();
                using (UdpClient client = new UdpClient(AddressFamily.InterNetwork))
                {
                    client.Client.ReceiveTimeout = timeoutMs;
                    client.Client.SendTimeout = timeoutMs;
                    client.Connect(address, NetBiosPort);
                    client.Send(request, request.Length);

                    IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    byte[] response = client.Receive(ref remote);
                    return ParseNodeStatusResponse(response);
                }
            }
            catch (Exception) { return ""; }
        }

        internal static byte[] BuildNodeStatusRequest()
        {
            byte[] packet = new byte[50];
            packet[0] = 0x11;
            packet[1] = 0x11;
            packet[2] = 0x00;
            packet[3] = 0x00;
            packet[4] = 0x00;
            packet[5] = 0x01;
            packet[12] = 0x20;
            packet[13] = (byte)'C';
            packet[14] = (byte)'K';
            for (int i = 1; i < 16; i++)
            {
                packet[13 + i * 2] = (byte)'C';
                packet[14 + i * 2] = (byte)'A';
            }
            packet[45] = 0x00;
            packet[46] = 0x00;
            packet[47] = 0x21;
            packet[48] = 0x00;
            packet[49] = 0x01;
            return packet;
        }

        internal static string ParseNodeStatusResponse(byte[] data)
        {
            if (data == null || data.Length < 12) { return ""; }
            int questionCount = ReadUInt16(data, 4);
            int answerCount = ReadUInt16(data, 6);
            if (questionCount != 0 || answerCount < 1) { return ""; }

            int offset = 12;
            int recordEnd = 0;
            for (int answer = 0; answer < answerCount; answer++)
            {
                if (data.Length < offset + 12) { return ""; }
                if ((data[offset] & 0xC0) == 0xC0) { offset += 2; }
                else
                {
                    while (offset < data.Length && data[offset] != 0)
                    {
                        int labelLength = data[offset++];
                        if ((labelLength & 0xC0) != 0 || labelLength > 63
                            || data.Length < offset + labelLength) { return ""; }
                        offset += labelLength;
                    }
                    if (offset >= data.Length) { return ""; }
                    offset++;
                }

                int type = ReadUInt16(data, offset); offset += 2;
                int rrClass = ReadUInt16(data, offset); offset += 2;
                offset += 4;
                int rdLength = ReadUInt16(data, offset); offset += 2;
                if (rdLength < 1 || data.Length < offset + rdLength) { return ""; }
                if (type == 0x21 && rrClass == 1)
                {
                    recordEnd = offset + rdLength;
                    break;
                }
                offset += rdLength;
            }
            if (recordEnd == 0) { return ""; }

            int nameCount = data[offset++];
            if (nameCount < 1 || nameCount > 32) { return ""; }
            for (int i = 0; i < nameCount; i++)
            {
                if (data.Length < offset + 18) { return ""; }
                string name = Encoding.ASCII.GetString(data, offset, 15).Trim();
                byte suffix = data[offset + 15];
                int flags = ReadUInt16(data, offset + 16);
                offset += 18;
                if ((flags & 0x8000) != 0 && suffix == 0x00 && name.Length > 0)
                {
                    return Clean(name);
                }
            }
            return "";
        }

        internal static string StripDomain(string hostName)
        {
            string name = Clean(hostName);
            int dot = name.IndexOf('.');
            if (dot > 0) { name = name.Substring(0, dot); }
            return name;
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value)) { return ""; }
            string text = value.Trim();
            if (text.Length > 253) { text = text.Substring(0, 253); }
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsControl(text[i])) { return ""; }
            }
            return text;
        }

        private static int ReadUInt16(byte[] data, int offset)
        {
            if (data == null || offset < 0 || data.Length < offset + 2) { return -1; }
            return (data[offset] << 8) | data[offset + 1];
        }
    }
}
