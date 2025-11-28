using System;

namespace Odoo.Core
{
    /// <summary>
    /// The universal identity of a record.
    /// Contains the Environment, the Record ID, and the Model Token.
    /// This struct is passed around by value (16-24 bytes) and avoids boxing.
    /// </summary>
    public readonly struct RecordHandle : IEquatable<RecordHandle>
    {
        public readonly IEnvironment Env;
        public readonly int Id;
        public readonly ModelHandle Model;

        public RecordHandle(IEnvironment env, int id, ModelHandle model)
        {
            Env = env;
            Id = id;
            Model = model;
        }

        /// <summary>
        /// Zero-cost cast to a specific interface wrapper.
        /// </summary>
        public T As<T>() where T : struct, IRecordWrapper
        {
            return new T { Handle = this };
        }

        public override bool Equals(object? obj) => obj is RecordHandle other && Equals(other);
        public bool Equals(RecordHandle other) => Id == other.Id && Model.Token == other.Model.Token && Env == other.Env;
        public override int GetHashCode() => HashCode.Combine(Env, Id, Model);
        public static bool operator ==(RecordHandle left, RecordHandle right) => left.Equals(right);
        public static bool operator !=(RecordHandle left, RecordHandle right) => !left.Equals(right);
        
        public override string ToString() => $"{Model}({Id})";
    }
}