using System;
using System.Runtime.CompilerServices;

namespace Odoo.Core
{
    /// <summary>
    /// Strongly-typed handle for field access using integer tokens.
    /// Eliminates string hashing overhead in cache lookups.
    /// </summary>
    public readonly record struct FieldHandle(int Token)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator int(FieldHandle handle) => handle.Token;

        public override string ToString() => $"Field({Token})";
    }

    /// <summary>
    /// Model token for fast model identification.
    /// Eliminates string-based model lookups.
    /// </summary>
    public readonly record struct ModelHandle(int Token)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator int(ModelHandle handle) => handle.Token;

        public override string ToString() => $"Model({Token})";
    }
}