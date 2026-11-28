// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;

namespace AiStackprint.Ui;

#pragma warning disable S6640 // WinRT has no managed way in without the 25 MB Windows SDK projection: a few COM calls through function pointers, checked by their HRESULTs
internal static partial class Notifier
{
    internal sealed partial class WindowsToast
    {
        // WinRT interfaces of Windows.Data.Xml.Dom and Windows.UI.Notifications (Windows 10 SDK). Methods start at vtable slot 6: IUnknown (3) and IInspectable (3) come first.
        private static readonly Guid _xmlDocumentIo = new("6CD0E74E-EE65-4489-9EBF-CA43E87BA637");     // IXmlDocumentIO: LoadXml
        private static readonly Guid _xmlDocument = new("F7F3A506-1E87-42D6-BCFB-B8C809FA5494");       // IXmlDocument
        private static readonly Guid _toastFactory = new("04124B20-82C6-4229-B109-FD9ED4662B53");      // IToastNotificationFactory: CreateToastNotification
        private static readonly Guid _managerStatics = new("50AC103F-D235-4598-BBEF-98FE4D1A3AD4");    // IToastNotificationManagerStatics: CreateToastNotifierWithId (slot 7)
        private const int Ok = 0, First = 6;

        /// <summary>Shows the toast; false when Windows refused (no WinRT, the app id not registered, or notifications turned off for the app).</summary>
        private static unsafe bool Toast(string xml)
        {
            nint doc = 0, io = 0, idoc = 0, factory = 0, toast = 0, statics = 0, notifier = 0;
            try
            {
                if (Activate("Windows.Data.Xml.Dom.XmlDocument", out doc) != Ok
                    || Marshal.QueryInterface(doc, in _xmlDocumentIo, out io) != Ok
                    || Call(io, First, xml) != Ok
                    || Marshal.QueryInterface(doc, in _xmlDocument, out idoc) != Ok
                    || Factory("Windows.UI.Notifications.ToastNotification", _toastFactory, out factory) != Ok
                    || ((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Slot(factory, First))(factory, idoc, &toast) != Ok
                    || Factory("Windows.UI.Notifications.ToastNotificationManager", _managerStatics, out statics) != Ok)
                {
                    return false;
                }

                var id = String(AppId);
                try
                {
                    if (((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Slot(statics, First + 1))(statics, id, &notifier) != Ok)
                    {
                        return false;
                    }
                }
                finally
                {
                    _ = WindowsDeleteString(id);
                }

                return ((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(notifier, First))(notifier, toast) == Ok;
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or COMException) { return false; }   // an older Windows
            finally
            {
                foreach (var p in new[] { notifier, statics, toast, factory, idoc, io, doc })
                {
                    if (p != 0)
                    {
                        Marshal.Release(p);
                    }
                }
            }
        }

        private static unsafe nint Slot(nint obj, int i) => (*(nint**)obj)[i];

        /// <summary>Calls a method that takes one string (an HSTRING).</summary>
        private static unsafe int Call(nint obj, int slot, string arg)
        {
            var h = String(arg);
            try
            {
                return ((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(obj, slot))(obj, h);
            }
            finally
            {
                _ = WindowsDeleteString(h);
            }
        }

        private static int Activate(string cls, out nint instance)
        {
            var h = String(cls);
            try
            {
                return RoActivateInstance(h, out instance);
            }
            finally
            {
                _ = WindowsDeleteString(h);
            }
        }

        private static int Factory(string cls, Guid iid, out nint factory)
        {
            var h = String(cls);
            try
            {
                return RoGetActivationFactory(h, in iid, out factory);
            }
            finally
            {
                _ = WindowsDeleteString(h);
            }
        }

        private static nint String(string s)
        {
            Marshal.ThrowExceptionForHR(WindowsCreateString(s, s.Length, out var h));
            return h;
        }

        [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
        private static partial int WindowsCreateString(string source, int length, out nint hstring);

        [LibraryImport("combase.dll")]
        private static partial int WindowsDeleteString(nint hstring);

        [LibraryImport("combase.dll")]
        private static partial int RoActivateInstance(nint classId, out nint instance);

        [LibraryImport("combase.dll")]
        private static partial int RoGetActivationFactory(nint classId, in Guid iid, out nint factory);
    }
}
#pragma warning restore S6640
