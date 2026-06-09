using System;
using System.IO;

public class Tests
{
    private static int passed = 0;
    private static int failed = 0;

    public static void Main()
    {
        Console.WriteLine("Running Tests...");
        Console.WriteLine("============================");

        TestConfigLoad();
        TestVersionFallback();

        Console.WriteLine("============================");
        Console.WriteLine(string.Format("Tests Completed: {0} passed, {1} failed.", passed, failed));
        if (failed > 0)
        {
            Environment.Exit(1);
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string testName)
    {
        if (expected.Equals(actual))
        {
            Console.WriteLine(string.Format("[PASS] {0}", testName));
            passed++;
        }
        else
        {
            Console.WriteLine(string.Format("[FAIL] {0} - Expected: {1}, Actual: {2}", testName, expected, actual));
            failed++;
        }
    }

    private static void TestConfigLoad()
    {
        string testConfigPath = "test_config.yaml";
        File.WriteAllText(testConfigPath, "appVersion: \"2.5.0\"\nscanner:\n  prefix: \"~\"\n  minBarcodeLen: 8\n");

        AppConfig config = AppConfig.Load(testConfigPath);

        AssertEqual("2.5.0", config.appVersion, "ConfigLoad_AppVersion");
        AssertEqual("~", config.scannerPrefix, "ConfigLoad_ScannerPrefix");
        AssertEqual(8, config.minBarcodeLen, "ConfigLoad_MinBarcodeLen");

        File.Delete(testConfigPath);
    }

    private static void TestVersionFallback()
    {
        // Testing behavior when version is missing or empty
        AppConfig config = new AppConfig();
        config.appVersion = ""; // Simulate empty
        
        // Temporarily inject to Program context
        LoginFormInstance._config = config;

        bool exceptionThrown = false;
        try
        {
            string v = Program.Version;
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("Не задана версия приложения"))
            {
                exceptionThrown = true;
            }
        }

        AssertEqual(true, exceptionThrown, "ProgramVersion_ThrowsOnEmptyAppVersion");
    }
}


