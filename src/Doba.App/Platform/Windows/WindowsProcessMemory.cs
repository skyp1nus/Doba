using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.OpenGL.Egl;
using Avalonia.Platform;

namespace Doba.App.Platform.Windows;

/// <summary>
/// <see cref="IProcessMemory"/> on Windows. Avalonia renders through ANGLE, an OpenGL ES layer over Direct3D 11, so the
/// device to trim is found through EGL's device query: the display's device (<c>EGL_DEVICE_EXT</c>), then that device's
/// <c>ID3D11Device</c> (<c>EGL_D3D11_DEVICE_ANGLE</c>). With any other graphics context (WGL, software) there is nothing
/// to trim.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsProcessMemory : IProcessMemory
{
    /// <summary>EGL_DEVICE_EXT (EGL_EXT_device_query).</summary>
    private const int EglDeviceExt = 0x322C;

    /// <summary>EGL_D3D11_DEVICE_ANGLE (EGL_ANGLE_device_d3d).</summary>
    private const int EglD3D11DeviceAngle = 0x33A1;

    /// <summary>Trim's slot in IDXGIDevice3's vtable: IUnknown 0–2, IDXGIObject 3–6, IDXGIDevice 7–11, IDXGIDevice1
    /// 12–13, IDXGIDevice2 14–16, then Trim.</summary>
    private const int TrimSlot = 17;

    private static readonly Guid s_dxgiDevice3 = new("6007896c-3244-4afd-bf18-a6d3beda5023");

    public bool TrimGraphicsDevice(IPlatformGraphicsContext context)
    {
        if (context is not EglContext egl)
        {
            return false;
        }

        // What Skia just deleted is still queued in the GL layer; Direct3D must see it before the driver trims.
        egl.GlInterface.Flush();
        var api = egl.EglInterface;
        if (!api.QueryDisplayAttribExt(egl.Display.Handle, EglDeviceExt, out var device) || device == IntPtr.Zero
            || !api.QueryDeviceAttribExt(device, EglD3D11DeviceAngle, out var d3dDevice) || d3dDevice == IntPtr.Zero)
        {
            return false;
        }

        // ANGLE hands its device out without adding a reference; only the interface queried here is released.
        if (Marshal.QueryInterface(d3dDevice, in s_dxgiDevice3, out var dxgiDevice) != 0 || dxgiDevice == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var trim = Marshal.ReadIntPtr(Marshal.ReadIntPtr(dxgiDevice), TrimSlot * IntPtr.Size);
            Marshal.GetDelegateForFunctionPointer<NativeMethods.DxgiDeviceTrim>(trim)(dxgiDevice);
            return true;
        }
        finally
        {
            Marshal.Release(dxgiDevice);
        }
    }

    public void TrimNativeHeaps()
    {
        var information = new NativeMethods.HEAP_OPTIMIZE_RESOURCES_INFORMATION
        {
            Version = NativeMethods.HEAP_OPTIMIZE_RESOURCES_CURRENT_VERSION,
        };

        // Best effort: on failure the heaps simply keep their caches.
        _ = NativeMethods.HeapSetInformation(IntPtr.Zero, NativeMethods.HeapOptimizeResources, ref information,
            (nuint)Marshal.SizeOf<NativeMethods.HEAP_OPTIMIZE_RESOURCES_INFORMATION>());
    }
}
