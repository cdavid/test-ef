using System;
using System.Net;

namespace ConsoleApp1
{
    internal static class Program
    {
        private static void Main()
        {
            ServicePointManager.DefaultConnectionLimit = 10000;

        }
    }
}
