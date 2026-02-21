using System;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;

namespace FFI_ScreenReader.Utils
{
    /// <summary>
    /// Utility for reading IL2CPP object fields via pointer offsets.
    /// Eliminates boilerplate unsafe pointer arithmetic across patch files.
    /// Supports both unsafe dereference and Marshal-based patterns.
    /// </summary>
    public static class IL2CppFieldReader
    {
        /// <summary>
        /// Reads a pointer field at the given offset from a base pointer.
        /// Uses unsafe dereference: *(IntPtr*)((byte*)basePtr + offset)
        /// </summary>
        public static unsafe IntPtr ReadPointer(IntPtr basePtr, int offset)
        {
            if (basePtr == IntPtr.Zero) return IntPtr.Zero;
            return *(IntPtr*)((byte*)basePtr.ToPointer() + offset);
        }

        /// <summary>
        /// Reads a pointer through a chain of offsets.
        /// E.g., ReadPointerChain(ptr, 0x28, 0x10) reads ptr->0x28->0x10.
        /// Returns IntPtr.Zero if any intermediate pointer is null.
        /// </summary>
        public static IntPtr ReadPointerChain(IntPtr basePtr, params int[] offsets)
        {
            IntPtr current = basePtr;
            for (int i = 0; i < offsets.Length; i++)
            {
                current = ReadPointer(current, offsets[i]);
                if (current == IntPtr.Zero) return IntPtr.Zero;
            }
            return current;
        }

        /// <summary>
        /// Reads a 32-bit integer field at the given offset.
        /// </summary>
        public static unsafe int ReadInt32(IntPtr basePtr, int offset)
        {
            if (basePtr == IntPtr.Zero) return 0;
            return *(int*)((byte*)basePtr.ToPointer() + offset);
        }

        /// <summary>
        /// Reads a boolean field (single byte) at the given offset.
        /// </summary>
        public static bool ReadBool(IntPtr basePtr, int offset)
        {
            if (basePtr == IntPtr.Zero) return false;
            return Marshal.ReadByte(basePtr + offset) != 0;
        }

        /// <summary>
        /// Reads a pointer at offset, then wraps it as an IL2CPP object of type T.
        /// Returns null if the pointer at offset is null.
        /// </summary>
        public static T ReadField<T>(IntPtr basePtr, int offset) where T : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
        {
            IntPtr fieldPtr = ReadPointer(basePtr, offset);
            if (fieldPtr == IntPtr.Zero) return null;
            return (T)Activator.CreateInstance(typeof(T), fieldPtr);
        }

        /// <summary>
        /// Reads a pointer to a UnityEngine.UI.Text object at the given offset
        /// and returns the text string. Returns null if pointer is null or text is empty.
        /// </summary>
        public static string ReadTextString(IntPtr basePtr, int offset)
        {
            IntPtr textPtr = ReadPointer(basePtr, offset);
            if (textPtr == IntPtr.Zero) return null;

            try
            {
                var textComponent = new UnityEngine.UI.Text(textPtr);
                if (textComponent == null) return null;
                string text = textComponent.text;
                return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Reads a pointer to an IL2CPP string at the given offset
        /// and converts it to a managed string using Il2CppStringToManaged.
        /// </summary>
        public static string ReadIL2CppString(IntPtr basePtr, int offset)
        {
            IntPtr stringPtr = ReadPointer(basePtr, offset);
            if (stringPtr == IntPtr.Zero) return null;

            try
            {
                return IL2CPP.Il2CppStringToManaged(stringPtr);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Reads a pointer field using Marshal.ReadIntPtr (safe, no unsafe block needed).
        /// Equivalent to Marshal.ReadIntPtr(basePtr + offset).
        /// </summary>
        public static IntPtr ReadPointerSafe(IntPtr basePtr, int offset)
        {
            if (basePtr == IntPtr.Zero) return IntPtr.Zero;
            return Marshal.ReadIntPtr(basePtr + offset);
        }

        /// <summary>
        /// Reads an IL2CPP List size (int at listPtr + 0x18).
        /// Standard IL2CPP List layout: items at 0x10, size at 0x18.
        /// </summary>
        public static int ReadListSize(IntPtr listPtr)
        {
            if (listPtr == IntPtr.Zero) return 0;
            return Marshal.ReadInt32(listPtr + 0x18);
        }

        /// <summary>
        /// Reads an element pointer from an IL2CPP List at the given index.
        /// Standard IL2CPP List layout: items array at 0x10, elements at items + 0x20 + (index * 8).
        /// </summary>
        public static IntPtr ReadListElement(IntPtr listPtr, int index)
        {
            if (listPtr == IntPtr.Zero) return IntPtr.Zero;
            IntPtr itemsPtr = Marshal.ReadIntPtr(listPtr + 0x10);
            if (itemsPtr == IntPtr.Zero) return IntPtr.Zero;
            int size = Marshal.ReadInt32(listPtr + 0x18);
            if (index < 0 || index >= size) return IntPtr.Zero;
            return Marshal.ReadIntPtr(itemsPtr + 0x20 + (index * 8));
        }
    }
}
