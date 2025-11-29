using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Odoo.Core
{
    /// <summary>
    /// Strongly-typed handle for field access using integer tokens.
    /// Eliminates string hashing overhead in cache lookups.
    /// </summary>
    /// <remarks>
    /// Equality is based solely on Token, not Name. The Name is for debugging purposes only.
    /// </remarks>
    [DebuggerDisplay("{DebugDisplay,nq}")]
    public readonly struct FieldHandle : IEquatable<FieldHandle>
    {
        public readonly long Token;
        public readonly string? Name;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FieldHandle(long token)
        {
            Token = token;
            Name = null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FieldHandle(long token, string name)
        {
            Token = token;
            Name = name;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator long(FieldHandle handle) => handle.Token;

        public bool Equals(FieldHandle other) => Token == other.Token;

        public override bool Equals(object? obj) => obj is FieldHandle other && Equals(other);

        public override int GetHashCode() => Token.GetHashCode();

        public static bool operator ==(FieldHandle left, FieldHandle right) =>
            left.Token == right.Token;

        public static bool operator !=(FieldHandle left, FieldHandle right) =>
            left.Token != right.Token;

        private string DebugDisplay => Name ?? $"Field({Token})";

        public override string ToString() => Name ?? $"Field({Token})";
    }

    /// <summary>
    /// Model token for fast model identification.
    /// Eliminates string-based model lookups.
    /// </summary>
    /// <remarks>
    /// Equality is based solely on Token, not Name. The Name is for debugging purposes only.
    /// </remarks>
    [DebuggerDisplay("{DebugDisplay,nq}")]
    public readonly struct ModelHandle : IEquatable<ModelHandle>
    {
        public readonly long Token;
        public readonly string? Name;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ModelHandle(long token)
        {
            Token = token;
            Name = null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ModelHandle(long token, string name)
        {
            Token = token;
            Name = name;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator long(ModelHandle handle) => handle.Token;

        public bool Equals(ModelHandle other) => Token == other.Token;

        public override bool Equals(object? obj) => obj is ModelHandle other && Equals(other);

        public override int GetHashCode() => Token.GetHashCode();

        public static bool operator ==(ModelHandle left, ModelHandle right) =>
            left.Token == right.Token;

        public static bool operator !=(ModelHandle left, ModelHandle right) =>
            left.Token != right.Token;

        private string DebugDisplay => Name ?? $"Model({Token})";

        public override string ToString() => Name ?? $"Model({Token})";
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
