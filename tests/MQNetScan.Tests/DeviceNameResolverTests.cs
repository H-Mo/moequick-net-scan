using System;
using System.Net;
using MQNetScan.Infrastructure;

namespace MQNetScan.Tests
{
    internal static class DeviceNameResolverTests
    {
        public static void Run()
        {
            TestDnsNameNormalization();
            TestNetBiosRequestShape();
            TestNetBiosResponseParsing();
            TestMalformedResponses();
        }

        private static void TestDnsNameNormalization()
        {
            Console.WriteLine();
            Console.WriteLine("[NAME-1] DNS 名称标准化");
            TestRunner.BeginCase("反向 DNS 名称取主机部分并清理");
            TestRunner.AssertEqual("workstation", DeviceNameResolver.StripDomain("workstation.office.local"),
                "移除 DNS 域后缀");
            TestRunner.AssertEqual("printer-01", DeviceNameResolver.StripDomain(" printer-01 "),
                "清理首尾空格");
            TestRunner.AssertEqual("", DeviceNameResolver.StripDomain(""), "空值返回空");
        }

        private static void TestNetBiosRequestShape()
        {
            Console.WriteLine();
            Console.WriteLine("[NAME-2] NetBIOS 请求格式");
            TestRunner.BeginCase("发送 Node Status 查询且长度正确");
            byte[] packet = DeviceNameResolver.BuildNodeStatusRequest();
            TestRunner.AssertEqual(50, packet.Length, "请求长 50 字节");
            TestRunner.AssertEqual((byte)1, packet[5], "问题计数为 1");
            TestRunner.AssertEqual((byte)0x20, packet[12], "NetBIOS label 长度正确");
            TestRunner.AssertEqual((byte)0x21, packet[47], "查询类型为 NBSTAT");
            TestRunner.AssertEqual((byte)1, packet[49], "查询类为 IN");
        }

        private static void TestNetBiosResponseParsing()
        {
            Console.WriteLine();
            Console.WriteLine("[NAME-3] NetBIOS 响应解析");
            TestRunner.BeginCase("提取唯一活动的工作站名称");
            byte[] response = CreateResponse("WORKSTATION", 0x00, 0x8000);
            TestRunner.AssertEqual("WORKSTATION", DeviceNameResolver.ParseNodeStatusResponse(response),
                "解析到计算机名");
        }

        private static void TestMalformedResponses()
        {
            Console.WriteLine();
            Console.WriteLine("[NAME-4] 畸形 NetBIOS 响应处理");
            TestRunner.BeginCase("畸形包不崩溃并返回空名");
            TestRunner.AssertEqual("", DeviceNameResolver.ParseNodeStatusResponse(null), "null 包为空名");
            TestRunner.AssertEqual("", DeviceNameResolver.ParseNodeStatusResponse(new byte[8]), "短包为空名");
            byte[] invalid = CreateResponse("BADSERVICE", 0x20, 0x8000);
            TestRunner.AssertEqual("", DeviceNameResolver.ParseNodeStatusResponse(invalid),
                "非 workstation 后缀不作为设备名");
        }

        private static byte[] CreateResponse(string name, byte suffix, ushort flags)
        {
            byte[] data = new byte[64];
            data[0] = 0x11;
            data[1] = 0x11;
            data[2] = 0x85;
            data[3] = 0x00;
            data[4] = 0x00;
            data[5] = 0x00;
            data[6] = 0x00;
            data[7] = 0x01;
            data[12] = 0xC0;
            data[13] = 0x0C;
            data[14] = 0x00;
            data[15] = 0x21;
            data[16] = 0x00;
            data[17] = 0x01;
            data[18] = 0x00;
            data[19] = 0x00;
            data[20] = 0x00;
            data[21] = 0x00;
            data[22] = 0x00;
            data[23] = 0x13;
            data[24] = 0x01;
            string padded = (name ?? "").PadRight(15).Substring(0, 15);
            byte[] bytes = System.Text.Encoding.ASCII.GetBytes(padded);
            Array.Copy(bytes, 0, data, 25, 15);
            data[40] = suffix;
            data[41] = (byte)(flags >> 8);
            data[42] = (byte)(flags & 0xFF);
            return data;
        }
    }
}
