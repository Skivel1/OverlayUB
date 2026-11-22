using LightWeightOverlay;
using System;

namespace LWOServ
{
    class Program
    {
        static void Main(string[] args)
        {
            var server = new LWOServer(
                Environment.GetEnvironmentVariable("LWO_SETTINGS") ?? "settings.json",
                Environment.GetEnvironmentVariable("LWO_STATE") ?? "globalstate.json");

            server.PrintWebLinks();
            Console.WriteLine("Servidor iniciado. Presiona Ctrl+C para detenerlo.");
            System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite);
        }
    }
}
