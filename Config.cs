using System;
using System.IO;

public class AppConfig
{
    public string pathTo1c = @"C:\Program Files (x86)\1cv8\8.3.27.1786\bin\1cv8.exe";
    public string platformVersion = "8.3";
    public string defaultServer = "";
    public string defaultDatabase = "";
    public string scannerPrefix = "QR:";
    public int scannerTimeoutMs = 1000;

    public int minBarcodeLen = 5;
    public string logFile = "1c-authorizator.log";
    public bool restart1C = true;
    public bool debug = false;
    public bool topMost = false;
    public string theme = "auto";
    public string appVersion = "1.0.0";


    public static AppConfig Load(string path)
    {
        AppConfig c = new AppConfig();
        if (!File.Exists(path)) return c;

        string[] lines = File.ReadAllLines(path);
        string currentSection = "";

        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

            // Reset section if the line is not indented (root-level key)
            bool isIndented = line.StartsWith(" ") || line.StartsWith("\t");
            if (!isIndented)
            {
                currentSection = "";
            }

            if (trimmed.EndsWith(":") && !trimmed.Contains("\""))
            {
                currentSection = trimmed.Substring(0, trimmed.Length - 1).Trim().ToLower();
                continue;
            }

            int colonIdx = trimmed.IndexOf(':');
            if (colonIdx < 0) continue;

            string key = trimmed.Substring(0, colonIdx).Trim().ToLower();
            string val = trimmed.Substring(colonIdx + 1).Trim();

            int hashIdx = val.IndexOf('#');
            if (hashIdx >= 0) val = val.Substring(0, hashIdx).Trim();

            if (val.StartsWith("\"") && val.EndsWith("\"") && val.Length >= 2)
                val = val.Substring(1, val.Length - 2);
            else if (val.StartsWith("'") && val.EndsWith("'") && val.Length >= 2)
                val = val.Substring(1, val.Length - 2);

            if (currentSection == "scanner")
            {
                if (key == "prefix") c.scannerPrefix = val;
                else if (key == "timeout_ms") int.TryParse(val, out c.scannerTimeoutMs);
                else if (key == "minbarcodelen") int.TryParse(val, out c.minBarcodeLen);
            }
            else
            {
                if (key == "pathto1c") c.pathTo1c = val;
                else if (key == "platformversion") c.platformVersion = val;
                else if (key == "defaultserver") c.defaultServer = val;
                else if (key == "defaultdatabase") c.defaultDatabase = val;
                else if (key == "logfile") c.logFile = val;
                else if (key == "restart1c") c.restart1C = val.ToLower() == "true" || val == "1";
                else if (key == "debug") c.debug = val.ToLower() == "true" || val == "1";
                else if (key == "topmost") c.topMost = val.ToLower() == "true" || val == "1";
                else if (key == "theme") c.theme = val.ToLower();
                else if (key == "appversion") c.appVersion = val;
            }

        }
        return c;
    }
}
