using System;
using System.Runtime;
using System.Threading;

namespace Runix.Desktop.Services;

/// <summary>
/// Serviço inteligente para liberação e trim de memória ociosa e descarte de heaps (Fase 2).
/// </summary>
public static class MemoryTrimService
{
    private static DateTime _lastTrim = DateTime.MinValue;

    /// <summary>
    /// Notifica o Garbage Collector para liberar páginas de memória não utilizadas de volta ao sistema operacional.
    /// Inclui throttling de 2 segundos para evitar sobrecarga de coletas consecutivas.
    /// </summary>
    /// <param name="forceLoh">Se true, compacta o Large Object Heap (LOH), ideal após fechar abas pesadas ou desconectar.</param>
    public static void TrimIdleMemory(bool forceLoh = false)
    {
        if ((DateTime.UtcNow - _lastTrim).TotalSeconds < 2) return;
        _lastTrim = DateTime.UtcNow;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                if (forceLoh)
                {
                    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect(2, GCCollectionMode.Forced, blocking: false, compacting: true);
                }
                else
                {
                    GC.Collect(1, GCCollectionMode.Optimized, blocking: false);
                }
            }
            catch
            {
                // Silencioso em caso de restrição de runtime
            }
        });
    }
}
