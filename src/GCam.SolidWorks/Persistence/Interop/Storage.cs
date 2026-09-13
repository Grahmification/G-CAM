using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace GCam.SolidWorks.Persistence.Interop
{
    /// <summary>
    /// OLE structured storage, declared here because .NET does not.
    /// </summary>
    /// <remarks>
    /// <c>System.Runtime.InteropServices.ComTypes</c> ships <see cref="IStream"/> but not
    /// <c>IStorage</c>, and SOLIDWORKS hands us one of the latter from
    /// <c>IModelDocExtension::IGet3rdPartyStorageStore</c>. The declaration below is the
    /// standard OLE interface, IID 0000000B-0000-0000-C000-000000000046.
    ///
    /// <b>Every method has to be declared, in vtable order, even the ones never called.</b>
    /// A COM interface is an array of function pointers; leaving one out silently shifts
    /// every method after it, so a call to <c>OpenStream</c> would land on whatever
    /// happened to be in that slot. The unused ones take <see cref="IntPtr"/> where the
    /// real signature is complicated - they occupy their slot and are never invoked.
    ///
    /// <b>Element names are limited to 31 characters plus a terminator.</b> That is the
    /// reason toolpath streams are numbered rather than named after the operation whose
    /// path they hold - a GUID is 36. *From docs* (the OLE structured storage limit);
    /// confirm by attempting a 40-character name if it ever matters.
    /// </remarks>
    [ComImport]
    [Guid("0000000B-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IStorage
    {
        void CreateStream(
            [MarshalAs(UnmanagedType.LPWStr)] string name,
            uint mode,
            uint reserved1,
            uint reserved2,
            out IStream stream);

        void OpenStream(
            [MarshalAs(UnmanagedType.LPWStr)] string name,
            IntPtr reserved1,
            uint mode,
            uint reserved2,
            out IStream stream);

        void CreateStorage(
            [MarshalAs(UnmanagedType.LPWStr)] string name,
            uint mode,
            uint reserved1,
            uint reserved2,
            out IStorage storage);

        void OpenStorage(
            [MarshalAs(UnmanagedType.LPWStr)] string name,
            IntPtr priority,
            uint mode,
            IntPtr exclude,
            uint reserved,
            out IStorage storage);

        void CopyTo(uint excludeCount, IntPtr excludeIids, IntPtr excludeNames, IStorage destination);

        void MoveElementTo(
            [MarshalAs(UnmanagedType.LPWStr)] string name,
            IStorage destination,
            [MarshalAs(UnmanagedType.LPWStr)] string newName,
            uint flags);

        void Commit(uint commitFlags);

        void Revert();

        // Never called; the slot has to exist so the methods after it line up.
        void EnumElements(uint reserved1, IntPtr reserved2, uint reserved3, out IntPtr enumerator);

        void DestroyElement([MarshalAs(UnmanagedType.LPWStr)] string name);

        void RenameElement(
            [MarshalAs(UnmanagedType.LPWStr)] string oldName,
            [MarshalAs(UnmanagedType.LPWStr)] string newName);

        void SetElementTimes(
            [MarshalAs(UnmanagedType.LPWStr)] string name,
            IntPtr creation,
            IntPtr access,
            IntPtr modification);

        void SetClass(ref Guid clsid);

        void SetStateBits(uint stateBits, uint mask);

        void Stat(out System.Runtime.InteropServices.ComTypes.STATSTG statstg, uint statFlag);
    }

    /// <summary>Open modes for a storage element.</summary>
    public static class Stgm
    {
        public const uint Read = 0x00000000;
        public const uint Write = 0x00000001;
        public const uint ReadWrite = 0x00000002;
        public const uint ShareExclusive = 0x00000010;
        public const uint Create = 0x00001000;

        /// <summary>Making a stream to write into, replacing any that is there.</summary>
        public const uint CreateForWriting = Create | Write | ShareExclusive;

        /// <summary>Opening a stream to read.</summary>
        public const uint OpenForReading = Read | ShareExclusive;
    }
}
