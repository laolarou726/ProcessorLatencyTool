using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace ProcessorLatencyTool.Helpers.LatencyTester;

[SupportedOSPlatform("osx")]
public sealed unsafe partial class MacOsLatencyTester : LatencyTesterBase
{
    // QoS class constants from MacOS
    private const int QOS_CLASS_USER_INTERACTIVE = 0x21;
    private const int QOS_CLASS_USER_INITIATED = 0x19;
    private const int QOS_CLASS_DEFAULT = 0x15;
    private const int QOS_CLASS_UTILITY = 0x11;
    private const int QOS_CLASS_BACKGROUND = 0x09;

    // Error codes
    private const int EPERM = 1;
    private const int EINVAL = 22;

    [LibraryImport("libSystem.dylib", EntryPoint = "pthread_self")]
    private static partial IntPtr pthread_self();

    [LibraryImport("libSystem.dylib", EntryPoint = "pthread_set_qos_class_self_np")]
    private static partial int pthread_set_qos_class_self_np(int qos_class, int relative_priority);

    [LibraryImport("libSystem.dylib", EntryPoint = "pthread_getname_np")]
    private static partial int pthread_getname_np(IntPtr thread, byte* name, nuint len);

    [LibraryImport("libSystem.dylib", EntryPoint = "pthread_setname_np")]
    private static partial int pthread_setname_np(byte* name);

    [LibraryImport("libSystem.dylib", EntryPoint = "pthread_get_qos_class_np")]
    private static partial int pthread_get_qos_class_np(IntPtr thread, out int qos_class, out int relative_priority);

    [LibraryImport("arm64_registers", EntryPoint = "read_tpidr_el0")]
    private static partial ulong ReadTpidrEl0();

    [LibraryImport("arm64_registers", EntryPoint = "read_cntvct_el0")]
    private static partial ulong ReadCntvctEl0();

    [LibraryImport("arm64_registers", EntryPoint = "read_cntfrq_el0")]
    private static partial ulong ReadCntfrqEl0();

    private string GetQosClassName(int qosClass)
    {
        return qosClass switch
        {
            QOS_CLASS_USER_INTERACTIVE => "USER_INTERACTIVE",
            QOS_CLASS_USER_INITIATED => "USER_INITIATED",
            QOS_CLASS_DEFAULT => "DEFAULT",
            QOS_CLASS_UTILITY => "UTILITY",
            QOS_CLASS_BACKGROUND => "BACKGROUND",
            _ => $"UNKNOWN({qosClass})"
        };
    }

    private bool SetQosClass(int qosClass)
    {
        var threadId = pthread_self();
        int currentQos;
        int currentPriority;
        
        // Get current QoS class
        var getResult = pthread_get_qos_class_np(threadId, out currentQos, out currentPriority);
        if (getResult == 0)
        {
            Console.WriteLine($"Current QoS class: {GetQosClassName(currentQos)} (priority: {currentPriority})");
        }

        var result = pthread_set_qos_class_self_np(qosClass, 0);
        if (result != 0)
        {
            switch (result)
            {
                case EPERM:
                    Console.WriteLine($"Warning: Permission denied setting QoS class to {GetQosClassName(qosClass)}. Try running with sudo.");
                    break;
                case EINVAL:
                    Console.WriteLine($"Warning: Invalid QoS class value {GetQosClassName(qosClass)}");
                    break;
                default:
                    Console.WriteLine($"Warning: Failed to set QoS class to {GetQosClassName(qosClass)} (error {result})");
                    break;
            }
            return false;
        }

        // Verify the change
        getResult = pthread_get_qos_class_np(threadId, out currentQos, out currentPriority);
        if (getResult == 0)
        {
            if (currentQos == qosClass)
            {
                Console.WriteLine($"Successfully set QoS class to {GetQosClassName(currentQos)}");
                return true;
            }
            else
            {
                Console.WriteLine($"Warning: QoS class mismatch. Requested: {GetQosClassName(qosClass)}, Current: {GetQosClassName(currentQos)}");
                return false;
            }
        }
        
        return true;
    }

    private void SetThreadName(string name)
    {
        // Max thread name length in MacOS is 64 bytes
        var threadNameBytes = new byte[64];
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        Array.Copy(nameBytes, threadNameBytes, Math.Min(nameBytes.Length, 63));
        
        fixed (byte* namePtr = threadNameBytes)
        {
            pthread_setname_np(namePtr);
        }
    }

    protected override void SetThreadAffinity(int core)
    {
        try
        {
            // Set thread name for debugging
            SetThreadName($"LatencyTest_Core{core}");

            // First try to set QoS class to user interactive
            if (!SetQosClass(QOS_CLASS_USER_INTERACTIVE))
            {
                // If that fails, try user initiated
                if (!SetQosClass(QOS_CLASS_USER_INITIATED))
                {
                    // If that also fails, try default
                    if (!SetQosClass(QOS_CLASS_DEFAULT))
                    {
                        Console.WriteLine("Warning: Failed to set any QoS class. Performance may be affected.");
                    }
                }
            }

            // Get current thread info for logging
            var threadId = pthread_self();
            var threadName = new byte[64];
            fixed (byte* namePtr = threadName)
            {
                pthread_getname_np(threadId, namePtr, 64);
            }
            
            Console.WriteLine($"Info: Thread {threadId} (Core {core}) set to User Interactive QoS");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Error setting thread parameters: {ex.Message}");
        }
    }

    protected override void SetThreadPriority()
    {
        // This is handled in SetThreadAffinity
    }

    protected override ulong GetCurrentTimer()
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            return ReadCntvctEl0();
        }
        return (ulong)Stopwatch.GetTimestamp();
    }

    protected override double GetTimerPeriodNs()
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            var freq = ReadCntfrqEl0();
            return 1.0 / freq * 1_000_000_000.0;
        }
        return 1_000_000_000.0 / Stopwatch.Frequency;
    }
} 