using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DCE_Manager.Utils
{
    // Rattache les process enfants (luae.exe) à un "Job Object" Windows, avec l'option
    // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE : dès que DCE_Manager.exe se termine, pour N'IMPORTE
    // QUELLE raison (fermeture normale, crash, Stop du debugger Visual Studio, "Fin de tâche"
    // dans le Gestionnaire des tâches...), Windows tue automatiquement tous les process
    // rattachés. C'est le seul moyen fiable d'éviter des luae.exe orphelins qui continuent de
    // tourner en arrière-plan et finissent par ralentir la machine.
    //
    // Un seul Job pour toute la durée de vie de l'appli : on y rattache chaque nouveau luae.exe
    // lancé via ProcessConsoleBridge.Start(). Rien à faire pour "libérer" un process du job une
    // fois qu'il s'est terminé normalement, Windows fait le ménage tout seul.
    internal static class ChildProcessTracker
    {
        private static readonly IntPtr _jobHandle;

        static ChildProcessTracker()
        {
            _jobHandle = CreateJobObject(IntPtr.Zero, null);

            var info = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            };

            var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = info
            };

            int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
            IntPtr extendedInfoPtr = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(extendedInfo, extendedInfoPtr, false);

                if (!SetInformationJobObject(_jobHandle, JobObjectExtendedLimitInformation, extendedInfoPtr, (uint)length))
                    throw new Win32Exception();
            }
            finally
            {
                Marshal.FreeHGlobal(extendedInfoPtr);
            }
        }

        // À appeler juste après process.Start(), avec process.Handle.
        public static void AddProcess(IntPtr processHandle)
        {
            AssignProcessToJobObject(_jobHandle, processHandle);
            // Pas de gestion d'erreur bloquante ici : si ça échoue (rare, ex: droits
            // insuffisants), le pire cas est qu'on retombe sur le comportement d'avant
            // (process potentiellement orphelin), pas un plantage de l'appli.
        }

        // ---- P/Invoke ----

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

        [DllImport("kernel32.dll")]
        private static extern bool SetInformationJobObject(IntPtr hJob, int JobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        private const int JobObjectExtendedLimitInformation = 9;
        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }
    }
}