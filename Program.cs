using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
//using LuaInterface;
using NLua;
using System.Diagnostics;

namespace DCE_Manager
{
    static class Program
    {
        /// <summary>
        /// Point d'entrée principal de l'application.
        /// </summary>
        [STAThread]
        static void Main()
        {
			// Limiter l'utilisation de la mémoire à 1 Go
			//System.GC.TrySetMemoryLimit(1024 * 1024 * 1024);
		
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            

            // Surveillance de la mémoire toutes les 30 secondes
            System.Threading.Timer memoryCheckTimer = new System.Threading.Timer(_ =>
            {
                long memoryUsed = Process.GetCurrentProcess().WorkingSet64;
                long memoryLimit = 1024L * 1024L * 1024L; // 1 Go

                if (memoryUsed > memoryLimit)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    Console.WriteLine("Mémoire nettoyée, utilisation actuelle : " + (memoryUsed / (1024 * 1024)) + " Mo");
                }
            }, null, 0, 30000);

            Application.Run(new DCE_Manager.Form1());

        }

    }
}
