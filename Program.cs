using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using NLua;
using System.Diagnostics;
using DCE_Manager.Utils;

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

            // Empêche une exception non gérée sur le thread UI de tuer l'appli sans prévenir :
            // on l'intercepte et on montre notre propre fenêtre (texte copiable), l'utilisateur
            // peut continuer à utiliser DCE_Manager si le reste de l'appli n'est pas affecté.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                FormUtils.LogRegister("Unhandled UI exception: " + e.Exception);
                ErrorDialogForm.Show("An unexpected error occurred. You can copy the details below and report them.", e.Exception);
            };

            // Filet de sécurité pour les exceptions hors thread UI (rare ici, mais au moins on
            // log et on prévient avant que le process ne s'arrête).
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                FormUtils.LogRegister("Unhandled non-UI exception (terminating=" + e.IsTerminating + "): " + ex);
                if (ex != null)
                    ErrorDialogForm.Show("A critical error occurred and DCE_Manager may need to close.", ex);
            };

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

            Application.Run(new DCE_Manager.Main_Form());

        }

    }
}
