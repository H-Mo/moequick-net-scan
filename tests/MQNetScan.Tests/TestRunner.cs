using System;

namespace MQNetScan.Tests
{
    /// <summary>
    /// 极简断言工具。
    /// 不使用 NUnit/xUnit/MSTest（均需 NuGet 依赖，违反零第三方依赖红线）。
    /// </summary>
    internal static class TestRunner
    {
        private static int _passed;
        private static int _failed;
        private static string _currentCase = "";

        public static int PassedCount { get { return _passed; } }
        public static int FailedCount { get { return _failed; } }

        public static void BeginCase(string name)
        {
            _currentCase = name;
            Console.WriteLine("  -- " + name);
        }

        public static void AssertTrue(bool condition, string message)
        {
            if (condition)
            {
                _passed++;
                Console.WriteLine("     [PASS] " + message);
            }
            else
            {
                _failed++;
                Console.WriteLine("     [FAIL] " + message);
            }
        }

        public static void AssertEqual(object expected, object actual, string message)
        {
            bool ok = (expected == null && actual == null)
                      || (expected != null && expected.Equals(actual));

            if (ok)
            {
                _passed++;
                Console.WriteLine("     [PASS] " + message);
            }
            else
            {
                _failed++;
                Console.WriteLine("     [FAIL] " + message);
                Console.WriteLine("            期望: " + (expected == null ? "<null>" : expected.ToString()));
                Console.WriteLine("            实际: " + (actual == null ? "<null>" : actual.ToString()));
            }
        }

        public static void AssertThrows(Action action, string message)
        {
            try
            {
                action();
                _failed++;
                Console.WriteLine("     [FAIL] " + message + "（未抛出异常）");
            }
            catch (Exception)
            {
                _passed++;
                Console.WriteLine("     [PASS] " + message);
            }
        }

        public static void Skip(string message)
        {
            Console.WriteLine("     [SKIP] " + message);
        }

        public static void Summarize()
        {
            Console.WriteLine();
            Console.WriteLine("============================================================");
            Console.WriteLine(" 通过: " + _passed + "    失败: " + _failed);
            Console.WriteLine("============================================================");
        }
    }
}
