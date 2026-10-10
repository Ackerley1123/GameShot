using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace GameShot
{
    // Windows Desktop Duplication：从显卡桌面帧读取图像，不向游戏注入代码。
    // 接口布局依据 Windows SDK 的 d3d11.h / dxgi1_2.h；原生资源必须在 finally 中释放。
    internal static class DesktopDuplication
    {
        private static readonly Guid FactoryId = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        private static readonly Guid Output1Id = new Guid("00cddea8-939b-4b83-a340-a685226666cc");
        private static readonly Guid TextureId = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
        private const int NotFound = unchecked((int)0x887A0002);
        private const int WaitTimeout = unchecked((int)0x887A0027);
        internal static string LastFrameInfo { get; private set; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct OutputDescription
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
            public Native.Rect Bounds;
            public int Attached, Rotation;
            public IntPtr Monitor;
        }
        [StructLayout(LayoutKind.Sequential)] private struct FrameInfo
        {
            public long LastPresentTime, LastMouseUpdateTime;
            public uint AccumulatedFrames;
            public int RectsCoalesced, ProtectedContentMaskedOut;
            public int PointerX, PointerY, PointerVisible;
            public uint MetadataSize, PointerShapeSize;
        }
        [StructLayout(LayoutKind.Sequential)] private struct TextureDescription
        {
            public uint Width, Height, MipLevels, ArraySize, Format;
            public uint SampleCount, SampleQuality, Usage, BindFlags, CpuAccessFlags, MiscFlags;
        }
        [StructLayout(LayoutKind.Sequential)] private struct MappedResource
        {
            public IntPtr Data;
            public uint RowPitch, DepthPitch;
        }

        [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid id, out IntPtr factory);
        [DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software,
            uint flags, IntPtr featureLevels, uint featureCount, uint sdkVersion, out IntPtr device, out uint feature, out IntPtr context);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Enumerate(IntPtr self, uint index, out IntPtr item);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetOutputDescription(IntPtr self, out OutputDescription description);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Duplicate(IntPtr self, IntPtr device, out IntPtr duplication);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Acquire(IntPtr self, uint timeout, out FrameInfo info, out IntPtr resource);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ReleaseFrame(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GetTextureDescription(IntPtr self, out TextureDescription description);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateTexture(IntPtr self, ref TextureDescription description, IntPtr initialData, out IntPtr texture);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void CopyResource(IntPtr self, IntPtr destination, IntPtr source);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int MapResource(IntPtr self, IntPtr resource, uint subresource, uint mapType, uint flags, out MappedResource mapped);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void UnmapResource(IntPtr self, IntPtr resource, uint subresource);

        private static T Method<T>(IntPtr instance, int slot) where T : Delegate
        {
            IntPtr table = Marshal.ReadIntPtr(instance);
            return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(table, slot * IntPtr.Size));
        }
        private static void Check(int result) { if (result < 0) Marshal.ThrowExceptionForHR(result); }
        private static void Release(ref IntPtr value) { if (value != IntPtr.Zero) { Marshal.Release(value); value = IntPtr.Zero; } }

        internal static Bitmap Capture(string displayName, out bool protectedContent)
        {
            protectedContent = false;
            IntPtr factory = IntPtr.Zero;
            try
            {
                Guid id = FactoryId;
                Check(CreateDXGIFactory1(ref id, out factory));
                var enumerateAdapters = Method<Enumerate>(factory, 12);
                for (uint adapterIndex = 0; ; adapterIndex++)
                {
                    IntPtr adapter;
                    int result = enumerateAdapters(factory, adapterIndex, out adapter);
                    if (result == NotFound) break;
                    Check(result);
                    try
                    {
                        var enumerateOutputs = Method<Enumerate>(adapter, 7);
                        for (uint outputIndex = 0; ; outputIndex++)
                        {
                            IntPtr output;
                            result = enumerateOutputs(adapter, outputIndex, out output);
                            if (result == NotFound) break;
                            Check(result);
                            try
                            {
                                OutputDescription description;
                                Check(Method<GetOutputDescription>(output, 7)(output, out description));
                                if (description.Attached != 0 && string.Equals(description.Name, displayName, StringComparison.OrdinalIgnoreCase))
                                    return ReadFrame(adapter, output, description.Rotation, out protectedContent);
                            }
                            finally { Release(ref output); }
                        }
                    }
                    finally { Release(ref adapter); }
                }
                throw new InvalidOperationException("DirectX 未找到显示器 " + displayName);
            }
            finally { Release(ref factory); }
        }

        private static Bitmap ReadFrame(IntPtr adapter, IntPtr output, int rotation, out bool protectedContent)
        {
            protectedContent = false;
            IntPtr device = IntPtr.Zero, context = IntPtr.Zero, output1 = IntPtr.Zero, duplication = IntPtr.Zero;
            IntPtr resource = IntPtr.Zero, texture = IntPtr.Zero, staging = IntPtr.Zero;
            bool ownsFrame = false, mapped = false;
            Bitmap image = null;
            try
            {
                uint feature;
                // 必须使用显示器所属的显卡；多显卡机器不能简单选择默认 GPU。
                Check(D3D11CreateDevice(adapter, 0, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out device, out feature, out context));
                Guid outputId = Output1Id;
                Check(Marshal.QueryInterface(output, in outputId, out output1));
                Check(Method<Duplicate>(output1, 22)(output1, device, out duplication));

                FrameInfo frame = new FrameInfo();
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    int result = Method<Acquire>(duplication, 8)(duplication, 150, out frame, out resource);
                    if (result == WaitTimeout) continue;
                    Check(result);
                    ownsFrame = true;
                    // 新建 duplication 时可能先收到只有鼠标更新的空白帧。
                    // 等到真正的桌面呈现，再复制纹理；否则首张截图会全黑。
                    if (frame.LastPresentTime != 0 && frame.AccumulatedFrames > 0) break;
                    Release(ref resource);
                    Check(Method<ReleaseFrame>(duplication, 14)(duplication));
                    ownsFrame = false;
                }
                if (!ownsFrame) throw new InvalidOperationException("DirectX 尚未返回有效桌面帧，请使用普通捕获。");
                LastFrameInfo = "present=" + frame.LastPresentTime + " accumulated=" + frame.AccumulatedFrames + " protected=" + frame.ProtectedContentMaskedOut;
                protectedContent = frame.ProtectedContentMaskedOut != 0;
                Guid textureId = TextureId;
                Check(Marshal.QueryInterface(resource, in textureId, out texture));
                TextureDescription description;
                Method<GetTextureDescription>(texture, 10)(texture, out description);
                if (description.Width == 0 || description.Height == 0 || description.Width > 32768 || description.Height > 32768)
                    throw new InvalidOperationException("显卡返回了无效的截图尺寸。");
                if (description.Format != 87) throw new InvalidOperationException("显示器返回了不支持的像素格式。");
                description.MipLevels = 1;
                description.ArraySize = 1;
                description.Usage = 3; // D3D11_USAGE_STAGING：允许 CPU 读取，不作为渲染目标。
                description.BindFlags = 0;
                description.CpuAccessFlags = 0x20000;
                description.MiscFlags = 0;
                Check(Method<CreateTexture>(device, 5)(device, ref description, IntPtr.Zero, out staging));
                Method<CopyResource>(context, 47)(context, staging, texture);
                MappedResource data;
                Check(Method<MapResource>(context, 14)(context, staging, 0, 1, 0, out data));
                LastFrameInfo += " size=" + description.Width + "x" + description.Height + " rowPitch=" + data.RowPitch;
                mapped = true;
                image = new Bitmap((int)description.Width, (int)description.Height, PixelFormat.Format32bppRgb);
                BitmapData pixels = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                try
                {
                    // GPU 每行可能含有对齐填充，按 RowPitch 读，不能一次性复制 width*height。
                    byte[] row = new byte[checked((int)description.Width * 4)];
                    for (int y = 0; y < image.Height; y++)
                    {
                        Marshal.Copy(IntPtr.Add(data.Data, checked(y * (int)data.RowPitch)), row, 0, row.Length);
                        Marshal.Copy(row, 0, IntPtr.Add(pixels.Scan0, y * pixels.Stride), row.Length);
                    }
                }
                finally { image.UnlockBits(pixels); }
                if (rotation == 2) image.RotateFlip(RotateFlipType.Rotate90FlipNone);
                else if (rotation == 3) image.RotateFlip(RotateFlipType.Rotate180FlipNone);
                else if (rotation == 4) image.RotateFlip(RotateFlipType.Rotate270FlipNone);
                Bitmap ready = image;
                image = null;
                return ready;
            }
            finally
            {
                if (image != null) image.Dispose();
                if (mapped) Method<UnmapResource>(context, 15)(context, staging, 0);
                if (ownsFrame) Method<ReleaseFrame>(duplication, 14)(duplication);
                Release(ref staging); Release(ref texture); Release(ref resource); Release(ref duplication);
                Release(ref output1); Release(ref context); Release(ref device);
            }
        }
    }
}
