using System.Runtime.InteropServices;
using System.Windows;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace CrowLink.Services.Explorer;

public static class OleExplorerDragService
{
    private const uint DropEffectCopy = 1;
    private const int RpcChangedMode = unchecked((int)0x80010106);

    public static bool TryExtractFileDrop(System.Windows.IDataObject dataObject, out string[] paths)
    {
        paths = [];
        if (!dataObject.GetDataPresent(DataFormats.FileDrop, false) ||
            dataObject.GetData(DataFormats.FileDrop, false) is not string[] dropped)
        {
            return false;
        }

        paths = dropped
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return paths.Length > 0;
    }

    public static bool StartFileDrop(IReadOnlyList<string> paths)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            throw new InvalidOperationException("OLE drag-and-drop must run on the WPF STA thread.");
        }

        var existing = paths
            .Select(Path.GetFullPath)
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (existing.Length == 0)
        {
            throw new InvalidOperationException("Explorer로 드래그할 로컬 파일이 없습니다.");
        }

        // WPF DataObject exposes the managed COM IDataObject contract. FileDrop is rendered as
        // CF_HDROP, which Explorer consumes through its registered OLE IDropTarget.
        var dataObject = new DataObject();
        dataObject.SetData(DataFormats.FileDrop, existing, false);
        var comDataObject = (ComIDataObject)dataObject;
        var dropSource = new OleDropSource();
        var initializeResult = NativeMethods.OleInitialize(0);
        if (initializeResult < 0 && initializeResult != RpcChangedMode)
        {
            Marshal.ThrowExceptionForHR(initializeResult);
        }

        if (initializeResult == RpcChangedMode)
        {
            throw new InvalidOperationException("현재 UI 스레드의 COM apartment에서 OLE drag-and-drop을 시작할 수 없습니다.");
        }

        try
        {
            var result = NativeMethods.DoDragDrop(comDataObject, dropSource, DropEffectCopy, out var effect);
            if (result < 0)
            {
                Marshal.ThrowExceptionForHR(result);
            }

            return (effect & DropEffectCopy) != 0;
        }
        finally
        {
            NativeMethods.OleUninitialize();
        }
    }

    [ComVisible(true)]
    [Guid("00000121-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IOleDropSource
    {
        [PreserveSig]
        int QueryContinueDrag([MarshalAs(UnmanagedType.Bool)] bool escapePressed, uint keyState);

        [PreserveSig]
        int GiveFeedback(uint effect);
    }

    [ComVisible(true)]
    private sealed class OleDropSource : IOleDropSource
    {
        private const uint MouseLeft = 0x0001;
        private const uint MouseRight = 0x0002;
        private const int Continue = 0;
        private const int Drop = 0x00040100;
        private const int Cancel = 0x00040101;
        private const int UseDefaultCursors = 0x00040102;

        public int QueryContinueDrag(bool escapePressed, uint keyState)
        {
            if (escapePressed)
            {
                return Cancel;
            }

            return (keyState & (MouseLeft | MouseRight)) == 0 ? Drop : Continue;
        }

        public int GiveFeedback(uint effect) => UseDefaultCursors;
    }

    private static class NativeMethods
    {
        [DllImport("ole32.dll")]
        public static extern int OleInitialize(nint reserved);

        [DllImport("ole32.dll")]
        public static extern void OleUninitialize();

        [DllImport("ole32.dll")]
        public static extern int DoDragDrop(
            [MarshalAs(UnmanagedType.Interface)] ComIDataObject dataObject,
            [MarshalAs(UnmanagedType.Interface)] IOleDropSource dropSource,
            uint allowedEffects,
            out uint effect);
    }
}
