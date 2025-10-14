// FILE: src/Solitaire.Cli/CliInfo.cs
using System;
using System.Reflection;

namespace Solitaire.Cli
{
    internal static class CliInfo
    {
        public static string Version()
        {
            try
            {
                Version v = Assembly.GetExecutingAssembly().GetName().Version;
                return v != null ? v.ToString() : "1.0.0.0";
            }
            catch
            {
                return "1.0.0.0";
            }
        }
    }
}