using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

internal static class EnvironmentSanitizer
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetEnvironmentStrings();

    [DllImport("kernel32.dll")]
    private static extern bool FreeEnvironmentStrings(IntPtr block);

    // ProcessStartInfo.EnvironmentVariables uses a case-insensitive dictionary. Some
    // environments contain both NO_PROXY and no_proxy, which makes that dictionary
    // throw while it is being populated. Keep the first casing and remove duplicates.
    internal static void RemoveCaseDuplicates()
    {
        IntPtr block = GetEnvironmentStrings();
        if (block == IntPtr.Zero) return;
        try
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long offset = 0;
            while (true)
            {
                IntPtr address = new IntPtr(block.ToInt64() + offset);
                string entry = Marshal.PtrToStringUni(address);
                if (String.IsNullOrEmpty(entry)) break;
                offset += (entry.Length + 1) * 2;
                int separator = entry.IndexOf('=');
                if (separator <= 0) continue;
                string name = entry.Substring(0, separator);
                if (seen.Add(name)) continue;
                try { Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.Process); }
                catch (Exception) { }
            }
        }
        finally { FreeEnvironmentStrings(block); }
    }
}
