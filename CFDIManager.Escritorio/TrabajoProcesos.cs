using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CFDIManager.Escritorio;

/// <summary>
/// Job Object de Windows: si la app se cierra o falla, Windows termina también CFDIManager.Web.exe (y lo que haya
/// abierto) para que no quede un servidor huérfano. PostgreSQL no entra aquí: se detiene con pg_ctl.
/// </summary>
internal static class TrabajoProcesos
{
    private const uint MatarAlCerrar = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
    private const int InformacionExtendida = 9; // JobObjectExtendedLimitInformation

    private static readonly IntPtr Trabajo = Crear();

    public static void Agregar(Process proceso)
    {
        if (!AssignProcessToJobObject(Trabajo, proceso.Handle))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static IntPtr Crear()
    {
        IntPtr trabajo = CreateJobObject(IntPtr.Zero, null);
        if (trabajo == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var informacion = new LimitesExtendidos { Basicos = new LimitesBasicos { Banderas = MatarAlCerrar } };
        int tamano = Marshal.SizeOf<LimitesExtendidos>();
        IntPtr memoria = Marshal.AllocHGlobal(tamano);

        try
        {
            Marshal.StructureToPtr(informacion, memoria, false);
            if (!SetInformationJobObject(trabajo, InformacionExtendida, memoria, (uint)tamano))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            Marshal.FreeHGlobal(memoria);
        }

        return trabajo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LimitesBasicos
    {
        public long TiempoUsuarioPorProceso;
        public long TiempoUsuarioPorTrabajo;
        public uint Banderas;
        public UIntPtr MinimoMemoria;
        public UIntPtr MaximoMemoria;
        public uint ProcesosActivos;
        public UIntPtr Afinidad;
        public uint Prioridad;
        public uint Planificacion;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ContadoresEs
    {
        public ulong Lecturas, Escrituras, Otras, BytesLeidos, BytesEscritos, BytesOtros;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LimitesExtendidos
    {
        public LimitesBasicos Basicos;
        public ContadoresEs Es;
        public UIntPtr MemoriaPorProceso;
        public UIntPtr MemoriaPorTrabajo;
        public UIntPtr PicoMemoriaProceso;
        public UIntPtr PicoMemoriaTrabajo;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr atributos, string? nombre);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr trabajo, int clase, IntPtr informacion, uint tamano);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr trabajo, IntPtr proceso);
}
