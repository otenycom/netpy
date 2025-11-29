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
        public readonly RecordId Id;
        public readonly ModelHandle Model;

        public RecordHandle(IEnvironment env, RecordId id, ModelHandle model)
        {
            Env = env;
            Id = id;
            Model = model;
        }

        /// <summary>
        /// Cast to a specific interface type.
        /// With class-based unified wrappers, this returns the same instance from the identity map.
        /// </summary>
        /// <remarks>
        /// This method looks up the identity map to ensure reference equality:
        /// <code>
        /// var p1 = handle.As&lt;IPartnerBase&gt;();
        /// var p2 = handle.As&lt;IPartnerSaleExtension&gt;();
        /// Assert.True(ReferenceEquals(p1, p2)); // Same instance!
        /// </code>
        /// </remarks>
        public T As<T>()
            where T : class, IOdooRecord
        {
            // Try to get from identity map first
            if (
                Env is OdooEnvironment odooEnv
                && odooEnv.TryGetFromIdentityMap(Model, Id, out var existing)
                && existing is T typed
            )
            {
                return typed;
            }

            // Fallback: If we have a concrete OdooEnvironment, use GetRecord
            if (Env is OdooEnvironment env)
            {
                // This will create and cache the instance
                return env.GetRecordByToken<T>(Model, Id);
            }

            throw new InvalidOperationException(
                "Cannot use As<T>() without a proper OdooEnvironment with identity map support."
            );
        }

        public override bool Equals(object? obj) => obj is RecordHandle other && Equals(other);

        public bool Equals(RecordHandle other) =>
            Id == other.Id && Model.Token == other.Model.Token && Env == other.Env;

        public override int GetHashCode() => HashCode.Combine(Env, Id.Value, Model);

        public static bool operator ==(RecordHandle left, RecordHandle right) => left.Equals(right);

        public static bool operator !=(RecordHandle left, RecordHandle right) =>
            !left.Equals(right);

        public override string ToString() => $"{Model}({Id})";
    }
}
