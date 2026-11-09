using System;
using System.Globalization;
using System.Text;

internal static class AddressParser
{
    internal static bool TryParse(string input, out string address, out string error)
    {
        address = null;
        error = null;
        string text = Normalize(input);
        if (text.Length == 0)
        {
            error = "请填写 IP 地址和端口。";
            return false;
        }

        int colon = text.IndexOf(':');
        if (colon < 0)
        {
            error = "缺少端口，请按 192.168.1.1:9999 的格式填写。";
            return false;
        }
        if (text.IndexOf(':', colon + 1) >= 0)
        {
            error = "地址中只能有一个冒号，冒号后面是端口。";
            return false;
        }

        string host = text.Substring(0, colon);
        string portText = text.Substring(colon + 1);
        string[] parts = host.Split('.');
        if (parts.Length != 4)
        {
            error = "IPv4 地址需要 4 段，例如 192.168.1.1。";
            return false;
        }

        var octets = new int[4];
        for (int i = 0; i < 4; i++)
        {
            string part = parts[i];
            if (part.Length == 0)
            {
                error = "第 " + (i + 1) + " 段地址不能为空。";
                return false;
            }
            if (part.Length > 3)
            {
                error = "第 " + (i + 1) + " 段最多填 3 位数字。";
                return false;
            }
            int value;
            if (!Int32.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out value) || value < 0 || value > 255)
            {
                error = "第 " + (i + 1) + " 段地址应在 0–255 之间。";
                return false;
            }
            octets[i] = value;
        }

        if (portText.Length == 0)
        {
            error = "端口不能为空，请填写 1–65535。";
            return false;
        }
        if (portText.Length > 5)
        {
            error = "端口最多填 5 位数字。";
            return false;
        }
        int port;
        if (!Int32.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port < 1 || port > 65535)
        {
            error = "端口应在 1–65535 之间。";
            return false;
        }

        address = octets[0] + "." + octets[1] + "." + octets[2] + "." + octets[3] + ":" + port;
        return true;
    }

    internal static bool TryParsePairingCode(string input, out string code, out string error)
    {
        code = null;
        error = null;
        string text = Normalize(input);
        if (text.Length != 6)
        {
            error = "配对码应为 6 位数字。";
            return false;
        }
        for (int i = 0; i < text.Length; i++)
            if (text[i] < '0' || text[i] > '9')
            {
                error = "配对码应为 6 位数字。";
                return false;
            }
        code = text;
        return true;
    }

    private static string Normalize(string input)
    {
        if (String.IsNullOrEmpty(input)) return String.Empty;
        var value = new StringBuilder(input.Length);
        foreach (char original in input.Trim())
        {
            char c = original;
            if (c >= '０' && c <= '９') c = (char)('0' + (c - '０'));
            else if (c == '：') c = ':';
            else if (c == '．' || c == '。') c = '.';
            if (Char.IsWhiteSpace(c)) continue;
            value.Append(c);
        }
        return value.ToString();
    }
}
