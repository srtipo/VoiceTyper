using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using VoiceTyper.Models;

namespace VoiceTyper.Services;

public sealed class CudaDetector
{
    private const string CudaDriverDll = "nvcuda.dll";
    private const int CudaSuccess = 0;

    private int _initStatus = int.MinValue;

    public bool IsAvailable()
    {
        try
        {
            var system32 = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                CudaDriverDll);
            if (File.Exists(system32)) return true;

            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    try
                    {
                        if (File.Exists(Path.Combine(dir, CudaDriverDll))) return true;
                    }
                    catch
                    {
                    }
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public int GetDeviceCount()
    {
        if (!IsAvailable()) return 0;
        var initStatus = EnsureInitialized();
        if (initStatus != CudaSuccess) return 0;
        try
        {
            var status = CudaGetDeviceCount(out var count);
            if (status != CudaSuccess || count < 0) return 0;
            return count;
        }
        catch (DllNotFoundException)
        {
            return 0;
        }
        catch (EntryPointNotFoundException)
        {
            return 0;
        }
        catch
        {
            return 0;
        }
    }

    private int EnsureInitialized()
    {
        if (_initStatus != int.MinValue) return _initStatus;
        try
        {
            _initStatus = CudaInit(0);
        }
        catch (DllNotFoundException)
        {
            _initStatus = -1;
        }
        catch (EntryPointNotFoundException)
        {
            _initStatus = -1;
        }
        catch
        {
            _initStatus = -1;
        }
        return _initStatus;
    }

    public IReadOnlyList<CudaDevice> GetDevices()
    {
        var count = GetDeviceCount();
        if (count <= 0) return Array.Empty<CudaDevice>();

        var devices = new List<CudaDevice>(count);
        for (var i = 0; i < count; i++)
        {
            var name = TryGetDeviceName(i);
            devices.Add(new CudaDevice(i, name ?? $"GPU {i}"));
        }
        return devices;
    }

    private static string? TryGetDeviceName(int index)
    {
        try
        {
            const int maxLen = 256;
            var buffer = new byte[maxLen];
            var status = CudaGetDeviceName(buffer, maxLen, index);
            if (status != CudaSuccess) return null;

            var terminator = Array.IndexOf<byte>(buffer, 0);
            if (terminator < 0) terminator = buffer.Length;
            return System.Text.Encoding.UTF8.GetString(buffer, 0, terminator).Trim();
        }
        catch
        {
            return null;
        }
    }

    [DllImport(CudaDriverDll, EntryPoint = "cuInit", SetLastError = true)]
    private static extern int CudaInit(uint flags);

    [DllImport(CudaDriverDll, EntryPoint = "cuDeviceGetCount", SetLastError = true)]
    private static extern int CudaGetDeviceCount(out int count);

    [DllImport(CudaDriverDll, EntryPoint = "cuDeviceGetName", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int CudaGetDeviceName(byte[] name, int len, int index);
}
