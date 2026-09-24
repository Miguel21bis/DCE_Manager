using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace DCE_Manager.Utils
{
    // Encapsule le lancement d'un exécutable console en tâche de fond (fenêtre cachée), avec
    // entrée/sortie standard redirigées. Sert à piloter un script console interactif
    // (ScriptsMod/Lua, via luae.exe) depuis une Form WinForms au lieu d'une fenêtre console visible.
    public class ProcessConsoleBridge : IDisposable
    {
        private Process _process;
        private readonly SynchronizationContext _uiContext;

        // Le process peut signaler "Exited" AVANT que les dernières lignes de stdout/stderr
        // n'aient fini d'être livrées par la lecture asynchrone (BeginOutputReadLine) - piège
        // classique de l'API Process. On attend donc que les 3 conditions soient réunies (process
        // sorti + flux stdout fermé + flux stderr fermé) avant de lever ProcessExited, sinon des
        // marqueurs ##DCEM_...## de toute fin de script (ex: rapport de bugs juste avant
        // os.exit(0)) peuvent arriver APRÈS coup et perturber l'écran déjà construit.
        private volatile bool _processHasExited;
        private volatile bool _stdoutClosed;
        private volatile bool _stderrClosed;
        private bool _lastExitSuccess;
        private readonly object _exitLock = new object();

        // Une ligne de sortie a été reçue. isError = true si ça vient de stderr. Toujours levé sur le thread UI.
        public event Action<string, bool> OutputReceived;

        // Le process s'est terminé (ET tout son flux de sortie a bien été lu). bool = true si code
        // de sortie 0. Toujours levé sur le thread UI.
        public event Action<bool> ProcessExited;

        public bool IsRunning
        {
            get { return _process != null && !_process.HasExited; }
        }

        public ProcessConsoleBridge()
        {
            // WinForms n'installe son SynchronizationContext qu'à la création du premier
            // contrôle. Ce bridge est un initialiseur de champ de ScriptsModRunner_Form,
            // donc il est construit AVANT le constructeur de la Form : en lancement normal
            // Main_Form l'a déjà installé, mais en mode ligne de commande il n'y a rien eu
            // avant et Current est null - les évènements partiraient alors sur un thread de
            // fond, et l'affichage ne se mettrait jamais à jour.
            _uiContext = SynchronizationContext.Current ?? new System.Windows.Forms.WindowsFormsSynchronizationContext();
        }

        // Démarre l'exécutable (ex: luae.exe), fenêtre cachée, entrée/sortie redirigées.
        // extraEnvironmentVariables : variables à ajouter/écraser en plus de l'environnement
        // hérité normalement (ex: pathDCS, pathSavedGames, versionPackageICM lus dans path.bat).
        public void Start(
            string exePath,
            string arguments,
            string workingDirectory,
            IDictionary<string, string> extraEnvironmentVariables = null)
        {
            var startInfo = new ProcessStartInfo()
            {
                FileName = exePath,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            if (extraEnvironmentVariables != null)
            {
                foreach (KeyValuePair<string, string> kvp in extraEnvironmentVariables)
                {
                    startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
                }
            }

            _processHasExited = false;
            _stdoutClosed = false;
            _stderrClosed = false;
            _lastExitSuccess = false;

            _process = new Process();
            _process.StartInfo = startInfo;
            _process.EnableRaisingEvents = true;

            _process.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null)
                {
                    // .NET envoie une dernière ligne "null" quand le flux stdout se ferme :
                    // c'est notre vrai signal de "plus rien à lire", pas Process.Exited.
                    _stdoutClosed = true;
                    TryRaiseExited();
                }
                else
                {
                    RaiseOutput(e.Data, false);
                }
            };

            _process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data == null)
                {
                    _stderrClosed = true;
                    TryRaiseExited();
                }
                else
                {
                    RaiseOutput(e.Data, true);
                }
            };

            _process.Exited += (s, e) =>
            {
                _processHasExited = true;
                try { _lastExitSuccess = _process.ExitCode == 0; }
                catch { _lastExitSuccess = false; /* process déjà nettoyé, tant pis */ }
                TryRaiseExited();
            };

            _process.Start();

            FormUtils.LogRegister("Bridge | luae.exe lance : PID " + _process.Id
    + " | exe=" + exePath
    + " | args=" + arguments
    + " | wd=" + workingDirectory
    + " | uiContext=" + (_uiContext == null ? "NULL" : _uiContext.GetType().Name));

            ChildProcessTracker.AddProcess(_process.Handle); // rattache luae.exe au Job : tué automatiquement si DCE_Manager.exe meurt, même brutalement (Stop debugger, crash...)

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        // Envoie une ligne au process, comme si elle avait été tapée au clavier suivie d'Entrée.
        public void SendLine(string text)
        {
            if (!IsRunning)
                return;

            try
            {
                _process.StandardInput.WriteLine(text);
                _process.StandardInput.Flush();
            }
            catch (Exception ex)
            {
                // Ne doit jamais planter l'appli ni ressembler à un crash du script Lua :
                // préfixe distinct pour qu'on sache immédiatement que ça vient d'ici si ça revient.
                RaiseOutput("[BRIDGE] Échec d'envoi vers le process : " + ex.Message, true);
            }
        }

        private void RaiseOutput(string line, bool isError)
        {
            FormUtils.LogRegister("Bridge | ligne recue : " + line);

            if (line == null)
                return; // .NET envoie une ligne "null" en fin de flux, ce n'est pas une vraie ligne

            if (_uiContext != null)
                _uiContext.Post(delegate { OutputReceived?.Invoke(line, isError); }, null);
            else
                OutputReceived?.Invoke(line, isError);
        }

        // Les 3 évènements (fin stdout, fin stderr, Exited) peuvent arriver dans n'importe quel
        // ordre et depuis des threads différents : on ne lève ProcessExited qu'une seule fois,
        // quand les 3 sont réunis.
        private void TryRaiseExited()
        {
            lock (_exitLock)
            {
                if (!_processHasExited || !_stdoutClosed || !_stderrClosed)
                    return;
            }

            bool success = _lastExitSuccess;
            FormUtils.LogRegister("Bridge | process termine, succes=" + success);

            if (_uiContext != null)
                _uiContext.Post(delegate { ProcessExited?.Invoke(success); }, null);
            else
                ProcessExited?.Invoke(success);
        }

        // Tue le process s'il tourne encore (ex : fermeture forcée de la Form). On passe par
        // "taskkill /T" plutôt que _process.Kill() tout seul : Kill() ne tue QUE ce process précis,
        // pas d'éventuels processus qu'il aurait lancés lui-même entre-temps. C'est très
        // probablement la cause des luae.exe orphelins qui s'accumulent quand on ferme la fenêtre
        // avant la fin du script - "/T" tue tout l'arbre d'un coup.
        public void Kill()
        {
            if (_process == null)
                return;

            int pid;
            try
            {
                if (!IsRunning)
                    return;
                pid = _process.Id;
            }
            catch { return; }

            try
            {
                var taskkillInfo = new ProcessStartInfo("taskkill", "/PID " + pid + " /T /F")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using (var taskkillProcess = Process.Start(taskkillInfo))
                {
                    taskkillProcess.WaitForExit(3000);
                }
            }
            catch
            {
                // Repli si taskkill est indisponible (rare) : au moins tuer le process principal.
                try { _process.Kill(); } catch { }
            }
        }

        public void Dispose()
        {
            Kill();
            _process?.Dispose();
        }
    }
}