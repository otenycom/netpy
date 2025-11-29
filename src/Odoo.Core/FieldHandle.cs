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

    /// <summary>
    /// Strongly-typed wrapper for record IDs.
    /// Uses long backing type to support large databases.
    /// Provides type safety and enables future migrations.
    /// </summary>
    public readonly record struct RecordId(long Value)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator long(RecordId id) => id.Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator RecordId(long value) => new(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator RecordId(int value) => new(value);

        public bool IsValid => Value > 0;
        public static readonly RecordId Empty = new(0);

        public override string ToString() => Value.ToString();
    }
}
